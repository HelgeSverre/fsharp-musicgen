# F# minor / 96 BPM

A dependency-free F# synthesizer for a sparse, half-time trap beat. Requires the .NET SDK with F# Interactive.

```sh
dotnet fsi beat.fsx
open fsharp-minor-96.wav
```

An optional first argument selects the output WAV path (existing files are overwritten):

```sh
dotnet fsi beat.fsx my-beat.wav
```

The output is 44.1 kHz, 16-bit stereo PCM: four intro bars, eight verse bars, and a 2.5-second tail. All voices are synthesized, including piano-like stabs and bowed string swells; no samples or external instruments are used.

The verse repeats a four-bar pattern with snares on beat 3, sixteenth hats and occasional rolls. Kicks land on beat 1, then 1-and, then beat 1 with a ghost just before beat 3; the fourth bar has a single softer kick. The saturated mono 808 follows F#–D–A–E and slides down seven semitones in pattern bar 3. Kick-triggered ducking makes room for the transient. Strings swell into the snare on verse bars 5–6 and drop out for bars 7–8.

Edit `bpm`, `roots`, `chords`, and the arrangement loop in `beat.fsx` to develop the idea. Beat positions are zero-based: `0.0` is beat 1, `0.5` is 1-and, and `2.0` is beat 3. The master removes DC and peak-normalizes to −1 dBFS.
