using System;

namespace PedalFazeR
{
    // Per-voice noise source: white through a 1-pole low-pass for a pink-ish
    // tilt (SH101 §10 — uniform white sounds a touch bright in isolation). Fed
    // into the voice mix before the amp stage, so the DCA shapes it — sustained
    // for breathy pads, transient for percussive hits. MAKEUP brings the
    // post-filter level back up to roughly an oscillator at the same level
    // (tuned in the DSP harness against a unity saw).
    internal sealed class Noise
    {
        Rng   _rng;
        float _lp;

        const float A      = 0.33f;   // 1-pole coef ≈ 3 kHz cutoff at 48 kHz
        const float MAKEUP = 2.9f;    // restore RMS lost to the low-pass (tuned: noise@1.0 ≈ unity saw)

        public Noise(uint seed) { _rng = new Rng(seed); }
        public void Reset() { _lp = 0f; }

        public float Tick()
        {
            float w = _rng.NextBipolar();
            _lp += A * (w - _lp);
            return _lp * MAKEUP;
        }
    }
}
