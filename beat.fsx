// Run: dotnet fsi beat.fsx [output.wav]
// All sounds are synthesized; no samples or NuGet packages required.
open System
open System.IO

let bpm = 96.0
let sampleRate = 44100
let beat = 60.0 / bpm
// Small arrangement DSL: sections plus composable instrumentation modifiers.
type Groove = Intro | Verse | Hook | Breakdown | Outro
type Melody = Silent | Tease | Full | Answer
type Section = {
    Name: string; Bars: int; Groove: Groove
    Melody: Melody; Strings: bool; Fills: bool
}
let section name bars groove =
    { Name = name; Bars = bars; Groove = groove; Melody = Silent; Strings = false; Fills = false }
let melody mode section = { section with Melody = mode }
let withStrings section = { section with Strings = true }
let withFills section = { section with Fills = true }
let song = [
    section "Intro"       4 Intro |> withFills
    section "Verse 1"    16 Verse |> withStrings |> withFills
    section "Hook 1"      8 Hook |> melody Full |> withStrings |> withFills
    section "Verse 2"    16 Verse |> melody Answer |> withStrings |> withFills
    section "Breakdown"   4 Breakdown |> melody Tease |> withStrings |> withFills
    section "Final hook" 12 Hook |> melody Full |> withStrings |> withFills
    section "Outro"       4 Outro |> melody Tease
]
do
    if song.IsEmpty || song |> List.exists (fun s -> s.Bars <= 0) then
        invalidArg "song" "The song must contain sections with positive bar counts."
let bars = song |> List.sumBy (fun s -> s.Bars)
let musicLength = float bars * 4.0 * beat
let frameCount = int ((musicLength + 2.5) * float sampleRate)
let left = Array.zeroCreate<float> frameCount
let right = Array.zeroCreate<float> frameCount
let bass = Array.zeroCreate<float> frameCount
let duck = Array.create frameCount 1.0
let rng = Random(96)
let tau = 2.0 * Math.PI
let hz midi = 440.0 * 2.0 ** ((float midi - 69.0) / 12.0)
let noise () = rng.NextDouble() * 2.0 - 1.0
let clamp lo hi x = max lo (min hi x)
let fade duration t = clamp 0.0 1.0 ((duration - t) / 0.025)
let at bar position = (float bar * 4.0 + position) * beat

// Equal-power pan. Optional early reflections only apply to melodic voices.
let voice start duration gain pan room synth =
    let offset = int (start * float sampleRate)
    let gl = cos ((pan + 1.0) * Math.PI / 4.0)
    let gr = sin ((pan + 1.0) * Math.PI / 4.0)
    for j in 0 .. int (duration * float sampleRate) - 1 do
        let i = offset + j
        if i >= 0 && i < frameCount then
            let t = float j / float sampleRate
            let v = gain * synth t * fade duration t
            left[i] <- left[i] + v * gl
            right[i] <- right[i] + v * gr
            if room then
                for delay, level in [| 0.113, 0.19; 0.197, 0.13; 0.311, 0.08 |] do
                    let k = i + int (delay * float sampleRate)
                    if k < frameCount then
                        left[k] <- left[k] + v * gr * level
                        right[k] <- right[k] + v * gl * level

let kick start gain =
    voice start 0.38 gain 0.0 false (fun t ->
        let phase = tau * (46.0 * t + 115.0 * 0.018 * (1.0 - exp (-t / 0.018)))
        let attack = min 1.0 (t / 0.001)
        attack * (sin phase * exp (-t / 0.095) + 0.15 * noise() * exp (-t / 0.006)))
    for j in 0 .. int (0.22 * float sampleRate) do
        let i = int (start * float sampleRate) + j
        if i < frameCount then
            let t = float j / float sampleRate
            duck[i] <- min duck[i] (1.0 - 0.65 * gain * exp (-t / 0.065))

