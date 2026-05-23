using System;

namespace PedalFazeR
{
    // Pure-DSP helpers. No ReBuzz SDK dependency (Build §6.3).
    internal static class DspMath
    {
        // ── Fast 2^x for the audio hot path (SH101 §1) ──────────────────────
        // IEEE-754 exponent-bit trick + cubic polynomial for the fraction.
        // Accuracy ~0.04% (well under 1 cent of pitch). Clamp keeps it NaN-safe.
        public static float FastPow2(float x)
        {
            float xi = MathF.Floor(x);
            float xf = x - xi;
            // 2^xf via a degree-6 series (Horner). The 3-term form undershoots
            // by ~0.5% near integer x (≈9 cents at octave boundaries); the extra
            // terms bring worst-case error under ~0.003% (<0.1 cent).
            float p = 1f + xf * (0.6931472f + xf * (0.2402265f + xf * (0.0555041f
                          + xf * (0.0096181f + xf * (0.0013334f + xf * 0.0001540f)))));
            int   e = Math.Clamp((int)xi + 127, 1, 254);
            return BitConverter.Int32BitsToSingle(e << 23) * p;
        }

        public static float MidiToHz(float midi) => 440f * FastPow2((midi - 69f) / 12f);

        // ── Cosine table: returns cos(2*pi*phase), phase wrapped to [0,1) ────
        const int COS_BITS = 13;
        const int COS_SIZE = 1 << COS_BITS;     // 8192
        const int COS_MASK = COS_SIZE - 1;
        static readonly float[] _cos = BuildCos();

        static float[] BuildCos()
        {
            var t = new float[COS_SIZE + 1];    // +1 guard for linear interp
            for (int i = 0; i <= COS_SIZE; i++)
                t[i] = MathF.Cos(2f * MathF.PI * i / COS_SIZE);
            return t;
        }

        public static float Cos01(float phase)
        {
            phase -= MathF.Floor(phase);        // wrap to [0,1)
            float fi = phase * COS_SIZE;
            int   i  = (int)fi;
            float fr = fi - i;
            i &= COS_MASK;
            return _cos[i] + (_cos[i + 1] - _cos[i]) * fr;
        }

        // ── Gentle cubic soft-clip — near-linear below ~0.7, saturates by 3 ──
        public static float SoftClip(float x)
        {
            if (x <= -3f) return -1f;
            if (x >=  3f) return  1f;
            return x * (27f + x * x) / (27f + 9f * x * x);
        }

        // Exponential time map: v in [0,127] -> seconds in [minT, maxT].
        public static float TimeMap(int v, float minT, float maxT)
        {
            if (v <= 0)   return minT;
            if (v >= 127) return maxT;
            return minT * MathF.Pow(maxT / minT, v / 127f);
        }

        // One-pole coefficient reaching ~63% of its target in 'sec'.
        public static float Coef(float sec, int sr)
        {
            if (sec <= 0f) return 0f;
            return MathF.Exp(-1f / (sec * sr));
        }
    }

    // xorshift32 — fast, allocation-free, audio-thread safe RNG.
    internal struct Rng
    {
        uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

        public float NextBipolar()              // [-1, 1)
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return _s * (1f / 2147483648f) - 1f;
        }
    }
}
