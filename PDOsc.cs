using System;

namespace PedalFazeR
{
    internal enum PDWave
    {
        Sine = 0, Saw = 1, Square = 2, Pulse = 3,
        ResoSaw = 4, ResoTri = 5, ResoTrap = 6, SawPulse = 7
    }

    // Phase-distortion oscillator (Casio CZ lineage). A linear phase ramp is
    // warped through a per-shape distortion function, then read from a cosine
    // table. The distortion *amount* (d, the "DCW") morphs every shape from a
    // pure sine (d=0) toward its full character (d->1). For the resonant
    // shapes d maps to the formant ratio instead of a knee — sweeping d sweeps
    // a pitch-tracking resonant peak, which is the CZ's signature trick.
    internal sealed class PDOsc
    {
        public float Phase;      // [0,1)
        public float Inc;        // increment per (oversampled) sample
        public bool  Wrapped;    // true on the sample where Phase wrapped (osc sync)

        const float KMIN     = 0.0015f;   // min knee width => max brightness
        const float WMIN     = 0.03f;     // min pulse window
        const float RESO_MAX = 24f;       // max resonance ratio multiplier

        public void Reset() { Phase = 0f; Wrapped = false; }

        // Render the waveform at the current phase, then advance one sample.
        // d is clamped by the caller to [0, 0.999].
        public float Tick(PDWave wave, float d)
        {
            float p = Phase;
            float outv;

            switch (wave)
            {
                case PDWave.Saw:
                {
                    float k = 0.5f * (1f - d); if (k < KMIN) k = KMIN;
                    float mu = (p < k) ? 0.5f * p / k
                                       : 0.5f + 0.5f * (p - k) / (1f - k);
                    outv = DspMath.Cos01(mu);
                    break;
                }
                case PDWave.Square:
                {
                    // Saw-bend applied independently to each half-cycle.
                    float k    = 0.5f * (1f - d); if (k < KMIN) k = KMIN;
                    float half = p < 0.5f ? 0f : 1f;
                    float pp   = (half == 0f) ? p * 2f : (p - 0.5f) * 2f;
                    float m    = (pp < k) ? 0.5f * pp / k
                                          : 0.5f + 0.5f * (pp - k) / (1f - k);
                    outv = DspMath.Cos01(half * 0.5f + m * 0.5f);
                    break;
                }
                case PDWave.Pulse:
                {
                    float w  = 1f - d; if (w < WMIN) w = WMIN;
                    float mu = (p < w) ? p / w : 0f;   // run a cycle, then hold at peak
                    outv = DspMath.Cos01(mu);
                    break;
                }
                case PDWave.SawPulse:
                {
                    float w = 1f - d * 0.9f; if (w < 0.08f) w = 0.08f;
                    float mu;
                    if (p < w)
                    {
                        float pp = p / w;
                        float k  = 0.5f * (1f - d); if (k < KMIN) k = KMIN;
                        mu = (pp < k) ? 0.5f * pp / k
                                      : 0.5f + 0.5f * (pp - k) / (1f - k);
                    }
                    else mu = 0f;
                    outv = DspMath.Cos01(mu);
                    break;
                }
                case PDWave.ResoSaw:
                {
                    float r = 1f + d * (RESO_MAX - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    outv = (1f - p) * DspMath.Cos01(k);            // saw window
                    break;
                }
                case PDWave.ResoTri:
                {
                    float r = 1f + d * (RESO_MAX - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    outv = (1f - MathF.Abs(1f - 2f * p)) * DspMath.Cos01(k);  // triangle
                    break;
                }
                case PDWave.ResoTrap:
                {
                    float r = 1f + d * (RESO_MAX - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    float win = 2f * (1f - p); if (win > 1f) win = 1f;        // trapezoid
                    outv = win * DspMath.Cos01(k);
                    break;
                }
                default: // Sine — DCW has no effect; clean reference / sub
                    outv = DspMath.Cos01(p);
                    break;
            }

            Phase += Inc;
            if (Phase >= 1f) { Phase -= 1f; Wrapped = true; if (Phase >= 1f) Phase -= MathF.Floor(Phase); }
            else Wrapped = false;

            return outv;
        }
    }
}
