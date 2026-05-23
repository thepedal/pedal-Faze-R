# Pedal Faze-R

An 8-voice polyphonic **phase-distortion** synthesiser for ReBuzz — a modern
take on the Casio CZ engine.

Phase distortion makes its tone not by filtering a rich waveform, but by
*warping the phase* of a sine before it's read from a table. A pure cosine read
at a constant rate is a sine; read it faster through part of its cycle and
slower through the rest and you get harmonics. The amount of that warp — the
**DCW** (Digitally Controlled Wave) — is what a dedicated envelope sweeps here,
in place of a filter cutoff. That DCW envelope is the heart of the instrument
and the reason it sounds like PD rather than a subtractive synth.

## Voice architecture

```
  PD OSC1 ─┐
           ├─ mix / ring / sync ─→ Tone (gentle LP) ─→ DCA ─→ voice out
  PD OSC2 ─┘
       │                 │            │
   DCW env (wave) ────────┘    Amp env (DCA)
   Pitch env (DCO) → osc pitch
   LFO → pitch (vibrato) / DCW (wave wobble) / amp (tremolo)
```

- **Two PD oscillators**, each with independent octave / semitone / fine tune,
  base DCW, level, and one of eight waveshapes.
- **Osc Mode**: Mix, Ring (osc1 × osc2), or Sync (osc2 hard-synced to osc1).
- **DCW envelope** (Attack/Decay/Sustain/Release + depth + velocity) sweeps the
  harmonic content of both oscillators — the classic CZ "filterless filter".
- **Amp envelope** (DCA) with velocity sensitivity.
- **Pitch envelope** (one-shot AD, bipolar depth) for blips and drops.
- **LFO** (Tri/Saw/Square/S&H/Sine) with delay, routable to pitch, DCW and amp.
- **Tone**: a gentle non-resonant 2-pole low-pass with optional key-follow —
  brightness still comes from the DCW; this is just a tone shaper.
- **Oversample**: Off / 2× / 4× anti-aliasing. PD aliases at high distortion and
  high pitch; 2× (default) cleans it up, Off gives the authentic vintage grit.

### Waveshapes

`Sine` (pure reference), `Saw`, `Square`, `Pulse`, `Saw-Pulse`, and the three
**resonant** shapes `Reso Saw` / `Reso Tri` / `Reso Trap`. For the bend shapes,
DCW controls brightness (sine → full character). For the resonant shapes, DCW
controls the **formant ratio** — sweeping DCW sweeps a pitch-tracking resonant
peak, the CZ's signature trick. Point the DCW envelope at a resonant shape for
instant filter-style sweeps with no filter at all.

## Playing it

Track index = voice index. Polyphony comes from placing notes on multiple
pattern tracks at the same row (the standard ReBuzz convention); chords across
tracks are recovered correctly even on newer ReBuzz builds. Each track has a
**Note** column and a **Velocity** column (held; defaults to 100).

## Presets

`Pedal Faze-R_Presets.prs.xml` ships 30 presets across Bass, Lead, Pad, Pluck,
Keys, Bell, Resonant and FX categories. They appear in the machine's right-click
menu once the bundle is in the gear folder. Regenerate with:

```
python3 gen_presets.py
```

Preset overrides are sparse — anything not listed uses the machine default.
Parameter order is an append-only contract: new parameters must be added to the
*end* of the declaration list so existing presets keep working.

## Building

Requires the .NET 10 SDK and a ReBuzz install. The project references
`BuzzGUI.Interfaces.dll` from `C:\Program Files\ReBuzz`; if your install lives
elsewhere, fix the `<HintPath>` and the deploy paths in the `.csproj`.

```
dotnet build -c Release "Pedal Faze-R.NET.csproj"
```

The post-build target copies `Pedal Faze-R.NET.dll` and the preset bundle into
`C:\Program Files\ReBuzz\Gear\Generators\`. If ReBuzz is running it holds the
DLL open and the copy is skipped (the build still succeeds) — close ReBuzz and
rebuild to refresh. Only the `.dll` and the `.prs.xml` are needed at runtime;
`.pdb` / `.deps.json` are intentionally not generated.

## Files

| File | Role |
|------|------|
| `PedalFazeR.cs` | Machine: parameters, note/velocity setters, multi-track recovery, `Work` |
| `Voice.cs` | Per-voice state and the per-sample render loop |
| `PDOsc.cs` | Phase-distortion oscillator — the eight waveshapes |
| `Envelope.cs` | ADSR (click-free retrigger, forced release) + AD pitch envelope |
| `Lfo.cs` | Per-voice LFO |
| `Filter.cs` | TPT 2-pole low-pass tone control + DC blocker |
| `Decimator.cs` | Windowed-sinc oversampling decimator |
| `DspMath.cs` | FastPow2, cosine table, soft-clip, helpers |
| `gen_presets.py` | Preset-bank generator (source only — not deployed) |

## Notes / future ideas

- **Oversampling** filters the per-voice oscillator mix down to the base rate
  with a windowed-sinc FIR (length scales with the factor). Envelopes, LFO and
  the tone filter run at the base rate.
- The resonant shapes use a continuous formant ratio (no integer quantisation),
  so DCW sweeps are smooth rather than stepped.
- Possible later additions (append-only so presets stay valid): a noise source,
  a second LFO, per-osc DCW envelopes, and tempo-synced LFO rates.
