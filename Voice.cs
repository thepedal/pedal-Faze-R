using System;

namespace PedalFazeR
{
    // Per-buffer resolved control values, filled by the machine and read by
    // every active voice. Passed by 'in' to avoid copying.
    internal struct RenderCtx
    {
        public int     Sr;
        public int     Os;            // oversample factor 1/2/4

        public PDWave  Wave1, Wave2;
        public float   Lvl1, Lvl2;    // 0..1
        public float   Dcw1Base, Dcw2Base;   // 0..1
        public float   Tune1, Tune2;  // semitone offset (oct+semi+fine) per osc
        public int     Mode;          // 0 mix, 1 ring, 2 sync

        public float   DcwEnvDepth;   // 0..1
        public float   DcwLfoDepth;   // 0..1 (in DCW terms)
        public float   DcwVel;        // 0..1 (fraction of env depth scaled by velocity)
        public float   PitchEnvDepth; // semitones, signed
        public float   LfoPitch;      // semitones
        public float   LfoAmp;        // 0..1 tremolo depth
        public LfoWave LfoWaveform;
        public float   LfoInc;        // per output sample

        public float   ToneFcBase;    // Hz at note 60
        public float   ToneTrack;     // 0..1 key-follow
        public float   ToneRes;       // 0..1

        public float   AmpVel;        // 0..1
        public float   PortaCoef;     // 0 = instant glide

        // v1.1
        public float   DcwTrackAmt;   // [-1,1] DCW key-tracking amount
        public float   NoiseLevel;    // 0..1

        // v1.2
        public bool    Dcw2EnvOn;     // osc2 uses its own DCW envelope
        public float   Dcw2EnvDepth;  // 0..1
        public LfoWave Lfo2Waveform;
        public float   Lfo2Inc;       // per output sample
        public float   Lfo2Pitch;     // semitones
        public float   Lfo2DcwDepth;  // 0..1
        public float   Lfo2Amp;       // 0..1 tremolo depth

        // v1.4
        public int     FilterType;    // 0 LP, 1 BP, 2 HP, 3 Notch
        public float   FiltEnvAmt;    // [-1,1] cutoff sweep amount
    }

    internal sealed class Voice
    {
        public readonly PDOsc      Osc1     = new PDOsc();
        public readonly PDOsc      Osc2     = new PDOsc();
        public readonly Adsr       AmpEnv   = new Adsr();
        public readonly Adsr       DcwEnv   = new Adsr();
        public readonly Adsr       DcwEnv2  = new Adsr();
        public readonly Adsr       FiltEnv  = new Adsr();
        public readonly AdEnv      PitchEnv = new AdEnv();
        public readonly Lfo        Lfo;
        public readonly Lfo        Lfo2;
        public readonly TptSvf     Filt     = new TptSvf();
        public readonly Decimator  Dec      = new Decimator();
        public readonly Noise      Noise;

        public float TargetMidi = 60f, CurrentMidi = 60f;
        public float Velocity   = 100f;     // 0..127, read live

        const float FILT_ENV_OCT = 6f;      // filter envelope sweeps cutoff up to ±6 octaves
        float _fcSmooth = -1f;              // one-pole smoothed cutoff (-1 = snap on next note)
        float _resSmooth;                   // smoothed resonance — blooms from 0 each fresh note

        // Pending events drained at the top of the machine's Work (SH101 §6.3).
        public bool  HasNoteOn, HasNoteOff;
        public byte  PendingNote;

        public Voice(uint seed)
        {
            Lfo   = new Lfo(seed);
            Lfo2  = new Lfo(seed * 0x85EBCA6Bu + 0x165667B1u);   // decorrelated S&H
            Noise = new Noise(seed ^ 0x5BD1E995u);
        }

        public bool IsActive => AmpEnv.IsActive;

        public void QueueNoteOn(byte n) { PendingNote = n; HasNoteOn = true; }
        public void QueueNoteOff()      { HasNoteOff = true; }

        public void NoteOn(int lfoDelaySamps, int lfo2DelaySamps)
        {
            byte b   = PendingNote;
            int  oct = b >> 4;
            int  semi = (b & 0xF) - 1;
            int  midi = oct * 12 + semi;

            bool wasIdle = !AmpEnv.IsActive;     // capture before NoteOn (SH101 §6.1)
            TargetMidi = midi;
            if (wasIdle)
            {
                CurrentMidi = midi;              // snap — no glide from rest
                // Silent moment: safe to reset state with no click (M1 §5).
                Osc1.Reset(); Osc2.Reset();
                Filt.Reset(); Dec.Reset(); Noise.Reset();
                _fcSmooth = -1f;                 // snap filter cutoff to target on this fresh note
                _resSmooth = 0f;                 // and bloom resonance up from zero (no onset ping)
            }
            AmpEnv.NoteOn();
            DcwEnv.NoteOn();
            DcwEnv2.NoteOn();
            FiltEnv.NoteOn();
            PitchEnv.NoteOn();
            Lfo.NoteOn(lfoDelaySamps);
            Lfo2.NoteOn(lfo2DelaySamps);
        }

        public void NoteOff()
        {
            AmpEnv.NoteOff();
            DcwEnv.NoteOff();
            DcwEnv2.NoteOff();
            FiltEnv.NoteOff();
            // Pitch AD env is one-shot; let it finish.
        }

        public void ForcedRelease(int sr)
        {
            AmpEnv.ForcedRelease(sr);
            DcwEnv.ForcedRelease(sr);
            DcwEnv2.ForcedRelease(sr);
            FiltEnv.ForcedRelease(sr);
        }

