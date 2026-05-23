using System;

namespace PedalFazeR
{
    // ADSR with click-free retrigger (SH101 §6.2 — NoteOn does not zero the
    // level) and a forced fast release for transport-stop (Core §27).
    internal sealed class Adsr
    {
        enum Stage { Idle, Attack, Decay, Sustain, Release }
        Stage _stage = Stage.Idle;
        float _level;
        float _aCoef, _dCoef, _rCoefUser, _rForced, _sustain;
        bool  _forced;

        public bool  IsActive => _stage != Stage.Idle;
        public float Level    => _level;

        // Coefs precomputed once per buffer by the machine (avoids per-voice exp).
        public void SetCoefs(float aCoef, float dCoef, float sustain, float rCoef)
        {
            _aCoef = aCoef; _dCoef = dCoef; _sustain = sustain;
            _rCoefUser = rCoef;   // user release stays current even while a forced release runs
        }

        public void NoteOn()                 // smooth re-attack from current level
        {
            _forced = false;
            _stage  = Stage.Attack;
        }

        public void NoteOff()
        {
            if (_stage != Stage.Idle) _stage = Stage.Release;
        }

        public void ForcedRelease(int sr)    // ~5 ms fade regardless of user release
        {
            if (_stage == Stage.Idle) return;
            _forced  = true;
            _rForced = DspMath.Coef(0.0007f, sr);   // ~0.7 ms tau -> audibly gone in ~5 ms
            _stage   = Stage.Release;
        }

        public void HardReset() { _stage = Stage.Idle; _level = 0f; _forced = false; }

        public float Tick()
        {
            switch (_stage)
            {
                case Stage.Attack:
                {
                    const float aTarget = 1.05f;     // slight overshoot -> hits 1 in finite time
                    _level = aTarget + (_level - aTarget) * _aCoef;
                    if (_aCoef == 0f || _level >= 1f) { _level = 1f; _stage = Stage.Decay; }
                    break;
                }
                case Stage.Decay:
                    _level = _sustain + (_level - _sustain) * _dCoef;
                    if (_dCoef == 0f || _level <= _sustain + 1e-4f) { _level = _sustain; _stage = Stage.Sustain; }
                    break;
                case Stage.Sustain:
                    _level = _sustain;
                    break;
                case Stage.Release:
                {
                    float rc = _forced ? _rForced : _rCoefUser;
                    _level = (rc == 0f) ? 0f : _level * rc;
                    if (_level <= 1e-4f) { _level = 0f; _stage = Stage.Idle; }
                    break;
                }
                default:
                    _level = 0f;
                    break;
            }
            return _level;
        }
    }

    // Attack-Decay envelope for pitch (DCO): 0 -> 1 over attack, 1 -> 0 over
    // decay, then idle. Bipolar use comes from a signed depth in the machine.
    internal sealed class AdEnv
    {
        enum Stage { Idle, Attack, Decay }
        Stage _stage = Stage.Idle;
        float _level, _aCoef, _dCoef;

        public bool  IsActive => _stage != Stage.Idle;
        public float Level    => _level;

        public void SetCoefs(float aCoef, float dCoef) { _aCoef = aCoef; _dCoef = dCoef; }
        public void NoteOn()    { _stage = Stage.Attack; }
        public void HardReset() { _stage = Stage.Idle; _level = 0f; }

        public float Tick()
        {
            switch (_stage)
            {
                case Stage.Attack:
                {
                    const float aTarget = 1.05f;
                    _level = aTarget + (_level - aTarget) * _aCoef;
                    if (_aCoef == 0f || _level >= 1f) { _level = 1f; _stage = Stage.Decay; }
                    break;
                }
                case Stage.Decay:
                    _level = (_dCoef == 0f) ? 0f : _level * _dCoef;
                    if (_level <= 1e-4f) { _level = 0f; _stage = Stage.Idle; }
                    break;
                default:
                    _level = 0f; break;
            }
            return _level;
        }
    }
}
