# The track player's listening checklist

**Nothing automated hears anything.** The suite can prove that every Vorbis packet decodes, that
every note lands on a patch the cache declares, and that the mix is a non-zero stereo signal. It
cannot prove that the signal is *the right one*. A synthesiser that reads every byte correctly and
mixes them wrongly passes every test that exists, in exactly the way a renderer that draws the
wrong triangles passes every test in `reference/viewer-eyeball-checklist.md`.

So the player is judged by a person with headphones, and this file is what that person reads. Every
entry names what **correct** sounds like and what a **plausible wrong** result sounds like, because
a player that produces music is the failure mode to worry about, not one that produces silence.

**Do not claim the player sounds right on the strength of a green suite.** That is the exact claim
this project forbids, and the reason this file exists.

Run against the default cache, the vanilla b639 capture. Every figure below was measured identical
in both supported caches by `RealCacheTrackPlaybackTests` and `RealCacheMidiSampleMappingTests`, so
they are properties of build 639 rather than of one capture. Tracks are named by **id**, not by
title: the index-6 name join is only partly verifiable and this project has already been wrong
about it once, so an id is the only address worth writing down. The one exception is track 0, whose
identifier is `hash("scape main")` and which is therefore settled on its own.

---

## Results so far

**Not yet run.** The player was built and this checklist was written in the same pass, and the
agent that wrote both cannot hear. Every row below is unverified.

| Case | Verdict |
|---|---|
| **A** through **H** | **NOT YET RUN** |

## What the player is measured to do

From `RealCacheTrackPlaybackTests`, five seconds of each track, identical in both caches. These are
the numbers a listener is checking the *sound* against - they say the notes arrived, not that they
are right.

| Track | Events | Division | Peak | Peak voices | Patches sounded | Held notes |
|---|---|---|---|---|---|---|
| **0** | 21,675 | 960 | 10,132 | 23 | 7 | 32 |
| **1** | 12,981 | 480 | 26,525 | 53 | 6 | 103 |
| **62** | 5,209 | 480 | 3,099 | 8 | **1** | 12 |
| **100** | 11,644 | 480 | 5,289 | 11 | 2 | 13 |
| **150** | 6,414 | 960 | 6,336 | 19 | 2 | 37 |
| **321** | 14,434 | 480 | 21,335 | 17 | 7 | 46 |
| **500** | 11,277 | 960 | 22,914 | 22 | 3 | 41 |
| **700** | **338** | 960 | 4,610 | 21 | 2 | 43 |
| **900** | 7,400 | 480 | 18,829 | 8 | 3 | 8 |
| **962** | 5,530 | 480 | **30,548** | 56 | 6 | 92 |

Every one of them: **zero notes dropped, zero index-4 lookups, zero failed lookups**, and left and
right differing, so the pan path is doing something.

Bank-wide, from the patch census: **21,477 of 21,491 sounding keys draw on index 14** and only 14
draw on index 4; **17,483 keys are held**, meaning their sample loops for as long as the note lasts;
**45 keys carry a mute group**; and **21,363 keys name an envelope whose vibrato rate is above
zero**, so vibrato is very nearly universal and any vibrato defect is a defect in almost every note.

---

## A. It is not General MIDI - the positive case

Tracks tab, track **0**, Play. Listen to the first fifteen seconds.

- **Correct.** Recognisably the game's own instruments: soft, slightly grainy sampled voices with
  audible 8-bit texture on sustained notes. It should sound like the game, quantisation noise and
  all.
- **Wrong, silently fell back.** Clean, glossy, obviously-synthesised voices - a piano that sounds
  like a piano sample library, strings that sound like a string patch. That is the Windows GM synth
  and it is the thing this feature exists to replace. **There is no GM fallback in the code**, so
  hearing one means something is routing MIDI to `midiOut` rather than rendering it.
- **Wrong, right notes and wrong bank.** Music with the correct melody and rhythm on plainly wrong
  instruments, consistently - a lead line on a drum-ish timbre, or everything on one sound. Suspect
  the bank-select combination. `MidiSynthesiserTests.ProgramChange_CombinesBothBankSelectControllers`
  pins the arithmetic; what it cannot pin is that the id reaches the right group.

