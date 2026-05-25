#!/usr/bin/env python3
# gen_presets.py — generates "Pedal Faze-R_Presets.prs.xml".
#
# Build §3.4 pattern: PARAM_INDEX mirrors the machine's global-parameter
# DECLARATION ORDER (group 1). DEFAULTS holds every parameter's DefValue.
# Each preset is a sparse dict of raw-value overrides; everything unspecified
# falls back to its default. Append-only: when the machine gains parameters,
# add them to the end of PARAM_INDEX/DEFAULTS and old presets stay valid
# (Build §3.3). Not deployed — only the .prs.xml + .dll ship.

import io

MACHINE = "Pedal Faze-R"
OUT = "Pedal Faze-R_Presets.prs.xml"

# Declaration order == index order (group 1). DO NOT REORDER.
PARAM_INDEX = {
    "OSC1 Wave": 0, "OSC1 Octave": 1, "OSC1 Semi": 2, "OSC1 Fine": 3, "OSC1 DCW": 4, "OSC1 Level": 5,
    "OSC2 Wave": 6, "OSC2 Octave": 7, "OSC2 Semi": 8, "OSC2 Fine": 9, "OSC2 DCW": 10, "OSC2 Level": 11,
    "Osc Mode": 12, "Portamento": 13,
    "DCW Attack": 14, "DCW Decay": 15, "DCW Sustain": 16, "DCW Release": 17, "DCW Env Amt": 18, "DCW Vel": 19,
    "Amp Attack": 20, "Amp Decay": 21, "Amp Sustain": 22, "Amp Release": 23, "Amp Vel": 24,
    "Pitch Attack": 25, "Pitch Decay": 26, "Pitch Depth": 27,
    "LFO Wave": 28, "LFO Rate": 29, "LFO Delay": 30, "LFO Pitch": 31, "LFO DCW": 32, "LFO Amp": 33,
    "Tone": 34, "Tone Track": 35, "Tone Res": 36, "Oversample": 37, "Volume": 38,
    # ── New in v1.1 (appended; v1.0.x preset indices unchanged) ──
    "DCW Track": 39, "LFO Sync": 40, "LFO Division": 41, "Noise Level": 42,
    # ── New in v1.2 ──
    "DCW2 Env": 43, "DCW2 Attack": 44, "DCW2 Decay": 45, "DCW2 Sustain": 46,
    "DCW2 Release": 47, "DCW2 Env Amt": 48,
    "LFO2 Wave": 49, "LFO2 Rate": 50, "LFO2 Sync": 51, "LFO2 Division": 52,
    "LFO2 Delay": 53, "LFO2 Pitch": 54, "LFO2 DCW": 55, "LFO2 Amp": 56,
    # ── New in v1.3 ──
    "Chorus": 57, "Chorus Rate": 58, "Chorus Depth": 59, "Chorus Mix": 60,
}

DEFAULTS = {
    "OSC1 Wave": 1, "OSC1 Octave": 2, "OSC1 Semi": 12, "OSC1 Fine": 50, "OSC1 DCW": 0, "OSC1 Level": 100,
    "OSC2 Wave": 1, "OSC2 Octave": 2, "OSC2 Semi": 12, "OSC2 Fine": 50, "OSC2 DCW": 0, "OSC2 Level": 0,
    "Osc Mode": 0, "Portamento": 0,
    "DCW Attack": 0, "DCW Decay": 70, "DCW Sustain": 80, "DCW Release": 50, "DCW Env Amt": 90, "DCW Vel": 0,
    "Amp Attack": 0, "Amp Decay": 70, "Amp Sustain": 110, "Amp Release": 35, "Amp Vel": 70,
    "Pitch Attack": 0, "Pitch Decay": 40, "Pitch Depth": 64,
    "LFO Wave": 0, "LFO Rate": 50, "LFO Delay": 0, "LFO Pitch": 0, "LFO DCW": 0, "LFO Amp": 0,
    "Tone": 127, "Tone Track": 0, "Tone Res": 0, "Oversample": 1, "Volume": 100,
    "DCW Track": 64, "LFO Sync": 0, "LFO Division": 2, "Noise Level": 0,
    "DCW2 Env": 0, "DCW2 Attack": 0, "DCW2 Decay": 70, "DCW2 Sustain": 80,
    "DCW2 Release": 50, "DCW2 Env Amt": 90,
    "LFO2 Wave": 0, "LFO2 Rate": 50, "LFO2 Sync": 0, "LFO2 Division": 2,
    "LFO2 Delay": 0, "LFO2 Pitch": 0, "LFO2 DCW": 0, "LFO2 Amp": 0,
    "Chorus": 0, "Chorus Rate": 30, "Chorus Depth": 50, "Chorus Mix": 50,
}

