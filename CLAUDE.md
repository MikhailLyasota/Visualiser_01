# CLAUDE.md

## What this is

A **MIDI-driven music visualizer** for the owner's progressive metal solo project, rendered offline to video.
Vision: a camera floating through geometric abstractions, with lights and objects reacting to MIDI events
(drums first, other tracks too). It must be **re-skinnable**: new scenes, custom imported models, different looks per song.

- Engine: **Unity 6 (6000.6.9f1)**, **HDRP** (17.6). Quality assets live in `Assets/Settings/` (Performant / Balanced / High Fidelity).
- Targets: **macOS (Metal) and Windows (DX12/Vulkan)** — editor and builds. No platform-specific code or native plugins unless they ship for both.
- Primary output is **video** (offline render), not a realtime show. Realtime preview in the editor is for authoring.
- Project is currently a near-empty HDRP template (`Assets/Scenes/SampleScene.unity`, `Assets/TutorialInfo/` is template junk and can be deleted).

## Hard requirements (don't regress these)

1. **Musical correctness**
   - Read Standard MIDI Files (type 0 and 1), honoring the full **tempo map** (multiple tempo changes) and **time-signature changes**.
   - Store events by absolute tick; convert ticks → seconds via the tempo map. Never assume 4/4 or a constant BPM.
   - **Polyrhythms** need no special case if timing is tick-accurate; but beat/bar helpers must expose the *real* bar/beat grid from time-sig events, and allow per-reactor custom subdivisions (e.g. a layer pulsing in 7 against the bar's 4).
2. **Flexible MIDI mappings**
   - Note numbers are mapped to **named semantic events** (`Kick`, `Snare`, `HiHatClosed`, `Crash`, `Tom1`…) via a `ScriptableObject` mapping asset, keyed by track name/index and/or channel.
   - Ship presets (General MIDI drums at minimum; add others like Superior Drummer / EZdrummer / GGD as needed). Multiple notes may map to one event (e.g. all kick articulations → `Kick`).
   - Visual code subscribes to semantic event names, **never raw note numbers**.
   - Velocity, duration (note-off), and CC (e.g. hi-hat openness) should be available to reactors.
3. **Deterministic, render-safe timing**
   - All visuals are driven by a single **song clock** (seconds since song start), not `Time.deltaTime` or wall-clock.
   - Any frame must be reproducible from the song time alone (scrubbing, offline rendering at fixed framerate, re-renders). Decay/envelopes are computed from "time since event", not accumulated per frame. Randomness is seeded.
   - Audio (rendered mix, WAV) syncs to the same clock; support an offset to align MIDI with the audio file.
4. **Extensible scenes & content**
   - Core (MIDI loading, clock, mapping, event dispatch, render pipeline) is scene-agnostic and lives separately from scene content.
   - A "scene/world" = a Unity scene or prefab containing reactors + camera path. Swappable per song without code changes.
   - User-imported models (FBX/OBJ) should be drop-in: a generic reactor component can be added to any GameObject (light intensity/color flash, emission pulse, scale punch, material property, VFX burst, camera shake, etc.).
   - Prefer data/config (ScriptableObjects, inspector settings) over hardcoded values so the owner can tweak looks without coding.

## Suggested architecture

```
Assets/Visualizer/
  Core/        MIDI parsing, TempoMap, SongClock, MidiMapping (SO), event dispatch  (own asmdef, no scene deps)
  Reactors/    MonoBehaviours reacting to semantic events (light flash, emission, transform punch, VFX…)
  Camera/      Camera path / flythrough (spline or Cinemachine), beat-aware motion
  Rendering/   Offline render setup (Unity Recorder), fixed framerate capture
  Editor/      Inspectors, mapping editor, import helpers
  Mappings/    MIDI mapping preset assets
Assets/Scenes/ (per-world scenes)   Assets/Songs/ (MIDI + audio per song)   Assets/Art/ (user models, materials)
```

- MIDI file parsing: a small custom SMF parser is fine (no realtime MIDI input needed); a managed library (e.g. DryWetMIDI) is acceptable if it works on both Mac and Win without native binaries.
- Video rendering: use **Unity Recorder** (`com.unity.recorder`, not yet installed) at a locked framerate; driving playback via **Timeline** (module present) is a good fit for scrubbing/preview.
- Use assembly definitions for `Core` to keep compile times low and dependencies clean.

## Conventions

- C#, Unity 6 APIs, HDRP-compatible shaders/materials only (Shader Graph / HDRP Lit). No URP/Built-in materials.
- Input via the new **Input System** (package installed); don't use legacy `Input`.
- Keep `.meta` files with their assets; never hand-edit GUIDs.
- Don't commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build*/`.
- Version control: git is used here; Plastic SCM (`.plastic/`, `ignore.conf`) is also present — don't remove it without asking.
- `com.coplaydev.unity-mcp` (MCP for Unity) is installed, so Claude can inspect/manipulate the editor when the MCP server is connected. Prefer it for scene/asset changes over hand-editing YAML.

## Working with the owner

- The owner is a musician; explain Unity/graphics decisions briefly and in plain terms.
- When adding a visual feature, expose its knobs in the Inspector and say where they are.
- Verify timing changes against a MIDI file with tempo + time-signature changes, not just a simple 4/4 loop.
