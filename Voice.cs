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
    }

    internal sealed class Voice
    {
        public readonly PDOsc      Osc1     = new PDOsc();
        public readonly PDOsc      Osc2     = new PDOsc();
        public readonly Adsr       AmpEnv   = new Adsr();
        public readonly Adsr       DcwEnv   = new Adsr();
        public readonly AdEnv      PitchEnv = new AdEnv();
        public readonly Lfo        Lfo;
        public readonly TptLowpass Tone     = new TptLowpass();
        public readonly Decimator  Dec      = new Decimator();

        public float TargetMidi = 60f, CurrentMidi = 60f;
        public float Velocity   = 100f;     // 0..127, read live

        // Pending events drained at the top of the machine's Work (SH101 §6.3).
        public bool  HasNoteOn, HasNoteOff;
        public byte  PendingNote;

        public Voice(uint seed) { Lfo = new Lfo(seed); }

        public bool IsActive => AmpEnv.IsActive;

        public void QueueNoteOn(byte n) { PendingNote = n; HasNoteOn = true; }
        public void QueueNoteOff()      { HasNoteOff = true; }

        public void NoteOn(int lfoDelaySamps)
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
                Tone.Reset(); Dec.Reset();
            }
            AmpEnv.NoteOn();
            DcwEnv.NoteOn();
            PitchEnv.NoteOn();
            Lfo.NoteOn(lfoDelaySamps);
        }

        public void NoteOff()
        {
            AmpEnv.NoteOff();
            DcwEnv.NoteOff();
            // Pitch AD env is one-shot; let it finish.
        }

        public void ForcedRelease(int sr)
        {
            AmpEnv.ForcedRelease(sr);
            DcwEnv.ForcedRelease(sr);
        }

        public void Render(in RenderCtx c, float[] mono, int n)
        {
            if (!AmpEnv.IsActive) return;
            Dec.Configure(c.Os);
            int   os    = c.Os;
            float invOs = 1f / os;
            float velN  = Velocity * (1f / 127f);

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
                float aenv = AmpEnv.Tick();
                float denv = DcwEnv.Tick();
                float penv = PitchEnv.Tick();
                float lfo  = Lfo.Tick(c.LfoWaveform, c.LfoInc);

                // effective DCW (envelope depth scaled by velocity + LFO wobble)
                float dcwEnvAmt = c.DcwEnvDepth * (1f - c.DcwVel + c.DcwVel * velN);
                float dMod = dcwEnvAmt * denv + c.DcwLfoDepth * lfo;
                float d1 = c.Dcw1Base + dMod; d1 = d1 < 0f ? 0f : (d1 > 0.999f ? 0.999f : d1);
                float d2 = c.Dcw2Base + dMod; d2 = d2 < 0f ? 0f : (d2 > 0.999f ? 0.999f : d2);

                // pitch
                float pitchMod = penv * c.PitchEnvDepth + lfo * c.LfoPitch;
                float baseMidi = CurrentMidi + pitchMod;
                float f1 = DspMath.MidiToHz(baseMidi + c.Tune1);
                float f2 = DspMath.MidiToHz(baseMidi + c.Tune2);
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

                // tone filter (control-rate coefficient updates — M1 §9, gate on i)
                if ((i & 15) == 0)
                {
                    float toneFc = c.ToneFcBase;
                    if (c.ToneTrack > 0f)
                        toneFc *= DspMath.FastPow2(((CurrentMidi - 60f) / 12f) * c.ToneTrack);
                    float nyq = c.Sr * 0.49f;
                    if (toneFc > nyq) toneFc = nyq;
                    if (toneFc < 20f) toneFc = 20f;
                    Tone.UpdateCoefs(toneFc, c.ToneRes, c.Sr);
                }
                float filtered = Tone.Process(monoS);

                // amp + tremolo
                float amp = aenv * (1f - c.AmpVel + c.AmpVel * velN);
                if (c.LfoAmp > 0f)
                    amp *= 1f - c.LfoAmp * 0.5f * (1f - lfo);  // lfo -1..1 -> dip to (1-depth)

                mono[i] += filtered * amp;
            }
        }
    }
}