# Wave indices: 0 Sine 1 Saw 2 Square 3 Pulse 4 ResoSaw 5 ResoTri 6 ResoTrap 7 Saw-Pulse
# Octave: 0=-2 1=-1 2=0 3=+1 4=+2 ; Osc Mode: 0 Mix 1 Ring 2 Sync ; LFO Wave: 0 Tri 1 Saw 2 Sq 3 S&H 4 Sine

PRESETS = {
    # ── Init / reference ─────────────────────────────────────────────
    "Init - Saw":          {},  # the machine defaults
    "Init - Sine":         {"OSC1 Wave": 0, "DCW Env Amt": 0},

    # ── Bass ─────────────────────────────────────────────────────────
    "Bass - CZ Classic":   {"OSC1 Wave": 1, "OSC1 Octave": 1, "OSC1 DCW": 5,
                            "DCW Decay": 45, "DCW Sustain": 25, "DCW Env Amt": 115,
                            "Amp Decay": 60, "Amp Sustain": 70, "Amp Release": 25, "Volume": 105},
    "Bass - Reso Growl":   {"OSC1 Wave": 4, "OSC1 Octave": 1, "OSC1 DCW": 10,
                            "DCW Decay": 55, "DCW Sustain": 35, "DCW Env Amt": 100,
                            "Amp Decay": 70, "Amp Sustain": 90, "Amp Release": 20},
    "Bass - Sub Sine":     {"OSC1 Wave": 0, "OSC1 Octave": 0, "DCW Env Amt": 0,
                            "Amp Decay": 80, "Amp Sustain": 100, "Amp Release": 30},
    "Bass - Sync Punch":   {"OSC1 Wave": 1, "OSC2 Wave": 1, "OSC2 Level": 90, "OSC2 Semi": 19,
                            "Osc Mode": 2, "OSC1 Octave": 1, "OSC2 Octave": 1,
                            "DCW Decay": 40, "DCW Sustain": 20, "DCW Env Amt": 110,
                            "Amp Decay": 45, "Amp Sustain": 60, "Amp Release": 18},

    # ── Lead ─────────────────────────────────────────────────────────
    "Lead - Saw Bright":   {"OSC1 Wave": 1, "OSC1 DCW": 40, "DCW Sustain": 100, "DCW Env Amt": 60,
                            "Amp Sustain": 120, "Amp Release": 30, "LFO Pitch": 8, "LFO Rate": 62, "LFO Delay": 55},
    "Lead - Square Hollow": {"OSC1 Wave": 2, "OSC1 DCW": 30, "DCW Sustain": 90, "DCW Env Amt": 70,
                            "Amp Sustain": 115, "Amp Release": 28},
    "Lead - Reso Sweep":   {"OSC1 Wave": 4, "OSC1 DCW": 0, "DCW Attack": 40, "DCW Decay": 80,
                            "DCW Sustain": 80, "DCW Env Amt": 120, "Amp Sustain": 120, "Amp Release": 35},
    "Lead - Sync Tear":    {"OSC1 Wave": 1, "OSC2 Wave": 2, "OSC2 Level": 110, "OSC2 Semi": 24,
                            "Osc Mode": 2, "DCW Sustain": 100, "DCW Env Amt": 70,
                            "Amp Sustain": 118, "Amp Release": 26, "LFO Pitch": 18, "LFO Rate": 70, "LFO Delay": 40},
    "Lead - Detune Stack": {"OSC1 Wave": 1, "OSC2 Wave": 1, "OSC2 Level": 100, "OSC2 Fine": 56,
                            "DCW Sustain": 100, "DCW Env Amt": 65, "Amp Sustain": 115,
                            "Amp Release": 40, "Portamento": 35},

    # ── Pad ──────────────────────────────────────────────────────────
    "Pad - Soft Strings":  {"OSC1 Wave": 1, "OSC2 Wave": 1, "OSC2 Level": 95, "OSC2 Fine": 57,
                            "DCW Attack": 60, "DCW Decay": 90, "DCW Sustain": 75, "DCW Release": 80, "DCW Env Amt": 75,
                            "Amp Attack": 55, "Amp Sustain": 115, "Amp Release": 85,
                            "LFO DCW": 14, "LFO Rate": 28, "Tone": 110},
    "Pad - Glassy":        {"OSC1 Wave": 3, "OSC2 Wave": 0, "OSC2 Level": 80, "OSC2 Octave": 3,
                            "DCW Attack": 50, "DCW Decay": 95, "DCW Sustain": 70, "DCW Release": 90, "DCW Env Amt": 85,
                            "Amp Attack": 45, "Amp Sustain": 110, "Amp Release": 90},
    "Pad - Reso Wash":     {"OSC1 Wave": 5, "OSC1 DCW": 20, "DCW Attack": 70, "DCW Decay": 100,
                            "DCW Sustain": 60, "DCW Release": 95, "DCW Env Amt": 110,
                            "Amp Attack": 60, "Amp Sustain": 110, "Amp Release": 95,
                            "LFO DCW": 22, "LFO Rate": 22},
    "Pad - Ring Air":      {"OSC1 Wave": 0, "OSC2 Wave": 0, "OSC2 Level": 100, "OSC2 Semi": 19,
                            "Osc Mode": 1, "DCW Env Amt": 0,
                            "Amp Attack": 65, "Amp Sustain": 105, "Amp Release": 100, "Tone": 105},

    # ── Pluck / Keys ─────────────────────────────────────────────────
    "Pluck - DCW Pop":     {"OSC1 Wave": 1, "OSC1 DCW": 0, "DCW Attack": 0, "DCW Decay": 28,
                            "DCW Sustain": 10, "DCW Env Amt": 120, "Amp Decay": 38, "Amp Sustain": 0,
                            "Amp Release": 22, "Amp Vel": 95},
    "Pluck - Reso Tine":   {"OSC1 Wave": 4, "OSC1 DCW": 5, "DCW Decay": 34, "DCW Sustain": 8, "DCW Env Amt": 110,
                            "Amp Decay": 42, "Amp Sustain": 0, "Amp Release": 26, "Amp Vel": 90},
    "Keys - Electric":     {"OSC1 Wave": 7, "OSC2 Wave": 0, "OSC2 Level": 60, "OSC2 Octave": 3,
                            "DCW Decay": 50, "DCW Sustain": 30, "DCW Env Amt": 95,
                            "Amp Decay": 70, "Amp Sustain": 45, "Amp Release": 35, "Amp Vel": 85},
    "Keys - Clav":         {"OSC1 Wave": 2, "OSC1 DCW": 15, "DCW Decay": 25, "DCW Sustain": 20, "DCW Env Amt": 100,
                            "Amp Decay": 40, "Amp Sustain": 30, "Amp Release": 20, "Amp Vel": 100},

    # ── Bell ─────────────────────────────────────────────────────────
    "Bell - CZ Tine EP":   {"OSC1 Wave": 0, "OSC2 Wave": 0, "OSC2 Level": 70, "OSC2 Octave": 4, "OSC2 Semi": 19,
                            "DCW Env Amt": 0, "Amp Decay": 95, "Amp Sustain": 20, "Amp Release": 60, "Amp Vel": 90},
    "Bell - Glass Bell":   {"OSC1 Wave": 5, "OSC1 DCW": 30, "DCW Decay": 80, "DCW Sustain": 25, "DCW Env Amt": 90,
                            "Amp Decay": 100, "Amp Sustain": 10, "Amp Release": 80},
    "Bell - Ring Metal":   {"OSC1 Wave": 0, "OSC2 Wave": 0, "OSC2 Level": 110, "OSC2 Semi": 21, "OSC2 Fine": 58,
                            "Osc Mode": 1, "DCW Env Amt": 0,
                            "Amp Decay": 90, "Amp Sustain": 0, "Amp Release": 70},

    # ── Resonant showcase ────────────────────────────────────────────
    "Reso - Formant Saw":  {"OSC1 Wave": 4, "OSC1 DCW": 0, "DCW Attack": 30, "DCW Decay": 70,
                            "DCW Sustain": 70, "DCW Env Amt": 127, "Amp Sustain": 115, "Amp Release": 40},
    "Reso - Tri Vox":      {"OSC1 Wave": 5, "OSC1 DCW": 25, "DCW Sustain": 80, "DCW Env Amt": 90,
                            "Amp Attack": 30, "Amp Sustain": 110, "Amp Release": 50,
                            "LFO DCW": 18, "LFO Rate": 30},
    "Reso - Trap Sweep":   {"OSC1 Wave": 6, "OSC1 DCW": 0, "DCW Attack": 20, "DCW Decay": 90,
                            "DCW Sustain": 50, "DCW Release": 70, "DCW Env Amt": 120,
                            "Amp Sustain": 115, "Amp Release": 45},
    "Reso - LFO Wah":      {"OSC1 Wave": 4, "OSC1 DCW": 30, "DCW Sustain": 100, "DCW Env Amt": 40,
                            "LFO DCW": 60, "LFO Rate": 44, "Amp Sustain": 115, "Amp Release": 40},

    # ── FX ───────────────────────────────────────────────────────────
    "FX - Sync Drama":     {"OSC1 Wave": 2, "OSC2 Wave": 1, "OSC2 Level": 120, "OSC2 Semi": 12,
                            "Osc Mode": 2, "DCW Sustain": 90, "DCW Env Amt": 80,
                            "LFO Pitch": 40, "LFO Rate": 30, "Amp Sustain": 120, "Amp Release": 50},
    "FX - Pitch Drop":     {"OSC1 Wave": 1, "Pitch Attack": 0, "Pitch Decay": 70, "Pitch Depth": 30,
                            "DCW Decay": 60, "DCW Sustain": 40, "DCW Env Amt": 100,
                            "Amp Decay": 80, "Amp Sustain": 0, "Amp Release": 40},
    "FX - Pitch Rise":     {"OSC1 Wave": 4, "OSC1 DCW": 10, "Pitch Attack": 50, "Pitch Decay": 60, "Pitch Depth": 100,
                            "DCW Env Amt": 110, "Amp Attack": 20, "Amp Sustain": 110, "Amp Release": 45},
    "FX - Ring Sweep":     {"OSC1 Wave": 1, "OSC2 Wave": 1, "OSC2 Level": 110, "OSC2 Semi": 17,
                            "Osc Mode": 1, "DCW Sustain": 90, "DCW Env Amt": 90,
                            "LFO Pitch": 14, "LFO Rate": 24, "Amp Sustain": 115, "Amp Release": 55},

    # ── v1.1 feature showcases ───────────────────────────────────────
    "Pluck - Noise Click": {"OSC1 Wave": 1, "OSC1 DCW": 0, "DCW Decay": 30, "DCW Sustain": 10, "DCW Env Amt": 110,
                            "Amp Decay": 36, "Amp Sustain": 0, "Amp Release": 24, "Amp Vel": 95,
                            "Noise Level": 55},
    "FX - Noise Sweep":    {"OSC1 Wave": 4, "OSC1 DCW": 0, "DCW Attack": 35, "DCW Decay": 80,
                            "DCW Sustain": 70, "DCW Env Amt": 120, "Amp Attack": 25, "Amp Sustain": 110,
                            "Amp Release": 60, "Noise Level": 40},
    "Lead - Sync Wobble":  {"OSC1 Wave": 1, "OSC1 DCW": 25, "DCW Sustain": 100, "DCW Env Amt": 50,
                            "LFO Sync": 1, "LFO Division": 3, "LFO DCW": 55,
                            "Amp Sustain": 118, "Amp Release": 35},
    "Bass - Synced Wah":   {"OSC1 Wave": 4, "OSC1 Octave": 1, "OSC1 DCW": 20, "DCW Sustain": 90, "DCW Env Amt": 40,
                            "LFO Sync": 1, "LFO Division": 5, "LFO DCW": 70,
                            "Amp Decay": 70, "Amp Sustain": 95, "Amp Release": 20},
    "Pad - Bright Track":  {"OSC1 Wave": 1, "OSC2 Wave": 1, "OSC2 Level": 95, "OSC2 Fine": 57,
                            "DCW Attack": 55, "DCW Sustain": 80, "DCW Env Amt": 60, "DCW Track": 100,
                            "Amp Attack": 50, "Amp Sustain": 115, "Amp Release": 85},
    "Keys - Reso Track":   {"OSC1 Wave": 5, "OSC1 DCW": 20, "DCW Decay": 55, "DCW Sustain": 40, "DCW Env Amt": 85,
                            "DCW Track": 96, "Amp Decay": 80, "Amp Sustain": 35, "Amp Release": 40, "Amp Vel": 85},

    # ── v1.2 feature showcases ───────────────────────────────────────
    "Pad - Two Lines":     {"OSC1 Wave": 1, "OSC2 Wave": 4, "OSC2 Level": 95, "OSC2 Fine": 56,
                            "DCW Attack": 50, "DCW Decay": 90, "DCW Sustain": 75, "DCW Env Amt": 70,
                            "DCW2 Env": 1, "DCW2 Attack": 90, "DCW2 Decay": 110, "DCW2 Sustain": 55, "DCW2 Env Amt": 100,
                            "Amp Attack": 55, "Amp Sustain": 115, "Amp Release": 90},
    "Lead - Dual Mod":     {"OSC1 Wave": 1, "OSC1 DCW": 25, "DCW Sustain": 100, "DCW Env Amt": 50,
                            "LFO Pitch": 7, "LFO Rate": 64, "LFO Delay": 40,
                            "LFO2 DCW": 45, "LFO2 Sync": 1, "LFO2 Division": 2,
                            "Amp Sustain": 118, "Amp Release": 35},
    "FX - Cross Sweep":    {"OSC1 Wave": 4, "OSC2 Wave": 4, "OSC2 Level": 90, "OSC2 Semi": 19,
                            "DCW Attack": 20, "DCW Decay": 70, "DCW Sustain": 60, "DCW Env Amt": 120,
                            "DCW2 Env": 1, "DCW2 Attack": 80, "DCW2 Decay": 100, "DCW2 Sustain": 80, "DCW2 Env Amt": 120,
                            "LFO2 DCW": 35, "LFO2 Rate": 30,
                            "Amp Attack": 25, "Amp Sustain": 115, "Amp Release": 55},

    # ── v1.3 feature showcases ───────────────────────────────────────
    "Pad - Wide Ensemble": {"OSC1 Wave": 1, "OSC2 Wave": 1, "OSC2 Level": 100, "OSC2 Fine": 57,
                            "DCW Attack": 60, "DCW Decay": 95, "DCW Sustain": 80, "DCW Env Amt": 70,
                            "Amp Attack": 60, "Amp Sustain": 118, "Amp Release": 95,
                            "Chorus": 1, "Chorus Rate": 24, "Chorus Depth": 55, "Chorus Mix": 70},
    "Keys - Chorus EP":    {"OSC1 Wave": 0, "OSC2 Wave": 4, "OSC2 Level": 75, "OSC2 Octave": 3,
                            "DCW Decay": 60, "DCW Sustain": 25, "DCW Env Amt": 95,
                            "Amp Decay": 85, "Amp Sustain": 30, "Amp Release": 45, "Amp Vel": 80,
                            "Chorus": 1, "Chorus Rate": 34, "Chorus Depth": 45, "Chorus Mix": 55},
}


def esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
             .replace('"', "&quot;"))


def main():
    buf = io.StringIO()
    buf.write('<?xml version="1.0" encoding="utf-8"?>\n')
    buf.write("<PresetDictionary>\n")
    for name, ov in PRESETS.items():
        unknown = set(ov) - set(PARAM_INDEX)
        if unknown:
            raise SystemExit(f"preset '{name}' references unknown params: {unknown}")
        buf.write(f'  <Item Key="{esc(name)}">\n')
        buf.write(f'    <Preset Machine="{esc(MACHINE)}">\n')
        buf.write("      <Parameters>\n")
        for pname, idx in sorted(PARAM_INDEX.items(), key=lambda kv: kv[1]):
            val = ov.get(pname, DEFAULTS[pname])
            buf.write(f'        <Parameter Name="{esc(pname)}" Group="1" '
                      f'Index="{idx}" Track="0" Value="{val}" />\n')
        buf.write("      </Parameters>\n")
        buf.write("      <Attributes />\n")
        buf.write("      <Comment></Comment>\n")
        buf.write("    </Preset>\n")
        buf.write("  </Item>\n")
    buf.write("</PresetDictionary>\n")

    with open(OUT, "w", encoding="utf-8-sig") as f:   # UTF-8 with BOM (Build §3.1)
        f.write(buf.getvalue())
    print(f"wrote {OUT}: {len(PRESETS)} presets, {len(PARAM_INDEX)} params each")


if __name__ == "__main__":
    main()
