using System;

namespace PedalFazeR
{
    // 2-pole TPT state-variable filter (Zavalishin), multimode: LP/BP/HP/Notch.
    // Upgraded in v1.4 from a gentle lowpass-only "Tone" into a proper resonant
    // filter with its own envelope. Unconditionally stable (k>0 always), with a
    // pre-warp cap on the cutoff so tan() can't blow up (M1 §7). Faze-R's core
    // brightness still comes from the DCW; this filter is an optional shaper.
    internal sealed class TptSvf
    {
        float _a1, _a2, _a3, _k, _ic1eq, _ic2eq;
        float _cFc = -1f, _cRes = -1f, _cSr = -1f;

        public void Reset() { _ic1eq = 0f; _ic2eq = 0f; }

        public void UpdateCoefs(float fcHz, float resN, int sr)
        {
            if (fcHz == _cFc && resN == _cRes && sr == _cSr) return;   // cache short-circuit
            _cFc = fcHz; _cRes = resN; _cSr = sr;

            float r  = resN < 0f ? 0f : (resN > 1f ? 1f : resN);
            float qN = 0.5f + r * r * 15f;       // Q ~0.5..15.5 (square law: gentle low, biting high)
            _k = 1f / qN;                          // damping (>0 -> never self-oscillates -> NaN-safe)

            float wd = MathF.PI * fcHz / sr;
            if (wd > 1.55f) wd = 1.55f;            // stay below tan's asymptote (NaN guard)
            float g  = MathF.Tan(wd);

            float den = 1f + g * (g + _k);
            _a1 = 1f / den;
            _a2 = g * _a1;
            _a3 = g * _a2;
        }

        // type: 0 = lowpass, 1 = bandpass, 2 = highpass, 3 = notch
        public float Process(float input, int type)
        {
            float v3 = input - _ic2eq;
            float v1 = _a1 * _ic1eq + _a2 * v3;          // bandpass
            float v2 = _ic2eq + _a2 * _ic1eq + _a3 * v3; // lowpass
            _ic1eq = 2f * v1 - _ic1eq;
            _ic2eq = 2f * v2 - _ic2eq;

            switch (type)
            {
                case 1:  return v1;                      // band
                case 2:  return input - _k * v1 - v2;    // high
                case 3:  return input - _k * v1;         // notch (= low + high)
                default: return v2;                      // low
            }
        }
    }

    // One-pole DC blocker (~5 Hz highpass). NOTE: not currently wired into the
    // signal path — measured DC from the resonant shapes is negligible (≤0.001
    // even at ~32 Hz). Kept available in case a future low-pitch/high-reso case
    // needs it; insert before the amp stage in Voice.Render if so.
    internal sealed class DcBlocker
    {
        float _x1, _y1;
        const float R = 0.9995f;
        public void Reset() { _x1 = 0f; _y1 = 0f; }
        public float Process(float x)
        {
            float y = x - _x1 + R * _y1;
            _x1 = x; _y1 = y;
            return y;
        }
    }
}