let snare start gain =
    let mutable previous = 0.0
    voice start 0.24 gain 0.0 false (fun t ->
        let n = noise()
        let high = n - previous
        previous <- n
        min 1.0 (t / 0.001) *
            (0.48 * high * exp (-t / 0.052) + 0.4 * sin (tau * 185.0 * t) * exp (-t / 0.038)))

let hat start gain duration pan =
    let mutable previous = 0.0
    voice start duration gain pan false (fun t ->
        let n = noise()
        let high = n - previous
        previous <- n
        let metal = sin (tau * 8039.0 * t) * sin (tau * 5371.0 * t)
        min 1.0 (t / 0.0006) * (0.7 * high + 0.3 * metal) * exp (-t / 0.023))

let sub start duration midi slide =
    let mutable phase = 0.0
    let f = hz midi
    for j in 0 .. int (duration * float sampleRate) - 1 do
        let t = float j / float sampleRate
        let progress = if slide then clamp 0.0 1.0 ((t / duration - 0.40) / 0.42) else 0.0
        let frequency = f * 2.0 ** (-7.0 * progress / 12.0)
        phase <- phase + tau * frequency / float sampleRate
        let envelope = min 1.0 (t / 0.008) * exp (-t / 2.6) * clamp 0.0 1.0 ((duration - t) / 0.07)
        let raw = sin phase + 0.16 * sin (2.0 * phase) + 0.06 * sin (3.0 * phase)
        let i = int (start * float sampleRate) + j
        if i < frameCount then bass[i] <- bass[i] + 0.52 * envelope * tanh (1.9 * raw)

// Inharmonic decaying partials give the keys a struck, piano-like timbre.
let piano start chord gain =
    for midi in chord do
        let f = hz midi
        voice start 0.52 gain -0.27 true (fun t ->
            let tone =
                [| 1.0, 1.0; 2.002, 0.45; 3.009, 0.22; 4.018, 0.10 |]
                |> Array.sumBy (fun (ratio, level) ->
                    level * sin (tau * f * ratio * t) * exp (-t * (8.0 + ratio * 3.0)))
            min 1.0 (t / 0.002) * tone)

// Band-limited additive bowed voices: slow attack, vibrato, gentle detuning.
let strings start chord =
    for midi in chord do
        let f = hz midi
        voice start 0.95 0.048 0.35 true (fun t ->
            let env = (min 1.0 (t / 0.48)) ** 1.7 * exp (-max 0.0 (t - 0.5) / 0.13)
            let vibrato = 0.055 * sin (tau * 5.2 * t)
            let mutable tone = 0.0
            for h in 1 .. 10 do
                let harmonic = float h
                tone <- tone +
                    (sin (tau * f * harmonic * t + harmonic * vibrato)
                     + 0.6 * sin (tau * f * 1.003 * harmonic * t)) / harmonic
            env * tone)

// F# minor, D major, A major, E major. MIDI roots are in the sub register.
let roots = [| 30; 26; 33; 28 |]
let chords = [| [| 66; 69; 73 |]; [| 62; 66; 69 |]; [| 64; 69; 73 |]; [| 64; 68; 71 |] |]

// A mellow plucked lead, with quieter dotted-eighth echoes.
let lead start length midi gain =
    let f = hz midi
    let duration = length * beat + 0.16
    let synth t =
        let env = min 1.0 (t / 0.009) * exp (-t / (0.22 + length * 0.12))
        let phase = tau * f * t + 0.025 * sin (tau * 5.0 * t)
        env * (sin phase + 0.23 * sin (2.0 * phase) * exp (-t * 9.0)
               + 0.10 * sin (3.0 * phase) * exp (-t * 14.0))
    voice start duration gain 0.08 true synth
    voice (start + beat * 0.75) duration (gain * 0.22) -0.45 false synth
    voice (start + beat * 1.5) duration (gain * 0.10) 0.5 false synth

