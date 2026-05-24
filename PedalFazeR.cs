using System;
using System.Linq;
using System.Reflection;
using System.Collections.Concurrent;
using Buzz.MachineInterface;   // IBuzzMachine, IBuzzMachineHost, MachineDecl, ParameterDecl, Note, Sample, WorkModes
using BuzzGUI.Interfaces;      // IMachine, IBuzz, IParameter, ParameterType

namespace PedalFazeR
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Pedal Faze-R  —  8-voice polyphonic phase-distortion synthesiser.
    //
    //  A modern take on the Casio CZ phase-distortion engine: each voice has two
    //  PD oscillators (mix / ring / sync) whose harmonic content is shaped by a
    //  dedicated DCW (wave) envelope rather than a filter — the brightness comes
    //  from distorting the phase, which is what makes PD sound like PD. An amp
    //  envelope (DCA), a one-shot pitch envelope (DCO), one LFO, a gentle tone
    //  filter, and selectable oversampling round it out.
    //
    //  Track index == voice index (Core §14 / M1 §1). Polyphony comes from notes
    //  on multiple pattern tracks at the same row, recovered via the multi-track
    //  pvalues poll below.
    // ─────────────────────────────────────────────────────────────────────────
    [MachineDecl(Name = "Pedal Faze-R", ShortName = "Faze-R",
                 Author = "thepedal", MaxTracks = 8)]
    public sealed class PedalFazeRMachine : IBuzzMachine
    {
        public const int MAX_VOICES = 8;

        const float SAMPLE_SCALE = 32768f;        // Buzz nominal generator range (PedalComp §1)
        const float PITCH_ENV_MAX = 24f;          // pitch env reaches ±2 octaves at the extremes
        // v1.0.1 — fixed polyphonic headroom (M1 §10). A single voice sits well
        // below the soft-clip knee so chords sum into the clean/linear region;
        // the soft clip is only a safety net for dense, loud chords. Trade: per-
        // voice level is lower — set output level with Volume / the master.
        const float MIX_HEADROOM = 0.4f;          // ~-8 dB

        readonly IBuzzMachineHost host;
        readonly Voice[] _voices = new Voice[MAX_VOICES];
        float[] _mono = new float[8192];
        bool _wasPlaying;
        int _lastSr;

        public PedalFazeRMachine(IBuzzMachineHost host)
        {
            this.host = host;
            for (int i = 0; i < MAX_VOICES; i++)
                _voices[i] = new Voice(0xCAFE0001u + (uint)i * 0x9E3779B9u);
        }

        // ── Global parameters (group 1). DECLARATION ORDER IS THE PRESET ───────
        // ── CONTRACT (Build §3.3): only ever append; never reorder or insert. ──

        // OSC1
        [ParameterDecl(Name = "OSC1 Wave", MinValue = 0, MaxValue = 7, DefValue = 1,
            ValueDescriptions = new[] { "Sine", "Saw", "Square", "Pulse", "Reso Saw", "Reso Tri", "Reso Trap", "Saw-Pulse" })]
        public int Osc1Wave { get; set; } = 1;
        [ParameterDecl(Name = "OSC1 Octave", MinValue = 0, MaxValue = 4, DefValue = 2,
            ValueDescriptions = new[] { "-2", "-1", "0", "+1", "+2" })]
        public int Osc1Octave { get; set; } = 2;
        [ParameterDecl(Name = "OSC1 Semi", MinValue = 0, MaxValue = 24, DefValue = 12)]
        public int Osc1Semi { get; set; } = 12;
        [ParameterDecl(Name = "OSC1 Fine", MinValue = 0, MaxValue = 100, DefValue = 50)]
        public int Osc1Fine { get; set; } = 50;
        [ParameterDecl(Name = "OSC1 DCW", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Osc1Dcw { get; set; } = 0;
        [ParameterDecl(Name = "OSC1 Level", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public int Osc1Level { get; set; } = 100;

        // OSC2 (off by default — single-oscillator patch out of the box)
        [ParameterDecl(Name = "OSC2 Wave", MinValue = 0, MaxValue = 7, DefValue = 1,
            ValueDescriptions = new[] { "Sine", "Saw", "Square", "Pulse", "Reso Saw", "Reso Tri", "Reso Trap", "Saw-Pulse" })]
        public int Osc2Wave { get; set; } = 1;
        [ParameterDecl(Name = "OSC2 Octave", MinValue = 0, MaxValue = 4, DefValue = 2,
            ValueDescriptions = new[] { "-2", "-1", "0", "+1", "+2" })]
        public int Osc2Octave { get; set; } = 2;
        [ParameterDecl(Name = "OSC2 Semi", MinValue = 0, MaxValue = 24, DefValue = 12)]
        public int Osc2Semi { get; set; } = 12;
        [ParameterDecl(Name = "OSC2 Fine", MinValue = 0, MaxValue = 100, DefValue = 50)]
        public int Osc2Fine { get; set; } = 50;
        [ParameterDecl(Name = "OSC2 DCW", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Osc2Dcw { get; set; } = 0;
        [ParameterDecl(Name = "OSC2 Level", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Osc2Level { get; set; } = 0;

        [ParameterDecl(Name = "Osc Mode", MinValue = 0, MaxValue = 2, DefValue = 0,
            ValueDescriptions = new[] { "Mix", "Ring", "Sync" })]
        public int OscMode { get; set; } = 0;
        [ParameterDecl(Name = "Portamento", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Portamento { get; set; } = 0;

        // DCW (wave) envelope — the harmonic-shaping envelope, Faze-R's heart
        [ParameterDecl(Name = "DCW Attack", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int DcwAttack { get; set; } = 0;
        [ParameterDecl(Name = "DCW Decay", MinValue = 0, MaxValue = 127, DefValue = 70)]
        public int DcwDecay { get; set; } = 70;
        [ParameterDecl(Name = "DCW Sustain", MinValue = 0, MaxValue = 127, DefValue = 80)]
        public int DcwSustain { get; set; } = 80;
        [ParameterDecl(Name = "DCW Release", MinValue = 0, MaxValue = 127, DefValue = 50)]
        public int DcwRelease { get; set; } = 50;
        [ParameterDecl(Name = "DCW Env Amt", MinValue = 0, MaxValue = 127, DefValue = 90)]
        public int DcwEnvAmt { get; set; } = 90;
        [ParameterDecl(Name = "DCW Vel", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int DcwVel { get; set; } = 0;

        // Amp (DCA) envelope
        [ParameterDecl(Name = "Amp Attack", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int AmpAttack { get; set; } = 0;
        [ParameterDecl(Name = "Amp Decay", MinValue = 0, MaxValue = 127, DefValue = 70)]
        public int AmpDecay { get; set; } = 70;
        [ParameterDecl(Name = "Amp Sustain", MinValue = 0, MaxValue = 127, DefValue = 110)]
        public int AmpSustain { get; set; } = 110;
        [ParameterDecl(Name = "Amp Release", MinValue = 0, MaxValue = 127, DefValue = 35)]
        public int AmpRelease { get; set; } = 35;
        [ParameterDecl(Name = "Amp Vel", MinValue = 0, MaxValue = 127, DefValue = 70)]
        public int AmpVel { get; set; } = 70;

        // Pitch (DCO) one-shot AD envelope, bipolar depth (64 = none)
        [ParameterDecl(Name = "Pitch Attack", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int PitchAttack { get; set; } = 0;
        [ParameterDecl(Name = "Pitch Decay", MinValue = 0, MaxValue = 127, DefValue = 40)]
        public int PitchDecay { get; set; } = 40;
        [ParameterDecl(Name = "Pitch Depth", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int PitchDepth { get; set; } = 64;

        // LFO
        [ParameterDecl(Name = "LFO Wave", MinValue = 0, MaxValue = 4, DefValue = 0,
            ValueDescriptions = new[] { "Tri", "Saw", "Square", "S&H", "Sine" })]
        public int LfoWaveSel { get; set; } = 0;
        [ParameterDecl(Name = "LFO Rate", MinValue = 0, MaxValue = 127, DefValue = 50)]
        public int LfoRate { get; set; } = 50;
        [ParameterDecl(Name = "LFO Delay", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int LfoDelay { get; set; } = 0;
        [ParameterDecl(Name = "LFO Pitch", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int LfoPitch { get; set; } = 0;
        [ParameterDecl(Name = "LFO DCW", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int LfoDcw { get; set; } = 0;
        [ParameterDecl(Name = "LFO Amp", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int LfoAmp { get; set; } = 0;

        // Tone (gentle post-filter) + output
        [ParameterDecl(Name = "Tone", MinValue = 0, MaxValue = 127, DefValue = 127)]
        public int Tone { get; set; } = 127;
        [ParameterDecl(Name = "Tone Track", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int ToneTrack { get; set; } = 0;
        [ParameterDecl(Name = "Tone Res", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int ToneRes { get; set; } = 0;
        [ParameterDecl(Name = "Oversample", MinValue = 0, MaxValue = 2, DefValue = 1,
            ValueDescriptions = new[] { "Off", "2x", "4x" })]
        public int Oversample { get; set; } = 1;
        [ParameterDecl(Name = "Volume", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public int Volume { get; set; } = 100;

        // ── New in v1.1 — appended so v1.0.x preset indices stay valid (Build §3.3) ──
        [ParameterDecl(Name = "DCW Track", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int DcwTrack { get; set; } = 64;            // 64 = none; bipolar (brighter up / inverse down)
        [ParameterDecl(Name = "LFO Sync", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Free", "Sync" })]
        public int LfoSync { get; set; } = 0;
        [ParameterDecl(Name = "LFO Division", MinValue = 0, MaxValue = 7, DefValue = 2,
            ValueDescriptions = new[] { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" })]
        public int LfoDivision { get; set; } = 2;
        [ParameterDecl(Name = "Noise Level", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int NoiseLevel { get; set; } = 0;

        // LFO cycles-per-beat for each division (beat = quarter note). Shared by both LFOs.
        static readonly float[] _syncCyc = { 0.25f, 0.5f, 1f, 2f, 3f, 4f, 6f, 8f };

        // ── New in v1.2 — appended so v1.0.x/v1.1 preset indices stay valid (Build §3.3) ──
        // Per-osc DCW envelope: osc2 can run its own DCW (wave) envelope.
        [ParameterDecl(Name = "DCW2 Env", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Off", "On" })]
        public int Dcw2Env { get; set; } = 0;          // Off = osc2 follows envelope 1 (back-compat)
        [ParameterDecl(Name = "DCW2 Attack", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Dcw2Attack { get; set; } = 0;
        [ParameterDecl(Name = "DCW2 Decay", MinValue = 0, MaxValue = 127, DefValue = 70)]
        public int Dcw2Decay { get; set; } = 70;
        [ParameterDecl(Name = "DCW2 Sustain", MinValue = 0, MaxValue = 127, DefValue = 80)]
        public int Dcw2Sustain { get; set; } = 80;
        [ParameterDecl(Name = "DCW2 Release", MinValue = 0, MaxValue = 127, DefValue = 50)]
        public int Dcw2Release { get; set; } = 50;
        [ParameterDecl(Name = "DCW2 Env Amt", MinValue = 0, MaxValue = 127, DefValue = 90)]
        public int Dcw2EnvAmt { get; set; } = 90;

        // Second LFO (free or tempo-synced), routable to pitch / DCW / amp.
        [ParameterDecl(Name = "LFO2 Wave", MinValue = 0, MaxValue = 4, DefValue = 0,
            ValueDescriptions = new[] { "Tri", "Saw", "Square", "S&H", "Sine" })]
        public int Lfo2Wave { get; set; } = 0;
        [ParameterDecl(Name = "LFO2 Rate", MinValue = 0, MaxValue = 127, DefValue = 50)]
        public int Lfo2Rate { get; set; } = 50;
        [ParameterDecl(Name = "LFO2 Sync", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Free", "Sync" })]
        public int Lfo2Sync { get; set; } = 0;
        [ParameterDecl(Name = "LFO2 Division", MinValue = 0, MaxValue = 7, DefValue = 2,
            ValueDescriptions = new[] { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" })]
        public int Lfo2Division { get; set; } = 2;
        [ParameterDecl(Name = "LFO2 Delay", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Lfo2Delay { get; set; } = 0;
        [ParameterDecl(Name = "LFO2 Pitch", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Lfo2Pitch { get; set; } = 0;
        [ParameterDecl(Name = "LFO2 DCW", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Lfo2Dcw { get; set; } = 0;
        [ParameterDecl(Name = "LFO2 Amp", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Lfo2Amp { get; set; } = 0;

        // Free or tempo-locked LFO increment per output sample.
        float LfoIncFor(int sync, int rateParam, int division, int sr)
        {
            if (sync == 1)
            {
                float bpm = host?.MasterInfo?.BeatsPerMin ?? 120f;
                if (bpm < 1f) bpm = 120f;
                int div = (division >= 0 && division < _syncCyc.Length) ? division : 2;
                return (bpm / 60f) * _syncCyc[div] / sr;
            }
            return DspMath.TimeMap(rateParam, 0.05f, 30f) / sr;   // free, 0.05..30 Hz
        }

        // ── Track parameters (group 2) ─────────────────────────────────────────
        // Note is the trigger (stateless = each row is an event, not a held value).
        [ParameterDecl(IsStateless = true)]
        public void SetNote(Note value, int track)
        {
            if ((uint)track < (uint)MAX_VOICES)          // untrusted index (PedalInvFFT §24)
            {
                byte v = value.Value;
                if (v == Note.Off)   _voices[track].QueueNoteOff();
                else if (v != 0)     _voices[track].QueueNoteOn(v);
            }
            EnsurePoll();
            PollSiblings(track);                         // recover the rest of a chord row (Core §14)
        }

        // Velocity is a held per-track value (0..127). Default 100 in the Voice.
        [ParameterDecl(Name = "Velocity", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public void SetVelocity(int value, int track)
        {
            if ((uint)track < (uint)MAX_VOICES && (uint)value <= 127)
                _voices[track].Velocity = value;
            EnsurePoll();
            PollSiblings(track);
        }

        // ── Multi-track simultaneous-delivery recovery (Core §14) ──────────────
        // parametersChanged is keyed by parameter, not (parameter, track), so a
        // chord row delivers only the last track's SetNote/SetVelocity. Recover
        // the siblings by reading the raw pvalues before the post-tick reset.
        IParameter _noteParam, _velParam;
        Func<int, int> _noteRead, _velRead;
        bool _pollInit;

        void EnsurePoll()
        {
            if (_pollInit) return;
            try
            {
                var pg = host?.Machine?.ParameterGroups;        // not populated until after ctor (Core §15)
                if (pg == null || pg.Count < 3) return;         // retry on a later setter call
                var tg = pg[2];
                _noteParam = tg.Parameters.FirstOrDefault(p => p?.Type == ParameterType.Note);
                _velParam  = tg.Parameters.FirstOrDefault(p => p != null && p.Name == "Velocity");
                _noteRead  = MakeReader(_noteParam);
                _velRead   = MakeReader(_velParam);
                _pollInit  = true;
            }
            catch { /* leave readers null; chords degrade to last-track-only, audio keeps running */ }
        }

        // Shape-tolerant pvalues reader (Tracker §16.3): pvalues changed from
        // ConcurrentDictionary<int,int> (ReBuzz ≤1818) to int[256] (≥1827). A bare
        // `as` cast silently nulls on the wrong shape — detect at runtime instead.
        static Func<int, int> MakeReader(IParameter p)
        {
            if (p == null) return null;
            var fi = p.GetType().GetField("pvalues", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) return null;
            object raw = fi.GetValue(p);
            if (raw == null) return null;
            int noValue = p.NoValue;

            if (raw is int[] arr)
                return t => ((uint)t < (uint)arr.Length) ? arr[t] : noValue;
            if (raw is ConcurrentDictionary<int, int> dict)
                return t => dict.TryGetValue(t, out int v) ? v : noValue;
            if (raw is System.Collections.IDictionary idict)
                return t => idict.Contains(t) ? Convert.ToInt32(idict[t]) : noValue;
            return null;
        }

        void PollSiblings(int firedTrack)
        {
            if (_noteRead != null)
            {
                int noVal = _noteParam.NoValue;             // 0 for Note type
                for (int t = 0; t < MAX_VOICES; t++)
                {
                    if (t == firedTrack) continue;
                    var v = _voices[t];
                    if (v.HasNoteOn || v.HasNoteOff) continue;   // a real setter already handled it
                    int pv = _noteRead(t);
                    if (pv != noVal && pv != 0)
                    {
                        if (pv == Note.Off) v.QueueNoteOff();
                        else                v.QueueNoteOn((byte)pv);
                    }
                }
            }
            if (_velRead != null)
            {
                int noVal = _velParam.NoValue;              // velocity is a held value — read all tracks
                for (int t = 0; t < MAX_VOICES; t++)
                {
                    int pv = _velRead(t);
                    if (pv != noVal && (uint)pv <= 127) _voices[t].Velocity = pv;
                }
            }
        }

        // ── Audio ──────────────────────────────────────────────────────────────
        public bool Work(Sample[] output, int n, WorkModes mode)
        {
            int sr = host?.MasterInfo?.SamplesPerSec ?? 48000;
            if (sr < 8000) sr = 48000;

            // Sample-rate change (Core §29) — reset filter/decimator state on real changes.
            if (sr != _lastSr)
            {
                foreach (var v in _voices) { v.Tone.Reset(); v.Dec.Reset(); v.Noise.Reset(); }
                _lastSr = sr;
            }

            // Transport stop (Core §27): on the falling edge of Playing, fast-fade
            // every voice so long-release patches don't ring forever after Stop.
            bool nowPlaying = _wasPlaying;
            try { nowPlaying = host?.Machine?.Graph?.Buzz?.Playing ?? false; } catch { }
            if (_wasPlaying && !nowPlaying)
                foreach (var v in _voices) v.ForcedRelease(sr);
            _wasPlaying = nowPlaying;

            // Drain pending note events (note-on before note-off — SH101 §6.3).
            int lfoDelay  = (int)(DspMath.TimeMap(LfoDelay,  0.0001f, 3f) * sr);
            int lfo2Delay = (int)(DspMath.TimeMap(Lfo2Delay, 0.0001f, 3f) * sr);
            for (int i = 0; i < MAX_VOICES; i++)
            {
                var v = _voices[i];
                if (v.HasNoteOn)  { v.NoteOn(lfoDelay, lfo2Delay); v.HasNoteOn = false; }
                if (v.HasNoteOff) { v.NoteOff();                   v.HasNoteOff = false; }
            }

            // Resolve per-buffer control context + push envelope coefficients.
            RenderCtx c = BuildCtx(sr);
            PushEnvCoefs(sr);

            if (n > _mono.Length) _mono = new float[n];   // rare; buffers are ≤256 (Core §34)
            Array.Clear(_mono, 0, n);

            bool any = false;
            for (int i = 0; i < MAX_VOICES; i++)
            {
                if (!_voices[i].IsActive) continue;
                _voices[i].Render(in c, _mono, n);
                any = true;
            }

            if (!any)                                     // fully idle — let downstream sleep
            {
                for (int i = 0; i < n; i++) { output[i].L = 0f; output[i].R = 0f; }
                return false;
            }

            float volN = Volume * (1f / 127f);
            for (int i = 0; i < n; i++)
            {
                float s = DspMath.SoftClip(_mono[i] * MIX_HEADROOM * volN) * SAMPLE_SCALE;
                output[i].L = s; output[i].R = s;
            }
            return true;
        }

        RenderCtx BuildCtx(int sr)
        {
            RenderCtx c = default;
            c.Sr = sr;
            c.Os = Oversample == 2 ? 4 : (Oversample == 1 ? 2 : 1);

            c.Wave1 = (PDWave)Osc1Wave; c.Wave2 = (PDWave)Osc2Wave;
            c.Lvl1  = Osc1Level * (1f / 127f); c.Lvl2 = Osc2Level * (1f / 127f);
            c.Dcw1Base = Osc1Dcw * (1f / 127f); c.Dcw2Base = Osc2Dcw * (1f / 127f);
            c.Tune1 = (Osc1Octave - 2) * 12f + (Osc1Semi - 12) + (Osc1Fine - 50) * 0.01f;
            c.Tune2 = (Osc2Octave - 2) * 12f + (Osc2Semi - 12) + (Osc2Fine - 50) * 0.01f;
            c.Mode  = OscMode;

            c.DcwEnvDepth = DcwEnvAmt * (1f / 127f);
            c.DcwVel      = DcwVel    * (1f / 127f);
            c.DcwLfoDepth = LfoDcw    * (1f / 127f);
            c.PitchEnvDepth = ((PitchDepth - 64) / 63f) * PITCH_ENV_MAX;
            c.LfoPitch  = LfoPitch * (1f / 127f) * 12f;   // up to ±1 octave
            c.LfoAmp    = LfoAmp   * (1f / 127f);
            c.LfoWaveform  = (LfoWave)LfoWaveSel;
            c.LfoInc       = LfoIncFor(LfoSync, LfoRate, LfoDivision, sr);
            c.Lfo2Waveform = (LfoWave)Lfo2Wave;
            c.Lfo2Inc      = LfoIncFor(Lfo2Sync, Lfo2Rate, Lfo2Division, sr);
            c.Lfo2Pitch    = Lfo2Pitch * (1f / 127f) * 12f;
            c.Lfo2DcwDepth = Lfo2Dcw   * (1f / 127f);
            c.Lfo2Amp      = Lfo2Amp   * (1f / 127f);

            c.DcwTrackAmt  = (DcwTrack - 64) / 63f;     // [-1,1]
            c.NoiseLevel   = NoiseLevel * (1f / 127f);
            c.Dcw2EnvOn    = Dcw2Env == 1;
            c.Dcw2EnvDepth = Dcw2EnvAmt * (1f / 127f);

            c.ToneFcBase = 30f * DspMath.FastPow2((Tone / 127f) * 9.5f);  // ~30 Hz .. ~21 kHz
            c.ToneTrack  = ToneTrack * (1f / 127f);
            c.ToneRes    = ToneRes   * (1f / 127f);

            c.AmpVel    = AmpVel * (1f / 127f);
            c.PortaCoef = Portamento == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(Portamento, 0.005f, 2f), sr);
            return c;
        }

        void PushEnvCoefs(int sr)
        {
            // v1.1.1 — floor the AMP attack/release so they always ramp rather than
            // step. An instant attack defeats the click-free retrigger (SH101 §6.2):
            // on a retrigger the envelope would jump from the release-tail level to
            // 1.0 in one sample, an audible click (worst on looped chords). ~3 ms is
            // fast enough to feel instant (the decimator already smooths onsets) but
            // removes the step. DCW/pitch envelopes are modulation, not amplitude, so
            // they keep their instant option.
            const float MIN_AMP_SEG = 0.003f;

            float dA = DcwAttack  == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(DcwAttack, 0.0005f, 8f), sr);
            float dD = DcwDecay   == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(DcwDecay, 0.001f, 15f), sr);
            float dR = DcwRelease == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(DcwRelease, 0.001f, 15f), sr);
            float dS = DcwSustain * (1f / 127f);

            float d2A = Dcw2Attack  == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(Dcw2Attack, 0.0005f, 8f), sr);
            float d2D = Dcw2Decay   == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(Dcw2Decay, 0.001f, 15f), sr);
            float d2R = Dcw2Release == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(Dcw2Release, 0.001f, 15f), sr);
            float d2S = Dcw2Sustain * (1f / 127f);

            float aAsec = AmpAttack  == 0 ? MIN_AMP_SEG : MathF.Max(DspMath.TimeMap(AmpAttack, 0.0005f, 8f), MIN_AMP_SEG);
            float aRsec = AmpRelease == 0 ? MIN_AMP_SEG : MathF.Max(DspMath.TimeMap(AmpRelease, 0.001f, 15f), MIN_AMP_SEG);
            float aA = DspMath.Coef(aAsec, sr);
            float aD = AmpDecay   == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(AmpDecay, 0.001f, 15f), sr);
            float aR = DspMath.Coef(aRsec, sr);
            float aS = AmpSustain * (1f / 127f);

            float pA = PitchAttack == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(PitchAttack, 0.0005f, 4f), sr);
            float pD = PitchDecay  == 0 ? 0f : DspMath.Coef(DspMath.TimeMap(PitchDecay, 0.001f, 8f), sr);

            for (int i = 0; i < MAX_VOICES; i++)
            {
                _voices[i].AmpEnv.SetCoefs(aA, aD, aS, aR);
                _voices[i].DcwEnv.SetCoefs(dA, dD, dS, dR);
                _voices[i].DcwEnv2.SetCoefs(d2A, d2D, d2S, d2R);
                _voices[i].PitchEnv.SetCoefs(pA, pD);
            }
        }
    }
}
