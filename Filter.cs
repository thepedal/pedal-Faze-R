using System;

namespace PedalFazeR
{
    // Gentle 2-pole TPT state-variable lowpass (Zavalishin), lowpass tap only.
    // Used as an optional "Tone" control — the brightness of Faze-R comes from
    // the DCW, not this filter, so it stays musical and never self-oscillates.
    // (Topology and pre-warp cap from M1 §7.)
    internal sealed class TptLowpass
    {
        float _a1, _a2, _a3, _ic1eq, _ic2eq;
        float _cFc = -1f, _cRes = -1f, _cSr = -1f;

        public void Reset() { _ic1eq = 0f; _ic2eq = 0f; }

        public void UpdateCoefs(float fcHz, float resN, int sr)
        {
            if (fcHz == _cFc && resN == _cRes && sr == _cSr) return;   // cache short-circuit
            _cFc = fcHz; _cRes = resN; _cSr = sr;

            float qN = 0.5f + (resN < 0f ? 0f : (resN > 1f ? 1f : resN)) * 3.5f; // Q 0.5..4
            float k  = 1f / qN;

            float wd = MathF.PI * fcHz / sr;
            if (wd > 1.55f) wd = 1.55f;          // stay below tan's asymptote (NaN guard)
            float g  = MathF.Tan(wd);

            float den = 1f + g * (g + k);
            _a1 = 1f / den;
            _a2 = g * _a1;
            _a3 = g * _a2;
        }

        public float Process(float input)
        {
            float v3 = input - _ic2eq;
            float v1 = _a1 * _ic1eq + _a2 * v3;
            float v2 = _ic2eq + _a2 * _ic1eq + _a3 * v3;
            _ic1eq = 2f * v1 - _ic1eq;
            _ic2eq = 2f * v2 - _ic2eq;
            return v2;                            // lowpass tap
        }
    }

    // One-pole DC blocker (~5 Hz highpass). Resonant PD shapes carry some DC;
    // this keeps the summed output centred.
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