// Each note is (beat position, duration in beats, MIDI pitch).
let motifs = [|
    [| 0.5, 0.5, 78; 1.25, 0.25, 81; 2.5, 0.5, 85; 3.25, 0.5, 81 |]
    [| 0.75, 0.5, 78; 1.5, 0.25, 76; 2.75, 0.75, 74 |]
    [| 0.5, 0.5, 76; 1.25, 0.5, 81; 2.75, 0.25, 85; 3.25, 0.5, 83 |]
    [| 0.75, 0.5, 80; 1.5, 0.25, 76; 2.5, 0.5, 73; 3.25, 0.5, 76 |]
|]

let tom start frequency gain pan =
    voice start 0.25 gain pan false (fun t ->
        let phase = tau * (frequency * t + 1.5 * (1.0 - exp (-t / 0.025)))
        min 1.0 (t / 0.002) * sin phase * exp (-t / 0.065))

let cymbal start gain =
    let mutable previous = 0.0
    voice start 0.8 gain 0.3 true (fun t ->
        let n = noise()
        let high = n - previous
        previous <- n
        min 1.0 (t / 0.002) * high * exp (-t / 0.19))

let fill bar big =
    // Reserve the last beat for a pickup; bigger fills end each section.
    let hits = if big then [| 3.0; 3.25; 3.5; 3.625; 3.75 |] else [| 3.5; 3.75 |]
    for i in 0 .. hits.Length - 1 do
        snare (at bar hits[i]) (0.16 + 0.045 * float i)
    if big then
        tom (at bar 3.25) 155.0 0.23 -0.3
        tom (at bar 3.5) 118.0 0.27 0.2
        tom (at bar 3.75) 85.0 0.30 0.3

let mutable sectionStart = 0
for part in song do
    printfn "%5.1fs  %-12s %2d bars" (at sectionStart 0.0) part.Name part.Bars
    for localBar in 0 .. part.Bars - 1 do
        let bar = sectionStart + localBar
        let pattern = localBar % 4
        let chord = chords[pattern]
        let isHook = part.Groove = Hook
        let fullDrums = part.Groove = Verse || isHook
        let lastBar = localBar = part.Bars - 1
        let fillHere = part.Fills && (lastBar || (fullDrums && localBar % 8 = 7))
        let energy = if part.Groove = Outro then 1.0 - float localBar / float part.Bars else 1.0
        piano (at bar 0.75) chord (0.09 * energy)
        if part.Groove <> Breakdown then piano (at bar 2.75) chord (0.07 * energy)
        if fullDrums then
            let kicks =
                match pattern with
                | 0 -> [| 0.0, 0.95 |]
                | 1 -> [| 0.5, 0.95 |]
                | 2 -> [| 0.0, 0.95; 1.75, 0.30 |]
                | _ -> [| 0.0, 0.55 |]
            for position, gain in kicks do kick (at bar position) gain
            if isHook && pattern % 2 = 0 then kick (at bar 3.0) 0.70
            snare (at bar 2.0) 0.67
            for step in 0 .. 15 do
                let position = float step / 4.0
                if not fillHere || position < 3.0 then
                    let gain = if step % 4 = 0 then 0.12 elif step % 2 = 0 then 0.085 else 0.055
                    hat (at bar position) gain 0.07 (if step % 2 = 0 then -0.13 else 0.18)
                    if (pattern = 1 && step = 14) || (pattern = 2 && step = 7) then
                        for roll in 1 .. 3 do
                            hat (at bar (position + float roll / 16.0)) (0.065 - float roll * 0.01) 0.035 0.2
            let bassPosition = if pattern = 1 then 0.5 else 0.0
            // Short bass gaps before transitions make the next downbeat hit harder.
            let bassEnd = if fillHere then 3.5 else 4.0
            sub (at bar bassPosition) ((bassEnd - bassPosition) * beat) roots[pattern] (pattern = 2)
            if isHook && localBar % 4 = 0 then cymbal (at bar 0.0) 0.13
        elif part.Groove = Intro then
            if localBar >= part.Bars / 2 then
                for step in 0 .. 7 do hat (at bar (float step * 0.5)) 0.055 0.06 0.15
            if lastBar then snare (at bar 2.0) 0.38
        elif part.Groove = Breakdown then
            if localBar = 0 then sub (at bar 0.0) (2.5 * beat) roots[pattern] false
            if lastBar then snare (at bar 2.0) 0.35
        elif part.Groove = Outro && localBar = 0 then
            kick (at bar 0.0) 0.5
            sub (at bar 0.0) (3.5 * beat) roots[pattern] false
        if fillHere then fill bar lastBar
        if part.Strings && (part.Groove <> Verse || localBar % 8 = 4 || localBar % 8 = 5) then
            strings (at bar 1.15) chord
        let notes =
            match part.Melody with
            | Silent -> [||]
            | Full -> motifs[pattern]
            | Answer -> if localBar % 4 = 3 then motifs[pattern] |> Array.skip 2 else [||]
            | Tease -> motifs[pattern] |> Array.truncate 2
        for position, length, midi in notes do
            lead (at bar position) length midi ((if isHook then 0.15 else 0.09) * energy)
        // An octave response lifts the last four bars of the final hook.
        if isHook && part.Bars > 8 && localBar >= part.Bars - 4 then
            for position, length, midi in motifs[pattern] |> Array.skip 2 do
                lead (at bar position) length (midi + 12) 0.045
    sectionStart <- sectionStart + part.Bars