        public void Render(in RenderCtx c, float[] mono, int n)
        {
            if (!AmpEnv.IsActive) return;
            Dec.Configure(c.Os);
            int   os    = c.Os;
            float invOs = 1f / os;
            float velN  = Velocity * (1f / 127f);
            float fcStep = 1f - DspMath.Coef(0.003f, c.Sr);   // ~3 ms cutoff smoothing

            for (int i = 0; i < n; i++)
            {
                // glide
                if (c.PortaCoef > 0f && CurrentMidi != TargetMidi)
                {
                    CurrentMidi = TargetMidi + (CurrentMidi - TargetMidi) * c.PortaCoef;
                    if (MathF.Abs(CurrentMidi - TargetMidi) < 1e-3f) CurrentMidi = TargetMidi;
                }
                else CurrentMidi = TargetMidi;

                // control-rate sources (one per output sample)
                float aenv  = AmpEnv.Tick();
                float denv  = DcwEnv.Tick();
                float denv2 = DcwEnv2.Tick();      // always ticked (cheap); used if engaged
                float fenv  = FiltEnv.Tick();
                float penv  = PitchEnv.Tick();
                float lfo   = Lfo.Tick(c.LfoWaveform, c.LfoInc);
                float lfo2  = Lfo2.Tick(c.Lfo2Waveform, c.Lfo2Inc);

                // effective DCW — env per osc (osc2 optionally on its own envelope),
                // plus shared key-track and both LFOs' DCW routing.
                float velScale   = 1f - c.DcwVel + c.DcwVel * velN;
                float dcwEnvAmt  = c.DcwEnvDepth  * velScale;
                float dcwEnv2Amt = c.Dcw2EnvDepth * velScale;
                float dcwTrack   = c.DcwTrackAmt * ((CurrentMidi - 60f) / 12f) * 0.4f;
                float dcwLfo     = c.DcwLfoDepth * lfo + c.Lfo2DcwDepth * lfo2;
                float dMod1 = dcwEnvAmt * denv + dcwLfo + dcwTrack;
                float dMod2 = (c.Dcw2EnvOn ? dcwEnv2Amt * denv2 : dcwEnvAmt * denv) + dcwLfo + dcwTrack;
                float d1 = c.Dcw1Base + dMod1; d1 = d1 < 0f ? 0f : (d1 > 0.999f ? 0.999f : d1);
                float d2 = c.Dcw2Base + dMod2; d2 = d2 < 0f ? 0f : (d2 > 0.999f ? 0.999f : d2);

                // pitch
                float pitchMod = penv * c.PitchEnvDepth + lfo * c.LfoPitch + lfo2 * c.Lfo2Pitch;
                float baseMidi = CurrentMidi + pitchMod;
                float f1 = DspMath.MidiToHz(baseMidi + c.Tune1);
                float f2 = DspMath.MidiToHz(baseMidi + c.Tune2);

                // Resonant shapes: cap DCW so the formant carrier (r·f) can't
                // climb past Nyquist and alias into top-end crackle (v1.0.1).
                if (PDOsc.IsReso(c.Wave1)) { float m = PDOsc.MaxResoDcw(f1, c.Sr); if (d1 > m) d1 = m; }
                if (PDOsc.IsReso(c.Wave2)) { float m = PDOsc.MaxResoDcw(f2, c.Sr); if (d2 > m) d2 = m; }

                Osc1.Inc = (f1 / c.Sr) * invOs;
                Osc2.Inc = (f2 / c.Sr) * invOs;

                // oversampled oscillator section
                for (int s = 0; s < os; s++)
                {
                    float a = Osc1.Tick(c.Wave1, d1);
                    if (c.Mode == 2 && Osc1.Wrapped) Osc2.Phase = 0f;     // hard sync
                    float b = Osc2.Tick(c.Wave2, d2);

                    float mix = (c.Mode == 1) ? a * b                     // ring mod
                                              : a * c.Lvl1 + b * c.Lvl2;  // mix
                    Dec.Push(mix);
                }
                float monoS = Dec.Read();
                if (c.NoiseLevel > 0f) monoS += Noise.Tick() * c.NoiseLevel;

                // filter — cutoff computed per sample and one-pole smoothed (v1.4.1) so a
                // fast/resonant sweep can't zip and an instant filter-env attack can't slam
                // the cutoff open (which made a high-Q filter ping at ~cutoff on note onset).
                float toneFc = c.ToneFcBase;
                if (c.ToneTrack > 0f)
                    toneFc *= DspMath.FastPow2(((CurrentMidi - 60f) / 12f) * c.ToneTrack);
                if (c.FiltEnvAmt != 0f)
                    toneFc *= DspMath.FastPow2(c.FiltEnvAmt * fenv * FILT_ENV_OCT);
                float nyq = c.Sr * 0.49f;
                if (toneFc > nyq) toneFc = nyq;
                if (toneFc < 20f) toneFc = 20f;
                if (_fcSmooth < 0f) _fcSmooth = toneFc;               // snap on fresh note
                else _fcSmooth += (toneFc - _fcSmooth) * fcStep;      // ~3 ms glide
                _resSmooth += (c.ToneRes - _resSmooth) * fcStep;      // resonance blooms in (~3 ms)
                Filt.UpdateCoefs(_fcSmooth, _resSmooth, c.Sr);        // cache skips when steady
                float filtered = Filt.Process(monoS, c.FilterType);

                // amp + tremolo (both LFOs)
                float amp = aenv * (1f - c.AmpVel + c.AmpVel * velN);
                if (c.LfoAmp  > 0f) amp *= 1f - c.LfoAmp  * 0.5f * (1f - lfo);
                if (c.Lfo2Amp > 0f) amp *= 1f - c.Lfo2Amp * 0.5f * (1f - lfo2);

                mono[i] += filtered * amp;
            }
        }
    }
}
