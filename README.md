# F# minor / 96 BPM

A dependency-free F# synthesizer for an instrumental hip-hop song: saturated mono 808, sparse half-time drums, hat rolls, snare/tom fills, piano stabs, bowed string swells, and a plucked lead with stereo echoes. All sounds are synthesized; no samples or NuGet packages are needed.

Requires the .NET SDK with F# Interactive:

```sh
dotnet fsi beat.fsx
open fsharp-minor-song.wav
```

Pass an optional WAV output path with `dotnet fsi beat.fsx my-song.wav`. Existing WAV files at the selected path are overwritten. The original short loop exports remain separate.

To create the shareable MP3, with FFmpeg installed:

```sh
ffmpeg -i fsharp-minor-song.wav -codec:a libmp3lame -b:a 192k fsharp-minor-song.mp3
```

## Arrangement

| Start | Section | Bars | Development |
|---|---|---|---|
| 0:00 | Intro | 4 | Piano, hats enter halfway, pickup fill |
| 0:10 | Verse 1 | 16 | Original sparse groove, intermittent strings |
| 0:50 | Hook 1 | 8 | Full lead motif, strings, extra kicks and cymbal accents |
| 1:10 | Verse 2 | 16 | Sparse melody responses leave room for vocals |
| 1:50 | Breakdown | 4 | Drums drop away, lead fragments and strings |
| 2:00 | Final hook | 12 | Full melody, octave responses in the last four bars |
| 2:30 | Outro | 4 | Drums fall away, fading keys and lead fragments |

The song contains 64 bars plus a 2.5-second tail (2:42.5 total). Output is 44.1 kHz, 16-bit stereo PCM, peak-normalized to −1 dBFS. The 808 stays centered and ducks under the kick. Harmony cycles F# minor–D–A–E, with a downward fifth slide in each third pattern bar.

## Arrangement DSL

The `song` list near the top of `beat.fsx` defines the form:

```fsharp
section "Verse" 16 Verse |> withStrings |> withFills
section "Hook"   8 Hook  |> melody Full |> withStrings |> withFills
section "Break"  4 Breakdown |> melody Tease
```

`section` takes a name, positive bar count, and groove (`Intro`, `Verse`, `Hook`, `Breakdown`, or `Outro`). Modifiers enable strings, fills, and melody modes (`Silent`, `Tease`, `Full`, or `Answer`). Sections can be reordered, repeated, or resized; duration and start times are calculated automatically. Verse strings enter on bars 5–6 of each eight-bar phrase. Fills land every eight verse/hook bars and at section ends. Hooks longer than eight bars receive octave responses in their last four bars.

Edit `motifs` for the melody: each tuple is `(beat position, duration in beats, MIDI pitch)`. Positions are zero-based: `0.0` is beat 1, `0.5` is 1-and, and `2.0` is beat 3. Change `bpm`, `roots`, `chords`, or individual synth functions to develop the sound further.
