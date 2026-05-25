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
  PD OSC1 ─┐  ← DCW env 1
           ├─ mix / ring / sync ─→ Tone (gentle LP) ─→ DCA ─→ voice out
  PD OSC2 ─┘  ← DCW env 1 (or its own DCW env 2)
       │                 │            │
   DCW env(s) (wave) ─────┘    Amp env (DCA)
   Pitch env (DCO) → osc pitch
   LFO 1 + LFO 2 → pitch (vibrato) / DCW (wave wobble) / amp (tremolo)
```

- **Two PD oscillators**, each with independent octave / semitone / fine tune,
  base DCW, level, and one of eight waveshapes.
- **Osc Mode**: Mix, Ring (osc1 × osc2), or Sync (osc2 hard-synced to osc1).
- **DCW envelope** (Attack/Decay/Sustain/Release + depth + velocity) sweeps the
  harmonic content — the classic CZ "filterless filter" — with optional
  **key-tracking** so brightness/formant follows pitch. Osc2 can run its **own
  independent DCW envelope** (DCW2 Env), so the two PD lines evolve separately.
- **Amp envelope** (DCA) with velocity sensitivity.
- **Pitch envelope** (one-shot AD, bipolar depth) for blips and drops.
- **Two LFOs** (Tri/Saw/Square/S&H/Sine) with delay, each routable to pitch, DCW
  and amp and running **free or tempo-synced** (1/1…1/32, incl. triplets) — layer
  a slow formant sweep under a fast vibrato, etc.
- **Noise** — a per-voice pink-ish source into the mix, shaped by the amp
  envelope (breathy pads, percussive attacks).
- **Tone**: a gentle non-resonant 2-pole low-pass with optional key-follow —
  brightness still comes from the DCW; this is just a tone shaper.
- **Oversample**: Off / 2× / 4× anti-aliasing. PD aliases at high distortion and
  high pitch; 2× (default) cleans it up, Off gives the authentic vintage grit.
- **Stereo chorus** (post-mix ensemble): the dry engine is mono; switch Chorus on
  to bloom it into a wide stereo image. Rate / Depth / Mix; default Off (mono
  passthrough). PD is famously dry — this is the ensemble it usually gets.

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

## Changelog

### v1.3.1
- **Fixed an intermittent chorus crash** (silent machine death). In the chorus
  delay read, when the modulated read position landed an ulp below zero, wrapping
  it by `+bufferSize` rounded to *exactly* `bufferSize` in float — one index past
  the buffer — throwing on roughly 1 sample in many thousand. Rare per-sample, but
  near-certain over a few seconds of chorus, and untied to any one parameter
  (which is why it seemed random). Added a high-side wrap so the read index is
  always in range. Found by fuzzing all 61 params; re-verified over ~130M samples
  with zero faults.

### v1.3
- **Stereo chorus.** The engine was mono out; a post-mix ensemble chorus (one
  delay line, two quadrature LFO taps for L/R) opens it into a wide stereo image
  — the bloom dry phase-distortion usually needs. Params: Chorus (Off/On), Rate,
  Depth, Mix. Defaults **Off** → exact mono passthrough, so existing presets are
  unchanged. Output is soft-clipped post-chorus to stay within nominal range, and
  the tail is flushed on idle so the ring-out isn't chopped. Two demo presets
  added (Pad - Wide Ensemble, Keys - Chorus EP) → 41 total.

### v1.2
- **Independent per-oscillator DCW envelope.** Osc2 can run its own DCW
  (Attack/Decay/Sustain/Release + depth) via the **DCW2 Env** toggle — the two PD
  lines evolve their harmonic content separately, the most CZ-authentic upgrade
  short of full multi-stage envelopes. Defaults **Off** (osc2 follows envelope 1),
  so existing presets are bit-identical.
- **Second LFO.** A second per-voice LFO (Tri/Saw/Square/S&H/Sine, delay, free or
  tempo-synced) with its own routing to pitch / DCW / amp. All depths default 0
  (no effect), so existing presets are unaffected.
- 14 params appended at indices 43–56; three demo presets added (Pad - Two Lines,
  Lead - Dual Mod, FX - Cross Sweep) → 39 total.

### v1.1.1
- **Trigger-click fix.** The amp envelope's attack and release are floored to
  ~3 ms so (re)triggers ramp instead of stepping. An instant attack defeated the
  click-free retrigger (SH101 §6.2): a retriggered voice keeps its oscillator
  phase, but the envelope jumped from the release tail to full in one sample — an
  audible click, worst on looped chords. Retrigger step drops ~14 dB (single
  voice −21 → −37 dB; 6-voice chord −29 → −43 dB). Fresh-note onsets were already
  clean (the decimator's group delay smooths them). Long/normal envelopes are
  unchanged — only sub-3 ms attacks/releases are floored.

### v1.1
- **DCW key-tracking** (`DCW Track`) — DCW follows pitch (bipolar; 64 = off). On
  the resonant shapes this makes the formant track the keyboard like a filter
  with key-follow.
- **Tempo-synced LFO** (`LFO Sync` + `LFO Division`) — lock the LFO to host
  tempo from 1/1 down to 1/32 (incl. 1/8T, 1/16T) for rhythmic wobble/gating;
  `Free` keeps the existing rate knob.
- **Noise source** (`Noise Level`) — per-voice pink-ish noise into the mix
  before the amp stage, so the DCA shapes it (breath on pads, transients on
  plucks). Level-matched to a unity oscillator.
- Six demo presets added; the four new params append after `Volume` (indices
  39–42), so all v1.0.x presets are unaffected.

### v1.0.1
- **Polyphonic headroom.** Voice sum is scaled ~−8 dB before the soft clip so
  chords sum into the clean region instead of hard-clamping (M1 §10 approach).
  Single notes are correspondingly quieter — set level with Volume / the master.
- **Resonant anti-alias clamp.** The formant ratio is now capped against pitch
  so the carrier (`r·f`) can't climb past Nyquist on high notes, removing the
  top-end crackle on resonant presets. Lossless where audible.
- **Steeper decimator.** Oversampling FIR lengthened (16·os+1 taps) with a small
  guard band; alias-region rejection improved from ~−17 dB to ~−75 dB.