// The saturated sub is summed identically into L/R and ducked by the kick.
// Linear mastering preserves its mono image; remove DC and normalize to -1 dBFS.
for i in 0 .. frameCount - 1 do
    let low = bass[i] * duck[i]
    left[i] <- left[i] + low
    right[i] <- right[i] + low

let removeDc (samples: float[]) =
    let mutable previousInput = 0.0
    let mutable previousOutput = 0.0
    for i in 0 .. samples.Length - 1 do
        let current = samples[i]
        let filtered = current - previousInput + 0.999 * previousOutput
        previousInput <- current
        previousOutput <- filtered
        let endFade = clamp 0.0 1.0 (float (samples.Length - 1 - i) / (0.1 * float sampleRate))
        samples[i] <- filtered * endFade

removeDc left
removeDc right
let peak = max (left |> Array.fold (fun peak x -> max peak (abs x)) 0.0) (right |> Array.fold (fun peak x -> max peak (abs x)) 0.0)
let masterGain = if peak > 0.0 then 10.0 ** (-1.0 / 20.0) / peak else 1.0
let output =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [||] -> Path.Combine(__SOURCE_DIRECTORY__, "fsharp-minor-song.wav")
    | [| path |] -> Path.GetFullPath path
    | _ -> failwith "Usage: dotnet fsi beat.fsx [output.wav]"

let writeWav path =
    use stream = File.Create path
    use writer = new BinaryWriter(stream)
    let ascii (s: string) = writer.Write(Text.Encoding.ASCII.GetBytes s)
    let dataBytes = frameCount * 4
    ascii "RIFF"
    writer.Write(36 + dataBytes)
    ascii "WAVE"
    ascii "fmt "
    writer.Write(16)
    writer.Write(1s) // PCM
    writer.Write(2s) // Stereo
    writer.Write(sampleRate)
    writer.Write(sampleRate * 4)
    writer.Write(4s)
    writer.Write(16s)
    ascii "data"
    writer.Write(dataBytes)
    let pcm sample = int16 (Math.Round(clamp -1.0 1.0 (sample * masterGain) * 32767.0))
    for i in 0 .. frameCount - 1 do
        writer.Write(pcm left[i])
        writer.Write(pcm right[i])

writeWav output
printfn "Rendered %s\n%.0f BPM · F# minor · %d bars · %.1f seconds" output bpm bars (float frameCount / float sampleRate)
