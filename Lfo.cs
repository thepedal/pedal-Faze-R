using System;

namespace PedalFazeR
{
    internal enum LfoWave { Tri = 0, Saw = 1, Square = 2, SH = 3, Sine = 4 }

    // Per-voice LFO. Phase is key-synced on NoteOn (predictable per-note
    // vibrato/tremolo); the delay counter ramps depth in after a note.
    internal sealed class Lfo
    {
        public float Phase;
        float _shCur, _shNext;
        Rng   _rng;
        int   _delaySamps, _delayCount;

        public Lfo(uint seed)
        {
            _rng    = new Rng(seed);
            _shCur  = _rng.NextBipolar();
            _shNext = _rng.NextBipolar();
        }

        public void NoteOn(int delaySamps)
        {
            Phase       = 0f;
            _delaySamps = delaySamps;
            _delayCount = delaySamps;
        }

        // inc = rateHz / sr. Returns bipolar [-1,1] scaled by the delay ramp.
        public float Tick(LfoWave wave, float inc)
        {
            float ph = Phase;
            float v;
            switch (wave)
            {
                case LfoWave.Saw:    v = 1f - 2f * ph; break;                 // +1 -> -1
                case LfoWave.Square: v = ph < 0.5f ? 1f : -1f; break;
                case LfoWave.Sine:   v = DspMath.Cos01(ph); break;
                case LfoWave.SH:     v = _shCur; break;
                default:             v = 1f - 4f * MathF.Abs(ph - 0.5f); break; // triangle -1..1
            }

            Phase += inc;
            if (Phase >= 1f)
            {
                Phase  -= 1f;
                _shCur  = _shNext;
                _shNext = _rng.NextBipolar();
            }

            float dscale = 1f;
            if (_delayCount > 0)
            {
                _delayCount--;
                dscale = 1f - (float)_delayCount / (_delaySamps > 0 ? _delaySamps : 1);
            }
            return v * dscale;
        }
    }
}