**Track 62 is the isolate for this**: it sounds only **one** patch in its first five seconds, so
whatever you hear is that one instrument and nothing is masking it.

## B. Pitch - the most likely wrong thing to sound plausible

Track **900**, which peaks at only 8 voices and is sparse enough to follow a line.

- **Correct.** The melody is in tune with itself and octaves land where they should. Play the
  exported MIDI in any player alongside it: the **notes and their intervals** must match, even
  though the instruments will not.
- **Wrong, root note ignored.** Every note is transposed by a fixed and often large interval, and
  the piece is internally in tune. That is the tuning word's coarse byte not being subtracted -
  `(note << 8) - (tuning & 0x7fff)` becoming `note << 8`.
- **Wrong, fine tune only.** Everything is slightly, uniformly sharp or flat - within a semitone -
  and otherwise correct. The low byte of the tuning word is being applied and the high byte is not,
  or vice versa.
- **Wrong, wrong exponent base.** Intervals are stretched or compressed: an octave in the score
  sounds like a fifth or like two octaves. The step exponent divides by 3072; using 1200 (cents) or
  256 gets exactly this.
- **Wrong, sample rate ignored.** Some instruments are in tune and others are transposed by a fixed
  amount that differs per instrument. The step is `sampleRate * 256 * 2^(offset/3072) / 22050`, and
  dropping the sample's own rate leaves every sample not recorded at 22050 Hz out of tune with the
  rest.

## C. Envelopes - right notes, wrong shape

Track **700**. Only **338 events** in five seconds against 21 simultaneous voices, so it is almost
entirely long sustained notes: the case where an envelope defect has nowhere to hide.

- **Correct.** Notes swell and decay smoothly. Releases fade rather than stopping.
- **Wrong, no attack.** Every note starts at full volume instantly, giving a hard organ-like edge on
  what should be a soft entry. The attack chain is not being walked.
- **Wrong, click at note end.** A short tick or pop when each note stops. The release chain's two
  implied points are missing - it must **start at level 64 and end at level 0**, and neither is
  stored in the file.
- **Wrong, buzz on sustained notes.** A regular low buzz, roughly 100 Hz, under held notes. The gain
  is stepping once per control tick instead of ramping across it. 100 Hz is exactly the control-tick
  rate, so the pitch of the buzz identifies the cause.
- **Wrong, wrong envelope rate.** Attacks and releases are audibly too fast or too slow, and
  consistently so across a piece. One stored time unit is two control ticks - 20 ms - so a factor of
  two here is a factor of two on every fade.

## D. Vibrato

Any sustained note on track **150** or **700**. **21,363 of the bank's 21,491 keys** name an
envelope with a non-zero vibrato rate, so this is nearly every note in the cache.

- **Correct.** A gentle pitch wobble that **fades in** over the first fraction of a second of a held
  note rather than being present from the attack.
- **Wrong, no ramp.** The wobble is at full depth from the instant the note starts. The ramp is
  `ticks * depth / (delay << 1)` and skipping it makes every entry seasick.
- **Wrong, far too deep.** Obvious warbling, close to a semitone. The depth is shifted left by two
  before use; shifting it further, or not at all, changes it by a factor of four either way.
- **Wrong, tremolo instead of vibrato.** The **volume** wobbles rather than the pitch. The
  oscillator has been wired into the gain rather than into the step.

## E. Stereo and panning

Track **321** on headphones, which sounds seven patches.

- **Correct.** Instruments sit at distinct places in the stereo field and the image is stable.
  Centre-panned material is equally loud in both ears.
- **Wrong, collapsed to mono.** Everything in the middle. Either the key pan is being ignored or the
  channel pan is overriding it instead of bending it - the channel's pan does not replace the key's,
  it pulls it.
- **Wrong, hard-panned everything.** Every instrument fully left or fully right, nothing in between.
  A pan value is being read as a flag rather than as a position.
