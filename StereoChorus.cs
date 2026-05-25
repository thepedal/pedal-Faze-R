using System;

namespace PedalFazeR
{
    // Stereo ensemble chorus. The mono voice mix feeds one delay line; two read
    // taps are modulated by the same LFO 90° apart (quadrature), so L and R sweep
    // independently and the dry-mono output opens into a wide stereo image. No
    // feedback — the tail is just the max delay (~17 ms), so it flushes to silence
    // quickly and can't build up or self-oscillate. Phase distortion is famously
    // dry; this is the bloom it usually gets from an outboard ensemble.
    internal sealed class StereoChorus
    {
        float[] _buf = Array.Empty<float>();
        int     _w, _size;
        float   _phase;       // LFO phase 0..1
        float   _baseSamps;   // centre delay

        public void Configure(int sr)
        {
            _size = (int)(0.050f * sr) + 4;     // 50 ms ring buffer — ample headroom
            _buf  = new float[_size];
            _w = 0; _phase = 0f;
            _baseSamps = 0.011f * sr;           // 11 ms centre delay
        }

        public void Reset()
        {
            if (_buf.Length > 0) Array.Clear(_buf, 0, _buf.Length);
            _w = 0; _phase = 0f;
        }

        // dry: mono input. incPhase: LFO step/sample. depthSamps: ± delay sweep.
        // dryG/wetG: equal-power wet/dry gains (computed once per buffer).
        public void Process(float dry, float incPhase, float depthSamps,
                            float dryG, float wetG, out float l, out float r)
        {
            _buf[_w] = dry;

            float lfoL = DspMath.Cos01(_phase);          // 0°
            float lfoR = DspMath.Cos01(_phase + 0.25f);  // +90° (quadrature → width)
            float wetL = ReadInterp(_baseSamps + depthSamps * lfoL);
            float wetR = ReadInterp(_baseSamps + depthSamps * lfoR);

            l = dry * dryG + wetL * wetG;
            r = dry * dryG + wetR * wetG;

            if (++_w >= _size) _w = 0;
            _phase += incPhase; if (_phase >= 1f) _phase -= 1f;
        }

        float ReadInterp(float delaySamps)
        {
            float rp = _w - delaySamps;
            while (rp < 0f)     rp += _size;
            while (rp >= _size) rp -= _size;   // fp guard: a tiny-negative rp can round up to exactly _size
            int   i0 = (int)rp;
            float fr = rp - i0;
            int   i1 = i0 + 1; if (i1 >= _size) i1 -= _size;
            return _buf[i0] + (_buf[i1] - _buf[i0]) * fr;
        }
    }
}
