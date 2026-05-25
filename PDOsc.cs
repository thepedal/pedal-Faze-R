using System;

namespace PedalFazeR
{
    internal enum PDWave
    {
        Sine = 0, Saw = 1, Square = 2, Pulse = 3,
        ResoSaw = 4, ResoTri = 5, ResoTrap = 6, SawPulse = 7,
        DblSine = 8, ResoPulse = 9
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

        const float KMIN    = 0.0015f;   // min knee width => max brightness
        const float WMIN    = 0.03f;     // min pulse window
        public const float ResoMax = 24f; // max resonance ratio multiplier

        public static bool IsReso(PDWave w) =>
            w == PDWave.ResoSaw || w == PDWave.ResoTri || w == PDWave.ResoTrap || w == PDWave.ResoPulse;

        // Largest DCW a resonant osc may use at this pitch before the formant
        // carrier (r·f) climbs past ~0.45·sr and aliases (v1.0.1). Musically
        // lossless — a formant above Nyquist isn't audible anyway. Clamping d
        // (rather than r) keeps the rest of the DSP unchanged.
        public static float MaxResoDcw(float freqHz, int sr)
        {
            float rMax = 0.45f * sr / MathF.Max(freqHz, 1f);
            if (rMax <= 1f) return 0f;
            float d = (rMax - 1f) / (ResoMax - 1f);
            return d > 0.999f ? 0.999f : d;
        }

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
                    float r = 1f + d * (ResoMax - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    outv = (1f - p) * DspMath.Cos01(k);            // saw window
                    break;
                }
                case PDWave.ResoTri:
                {
                    float r = 1f + d * (ResoMax - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    outv = (1f - MathF.Abs(1f - 2f * p)) * DspMath.Cos01(k);  // triangle
                    break;
                }
                case PDWave.ResoTrap:
                {
                    float r = 1f + d * (ResoMax - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    float win = 2f * (1f - p); if (win > 1f) win = 1f;        // trapezoid
                    outv = win * DspMath.Cos01(k);
                    break;
                }
                case PDWave.DblSine:
                {
                    // d=0 -> pure sine; rising d crossfades in a second cosine cycle
                    // (octave up). Both terms close at the wrap, so it stays smooth
                    // and band-limited regardless of d (no hard edge to alias).
                    outv = (1f - d) * DspMath.Cos01(p) + d * DspMath.Cos01(p + p);
                    break;
                }
                case PDWave.ResoPulse:
                {
                    // Resonant carrier under a squared-saw window — a harder, more
                    // percussive formant burst than Reso Saw's linear window.
                    float r = 1f + d * (ResoMax - 1f);
                    float k = p * r; k -= MathF.Floor(k);
                    float win = (1f - p); win *= win;
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
