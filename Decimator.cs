using System;

namespace PedalFazeR
{
    // Decimating low-pass for oversampled oscillator rendering.
    //
    // Usage per output sample: Push() the oversample-factor samples, then
    // Read() once. The FIR is a windowed-sinc low-pass with cutoff at the base
    // Nyquist (= 0.5/os of the oversampled Nyquist), computed at Configure()
    // time so there are no transcription-prone hardcoded coefficient tables.
    //
    // os = 1 bypasses the filter entirely (naive PD — the authentic CZ grit).
    internal sealed class Decimator
    {
        float[] _h;          // kernel
        float[] _hist;       // circular history of the oversampled stream
        int     _n;          // tap count
        int     _pos;        // write head into _hist
        int     _os = 1;
        float   _last;       // os==1 pass-through

        public int Oversample => _os;

        public void Configure(int os)
        {
            if (os < 1) os = 1;
            if (os == _os && (os == 1 || _h != null)) return;
            _os = os;

            if (os == 1) { _h = null; _hist = null; _pos = 0; return; }

            _n = 16 * os + 1;                // odd length scales with rate (v1.0.1: steeper)
            _h = new float[_n];
            float fc = 0.45f / os;           // cutoff just below base Nyquist (guard band)
            int   m  = _n - 1;
            double sum = 0;
            for (int k = 0; k < _n; k++)
            {
                double t = k - m / 2.0;
                double sinc = (t == 0.0) ? 2.0 * fc
                                         : Math.Sin(2.0 * Math.PI * fc * t) / (Math.PI * t);
                double w = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * k / m)   // Blackman
                                + 0.08 * Math.Cos(4.0 * Math.PI * k / m);
                double v = sinc * w;
                _h[k] = (float)v;
                sum  += v;
            }
            float inv = (float)(1.0 / sum);  // normalise DC gain to 1
            for (int k = 0; k < _n; k++) _h[k] *= inv;

            _hist = new float[_n];
            _pos  = 0;
        }

        public void Reset()
        {
            if (_hist != null) Array.Clear(_hist, 0, _hist.Length);
            _pos = 0; _last = 0f;
        }

        public void Push(float x)
        {
            if (_os == 1) { _last = x; return; }
            _hist[_pos] = x;
            if (++_pos >= _n) _pos = 0;
        }

        public float Read()
        {
            if (_os == 1) return _last;
            float acc = 0f;
            int idx = _pos - 1;              // kernel is symmetric, so order is irrelevant
            for (int k = 0; k < _n; k++)
            {
                if (idx < 0) idx += _n;
                acc += _h[k] * _hist[idx];
                idx--;
            }
            return acc;
        }
    }
}