- **Wrong, level jump when panned.** A sound gets **louder** as it moves off centre. The split is
  constant power - `sqrt` of the two halves - and using a linear split instead makes a hard-panned
  voice quieter, while omitting the split entirely makes it louder in one ear with no compensation.
- **Wrong, swapped.** Left and right reversed. Nothing else sounds wrong, which is why it needs
  checking deliberately: pan low is left.

## F. Stuck and cut notes

Track **1** (103 held notes in five seconds, up to 53 voices) and track **962** (92 held notes, 56
voices). **17,483 of the bank's 21,491 keys are held**, so a note-off defect affects most of the
cache.

- **Correct.** Notes stop when the music stops. Let a track play to the end: it goes quiet.
- **Wrong, stuck note.** One or more pitches drone on indefinitely under the music and never stop,
  usually growing more obvious as a piece goes on. A held key's sample loops forever and only the
  release envelope ends it, so this is a note-off, sustain-pedal or release-envelope failure.
- **Wrong, everything cut short.** Notes stop abruptly and the texture is thin and staccato where it
  should be legato. The release is starting immediately or the held flag is not reaching the voice.
- **Wrong, sustain pedal ignored.** Passages that should ring through a chord change instead clear
  on each note-off. Controller 64 stops the release envelope advancing; it does not release
  anything.

## G. Level and clipping

Track **962**, which peaks at **30,548** against a 32,767 full scale - the closest to the rail of
anything measured.

- **Correct.** Loud, and clean. No crackle on the peaks.
- **Wrong, distortion on peaks.** Crackle or fuzz in the loud passages of 962 while quieter tracks
  are clean. The accumulator has eight bits of headroom above a full-scale voice and is clamped at
  plus or minus 8,388,607 before the top 16 bits are taken; clamping to 16 bits first, or omitting
  the shift, produces exactly this.
- **Wrong, far too quiet.** Track 62 peaks at 3,099 and is genuinely a quiet piece, but if
  **everything** needs the volume control at maximum, a gain stage is being shifted too far. The
  chain squares the volume-and-expression product, so getting one shift wrong changes the level by a
  large factor.

## H. Timing

Any track with the Loop box ticked, listened to across the loop point.

- **Correct.** Tempo is steady, and matches the exported MIDI played in any other player. Tracks 0,
  150, 500 and 700 use division 960 and the rest use 480; both must play at the same musical speed,
  so a division that was being ignored would make half of these play at double or half tempo.
- **Wrong, tempo ignored.** Everything plays at 120 BPM regardless. The default tempo is being kept
  and the tempo meta events are not reaching the sequencer.
- **Wrong, loose timing.** Fast passages sound sloppy, with notes landing slightly off the beat in a
  way that is not in the score. Events are being quantised to the 10 ms control grid instead of
  splitting the block at the event.
- **Wrong, gap or overlap at the loop point.** A silence or a stumble when the track repeats.

---

## What is still unverified after all of this

- **Mute groups.** Only **45 keys in the whole bank** carry one, and **none of them sounded** in the
  five seconds sampled from any of the ten tracks above. The behaviour - a second note in a group
  cutting the first, which is how a closed hi-hat stops an open one - is therefore implemented and
  entirely unexercised. Finding a track that exercises it is an open task, and until one is found
  nothing here can say whether it works.
- **Index-4 keys.** 14 keys of 21,491 are silent by design and none of the sampled tracks reaches
  one, so their absence has not been heard either. If a track is found that does, the symptom is a
  missing percussive element rather than anything wrong with what does play.
- **Whether it matches the client.** This is a defect check, not a conformance check. The player
  omits voice stealing, portamento and the CC81 re-trigger deliberately, and the only way to settle
  a match would be to capture the client's own output and compare - which nothing here does.
- **The eight-bit texture.** The client's own output is 8-bit PCM per voice and this reproduces
  that, so a listener expecting CD-quality audio will hear quantisation noise and should not report
  it as a defect. If it sounds *cleaner* than the game, something has been widened that should not
  have been.
