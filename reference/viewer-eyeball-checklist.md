# The 3D viewer's eyeball checklist

**Nothing automated verifies the 3D viewport.** No BitBlt capture sees the OpenGL surface: a
`GLControl` clearing to magenta captures blank through `CopyFromScreen` and through `PrintWindow`
flags 0 to 3, in this application and in a minimal one outside the repository. That rectangle shows
whatever GDI last blitted into it, which reads convincingly as the previous page bleeding through
and is not. One investigation was already lost to that phantom, and `tools/Capture-EditorTab.ps1`
is on that family, so it is no evidence at all about this viewport.

**A DWM-composited screen capture DOES see it, confirmed 2026-08-09.** A grab taken with the
Windows Snipping Tool showed the viewport's particle quads, the wireframe and the overlay marks
correctly, on the same machine where every BitBlt path returns blank. The distinction is the
compositor: a GL surface bypasses GDI, so anything reading the window's GDI device context gets
nothing, while anything reading DWM's composed output gets the real pixels.

That matters because it means **this checklist is semi-automatable and nobody has built it**. A
driver using `Windows.Graphics.Capture` or Desktop Duplication, rather than `CopyFromScreen`, could
settle the cases below that are currently marked as needing a human. Until someone writes it, the
human pass stands. Do not "fix" `Capture-EditorTab.ps1` by pointing it at the GL rectangle - the
right move is a second tool on the DWM path, leaving the BitBlt one alone for WinForms panels,
where it works and is faster.

So the viewer is judged by a human at the monitor, and this file is what that human reads. Every
entry names what **correct** looks like and what a **plausible wrong** result looks like, because a
viewer that draws something is the failure mode to worry about, not one that draws nothing.

Run against the default cache, the vanilla b639 capture. Every id and figure below was measured
identical in both supported caches, so they are properties of build 639 rather than of one capture.
They were chosen by probing index 21, decoding 2,956 spot animations and examining 1,785 distinct
models, to find cases that isolate one failure each.

**What the readouts prove and do not prove.** The numbers beside the viewport come from
`SkeletalAnimator` and the particle system, not from the framebuffer, so they prove the animation
reached the model. They say nothing about whether it drew. Both halves matter and this file asks
for both.

---

## Results so far

First human pass, 2026-08-09, against the repack (the model list read 63,614).

| Case | Verdict |
|---|---|
| **A** skeletal animation | **PASS.** Readout `0.000 s of 3.360 s` over `frame 0/13`, run observed at about 3.4 s with the shape visibly changing. Rules out the rate conflation, which would have finished in about 0.43 s |
| **F** particles | **PASS, and both halves now confirmed.** The first pass passed model 62810 on its **readout** alone - `particles 68/2047, emitters 2/2` against a predicted peak near 73 - which proves the simulation ran and the emitters resolved and says nothing about whether anything reached the screen, because the readout comes from `ParticleSystem` and not from the framebuffer. On **2026-08-09 a human confirmed model 19074 visibly drawing particles**, which is what promotes this case from "the simulation runs" to "particles render". That confirmation was taken **after** the DPI-awareness change, which altered the process-wide awareness context and which nobody had checked GL survived. It did |
| **D** hover overlay | **OPEN.** Amber and blue marks are present near the shape. Whether they read as `face N` and `vN`, and whether the numbers fall in 0-7 and 0-23, is not yet settled |
| **H** multi-part entity | **PASS, confirmed on the monitor 2026-08-09.** NPC 1 with animation 811, the exact case a human reported as broken - jaw, hands and boots coming away - reads correctly after the composite merge. Both halves now agree: the seam measures 0 model units in both caches, and a person says the body reads as one object. Worth keeping as the pattern: the defect was found by eye, turned into a number, fixed against the number, and then confirmed by eye again |
| **F3/F4** particle position and material | **PASS, both confirmed on the monitor 2026-08-09.** F3: after the clock-unit fix the smoke sits at the cape's hem rather than detached below and behind it. F4: after the type-7 mono blend fix and the material draw, model 59885 renders soft orbs fading to nothing rather than hard opaque squares. Both were reported as defects by a human first, measured second, and confirmed by eye last |
| **B, C, E, G, I** | **NOT YET RUN** |

**Known limitation, reported 2026-08-09 and not yet fixed:** particles render only while the
Entities type selector is on **Models**. Selecting an item, NPC or object shows the mesh without
its particles, so a cape viewed as an item looks like a cape viewed as a model with the effect
missing. Do not read that as a particle defect while running these cases - pick Models.

Case H was added after this pass, which is why it moved Layout from H to I. A verdict recorded
against "H" before 2026-08-09 was about layout.

The tooltip also confirmed model 62810 as 24 vertices and 8 triangles, which is why it was chosen.

## A. Skeletal animation, the positive case

Models or Entities tab, model **49768**. Animation **12358**. Play.

- **Correct.** Status reads `Frame N of 13, 47 of 47 transforms applied.` The counter walks
  `frame 0/13` to `frame 12/13`, elapsed reaches about **3.360 s**, and the shape visibly changes
  across those steps.
- **Wrong, upload.** The counter climbs and the mesh is frozen. The pose is being computed and never
  reaches the vertex buffer.
- **Wrong, rate conflation.** The run finishes in about **0.43 s** instead of 3.36 s. The playhead is
  advancing one step per redraw rather than on the animation's own stored durations. These are
  different clocks and conflating them makes every animation play at the wrong speed.
- **Wrong, scale.** The mesh appears about **16x** too large. A pose was left in sixteenths, meaning
  `PosedMesh.Finish` was skipped on some path.

## B. The negative control - an animation that reaches nothing

Model **15748**, animation **12358**.

- **Correct.** Status reads `Frame 0 posed, but no transform reached this model - 47 resolved, 47
  matched no label.` The counter still climbs and **the model does not move**.
- **Wrong.** The model moves at all, or the status claims `47 of 47 transforms applied`. The label
  join is reaching vertices it should not.

This case matters more than case A. A viewer that animates everything looks healthier than one that
correctly refuses, which is exactly why it is easy to ship the broken one.

## C. Partial reach

Model **51296**, animation **12635**, with Loop ticked - it is 13 steps over 0.600 s, so it repeats
quickly enough to watch.

- **Correct.** `101 of 102 transforms applied`, one transform with no target.
- **Wrong.** `102 of 102` or `0 of 102`. Either means the join is not discriminating.

## D. The hover overlay - both index kinds at once

Model **62810**, chosen because it is tiny: **24 vertices, 8 faces**. Tick Index overlay. Hover over
the shape.

- **Correct.** An amber translucent triangle under the cursor; one **amber** `face N` label at its
  centre with `0 <= N <= 7`; three **blue** `vN` labels beside the corners with `0 <= N <= 23`; each
  label on a dark backdrop. The readout appends `hover face N vA/vB/vC` matching the labels.
- **Wrong, nothing drawn.** The readout names a face but no labels appear. GDI text drawn after
  `SwapBuffers` is being discarded. **This is the most likely failure in the whole list**, because
  GDI on the same DC immediately after a buffer swap is driver-dependent. The fix is to move the
  label pass out of `Gl_Paint` into a transparent child control docked over the GL surface, which
  costs nothing else in the design.
- **Wrong, mirrored.** Labels appear reflected top to bottom about the centre row - a missing y flip.
  **Check off centre.** The centre row is the one place a flip is invisible.
- **Wrong, scale disagreement.** The amber highlight sits on a different triangle from the one under
  the cursor. The picker and the uploader are on different scales.
- **Wrong, index spaces crossed.** A `v` number above 23, or a `face` number above 7.

Then model **15748**: it carries **5 render-type-2 faces out of 435**, and the pick mesh reports
**430** triangles. Hovering must never highlight one of those five, and no black sliver should be
drawn. The client refuses to draw render type 2, and faces it refuses were being drawn here for as
long as the viewer has existed without any sweep noticing.

## E. Emitter annotation on the face label

Model **57600**, which carries 6 emitters. Hover until a face label reads `face N [emitter M]`.

- **Correct.** The bracket appears only on the **amber face** label.
- **Wrong.** It appears on a blue `vN` label. Emitters and effectors have been crossed, which is
  precisely the defect the two-colour overlay exists to catch: an emitter anchors to a **face** and
  an effector anchors to a **vertex**.
- **Expected absence, not a defect.** `[effector ...]` should never appear on any model here. Of the
  1,785 distinct spot-animation models examined across both caches, **none carries an effector
  attachment**. Its absence is data, not a bug.

## F. Particles

Tick Particles.

| Model | Correct |
|---|---|
| **19074** | 1 emitter. Live count climbs and **saturates at 2047/2047** within about two seconds, `emitters 1/1`. Quads keep facing the camera while you orbit |
| **57600** | peak about **669** live, `emitters 6/6` |
| **62810** | peak about **73** live, `emitters 2/2` - the small, easy-to-read case |
| **63586** | `emitters 1/1` and **`particles 0/2047`** with nothing on screen is **correct**. Its emitter spawns and kills within a single 33 ms step. Do not read this as a dead simulation |

Wrong, for 19074: live stays 0; or the count exceeds 2047, meaning the cap is not honoured; or quads
stay fixed to the world as you orbit, meaning the camera basis is wrong; or the cloud punches holes
in itself, meaning depth writes were left on for the particle pass.

## F2. Which models actually carry particles, and how rare that is

**Most models have no emitter, and a model with no emitter reads `emitters 0/0` with
`particles 0/2047`. That is correct output, not a broken viewer.** Picking models at random and
concluding particles are broken is the failure this section exists to prevent, and it has already
happened once: a user looking for the Dungeoneering master cape's particles typed **19709** and
**19710** into the model viewer and saw nothing.

**Those are item ids, not model ids.** Items 19709 and 19710 are both named "Dungeoneering master
cape" in index 19 of both caches, and the models they name are inventory **59888**, worn male
**59885**, worn female **59887**. Index 7 does hold groups 19709 and 19710, and they are unrelated
28-vertex and 27-vertex meshes whose stored flags byte is `0x00`, so they carry no particle tail
block at all and no spot animation references either. The belief behind the attempt was right:
**59885 and 59887 each carry 5 emitters**. Only the id was from the wrong index.

**The population.** Sweeping every group index 7 declares and reading the tail block that
`ModelCodec.ReadTail` parses under flag `0x2`:

| | vanilla b639 | repack |
|---|---|---|
| Models declared | 63,607 | 63,614 |
| Carrying at least one emitter | **211** | **215** |
| Emitter attachments in total | 555 | 569 |
| Carrying at least one effector | **4** | **4** |
| Effector attachments in total | 12 | 12 |

So roughly **one model in 300** has particles at all. The vanilla 211 are a subset of the repack's
215 and every shared row is identical, attachment for attachment; the repack adds 23608, 25755,
63609 and 63613. Of the 211, **119 are reachable through a spot animation** and 92 are not - worn
equipment like the master cape among them - so index 21 is a route to little more than half of them.

**The effector claim is confirmed at its stated scope and sharpened.** Of the 1,785 distinct models
that index 21's 2,956 spot animations reference, **none carries an effector attachment**, exactly as
case E says. Across the whole of index 7 there are **four**, the same four in both caches: 51221,
51222, 51224 and 51225, twelve attachments between them, and **none of the four is referenced by any
spot animation**. So `[effector ...]` on a face label is still wrong wherever case E can reach, and
those four models are the only place in either cache where an effector label is legitimate at all.

### The ranked demonstration models

Ordered by how obvious the effect is. Emitter counts are read from the model's tail block; peak live
counts are from `ParticleSystem` driven at 33 ms steps for 10 s with the default seed and the 2047
cap. **Every figure below was re-measured against the repack and came out identical**, so they are
properties of build 639 rather than of one capture. A peak is seed- and step-schedule dependent, so
treat it as a target to within about ten per cent rather than an exact number - the same reason
62810 is listed here at 76 while a human read 68 off the screen and case F predicts 73.

| Rank | Model | Emitters | Peak live | What it should look like | Evidence |
|---|---|---|---|---|---|
| 1 | **19074** | 1 | **2047**, saturating | A dense amber-brown plume that fills the cap and stays there | **Seen drawing by a human, 2026-08-09** |
| 2 | **11889** | 1 | **2047** | White-pink, and primed 10,000 steps, so it is already full on the first frame rather than building up | Decoded and simulated only |
| 3 | **58633** | 4 | **2047** | Grey smoke rising from four separate points on one mesh | Decoded and simulated only |
| 4 | **57595** | 7 | 1509 | Seven streams in different colours at once - white, black, blue, green, violet and two pale yellows | Decoded and simulated only |
| 5 | **19076** | 2 | 1219 | Two heavy streams on a 796-vertex mesh | Decoded and simulated only |
| 6 | **55477** | 12 | 1031 | Pale blue, twelve emitters spread over the largest mesh in this list (2543 vertices) | Decoded and simulated only |
| 7 | **57600** | 6 | 694 | Red, green and blue in pairs. The case E model, so the face label should read `face N [emitter M]` | Decoded and simulated only |
| 8 | **60332** | 4 | 688 | Cyan and yellow on a **12-vertex, 4-face** mesh, so the geometry is almost nothing and the cloud is the whole picture | Decoded and simulated only |
| 9 | **62585** | 6 | 588 | Orange fire over dark brown smoke, two ramps running at once | Decoded and simulated only |
| 10 | **51849** | 6 | 348 | Six emitters on an 18-vertex, 6-face mesh - one per face | Decoded and simulated only |
| 11 | **62083** | 17 | 140 | **The most emitters of any model in either cache**, all seventeen sharing one definition. Sparse and orange-brown; its spawn alpha is stored as zero and the ramp raises it to 209, so a particle fades in rather than appearing | Decoded and simulated only |
| 12 | **59885** / **59887** | 5 | 78 | The Dungeoneering master cape as worn, male and female. Near-black particles at 150 to 200 alpha, low rate, on five of the mesh's last faces | Decoded and simulated only |
| 13 | **62810** | 2 | 76 | The small readable case, 24 vertices and 8 faces. Already passed by eye in the first pass | Readout confirmed 2026-08-09 |
| 14 | **62446** | 7 | 19 | Seven emitters and still almost nothing on screen: lifetimes of 10 to 50 ms make it a sputter, not a cloud | Decoded and simulated only |
| 15 | **63586** | 1 | **0** | Nothing. Spawns and kills inside one 33 ms step | Decoded and simulated only |
| 16 | **59584** | 8 | **0** | Nothing, from **eight** emitters. The stronger version of the case below | Decoded and simulated only |

**Read the Evidence column literally.** Only 19074 has been seen drawing, and only 62810 has had its
readout checked against a prediction on the monitor. Everything else in this table is a decoded
attachment count and a simulated peak, which says the emitters resolve and the arithmetic runs, and
says nothing about the framebuffer. Do not let row 1's confirmation stand in for the rest of the
column.

- **Correct.** The readout's emitter denominator matches the Emitters column exactly, the live count
  climbs towards the peak, and for 19074 quads are visibly on screen and keep facing the camera as
  you orbit.
- **Not a defect, though it looks like one.** `emitters 0/0, particles 0/2047` on a model that is not
  in this table. 211 models out of 63,607 have an emitter; the overwhelmingly likely explanation for
  an empty readout is the model, not the viewer.
- **Not a defect either.** `emitters 1/1, particles 0/2047` on 63586, or `emitters 8/8,
  particles 0/2047` on 59584. **59 of the 211 emitter models peak at zero** over a ten second run,
  because they spawn and kill within a single step. More than a quarter of the population shows
  nothing, which is why picking one at random is a bad way to test this.
- **Plausible wrong, and the trap to know about.** Model **11890** reads `particles 2047/2047` and is
  all but invisible: its particles never exceed alpha 10 out of 255. A full live count with an empty
  screen is a legitimate result for that model, so it is a terrible choice of demonstration and a
  terrible choice of regression case. Use 19074, where the peak alpha is 121.
- **Wrong.** The emitter denominator is lower than the Emitters column above, which means an
  attachment named an index-27 emitter the cache does not hold, or the tail block was mis-parsed.
  `MissingEmitterCount` was measured at **0** for every one of the 211 in both caches, so any nonzero
  value here is a regression rather than data.
- **Wrong.** A model in this table showing `emitters 0/0`. The attachment block is being skipped
  entirely, most likely by mis-reading the model's flags byte - the emitter list is gated on bit
  `0x2`, and 19709's `0x00` against 59885's `0x0B` is the difference between the two cases.

### F3. The Dungeoneering master cape, and what a wrong particle clock looks like

Models or Entities tab, model **59885** (worn male) or **59887** (worn female) - the same five
attachments either way, so pick either. Tick Particles. This is the case a human reported as broken
and the one the clock fix was made against, so it is the regression case for that fix.

The cape carries emitter **157** on face 715, **158** on faces 716 and 718, and **159** on 717 and
719 - the last five faces of a 721-face mesh, which is the hem. Emitter 157 is the smoke: it spawns
near-black at alpha 150 to 200, half extent 32 to 35 model units, and a lifetime of 50 to 60 client
cycles, so about one to one and a quarter seconds a particle.

- **Correct.** A **thick black trail rooted at the hem**, continuous rather than in shells, thinning
  and fading as it falls away. Particles must appear touching the bottom of the cape, not below it.
  The readout reads `emitters 5/5`.
- **Wrong, the reported defect: a faint sparse smudge, detached, below and behind the cape.** The
  step is being read as a millisecond rather than as a 20 ms client cycle, so one 33 ms redraw runs
  33 steps and every particle is first drawn about 57% through its life - alpha 84 instead of near
  its birth 150 to 200, half extent 19 instead of 33, and 92 model units clear of the hem instead of
  16, drawn once instead of thirty to thirty-six times. Any partial correction of the unit gives the
  same shape less severely. Pinned numerically by
  `FlashEditor.Tests/Rendering/ParticleClockTests.cs`, so if this is what you see, that file is
  failing too and it is not a render defect.
- **Wrong, and the near miss to watch for: the trail is present, moves with the cape, and reads as
  discrete puffs** with visible gaps between clumps rather than as one continuous column. That is a
  step still several times too long: enough particles survive to see, and each is drawn only a
  handful of times over its life, so the trail is sampled rather than swept. This one reads as
  "working" at a glance and is the reason this entry asks for continuity rather than for presence.
- **Wrong, rooted off the mesh.** A trail of the right density and colour that starts a body-width
  away from the cape and hangs in space. The attachment is on the wrong face, or the emitter is being
  left at the rest position while the model poses - `ParticleSystem.ApplyPose` not being called.
- **Wrong, no material.** Hard-edged opaque squares rather than soft smoke. That was the expected
  result until the material draw landed; it is now a regression, and section **F4** below is the
  case that covers it.
- **Not a defect.** The trail keeps falling for about a second after the cape stops moving. That is
  the particles' own lifetime, and the client does the same.

### F4. The particle material - soft orbs rather than squares

Same model, same tick box. This section is about **one particle**, so zoom in until a single one
fills a good part of the frame rather than judging the trail as a whole.

Emitter 157 names index-26 material **812**, whose colour output is an opaque noise field and whose
*alpha* output is a radial falloff. The falloff is the whole of the effect: the quad is a flat
camera-facing rectangle and nothing else about it is round.

- **Correct.** Each particle is a **soft round orb whose edge fades out to nothing**, with no
  boundary you can point at. A cluster of them reads as one **haze** rather than as a number of
  shapes, which is the difference between this and the client capture. Measured at 64x64, material
  812 rasterises to 120 distinct alpha values, 255 at the centre and 0 at every corner, with 1018 of
  its 4096 pixels fully transparent, so a correct draw shows roughly a quarter of each quad as
  nothing at all.
- **Wrong, hard-edged opaque squares.** The material is not reaching the draw. Either the prewarm
  has not run - it is kicked off when the models are set, and until it finishes the quads fall back
  to flat white on purpose - or `ViewportOverlayRenderer.MaterialTextureResolver` is unwired and
  every quad is sampling the one white pixel. If they are square and *stay* square after several
  seconds, it is the second.
- **Wrong, a visible square boundary around a soft centre.** The falloff is being sampled but the
  quad extends past it. Either the UVs no longer span 0 to 1 across the sprite, or the wrap mode is
  clamping where material 812 asks for repeat, which smears the edge row outwards instead of ending
  the orb.
- **Wrong, orbs of the wrong size relative to the trail.** The texture is right and the quad is not.
  This is a size or clock defect, not a material one - go back to **F3**.
- **Wrong, the wrong sprite entirely.** A recognisable pattern - bark, rope, brick - instead of a
  soft blob. The material id is being resolved against the wrong table, or the batch is binding a
  neighbouring material's texture.
- **Wrong, every particle sharing one material when several are live.** Only visible on a model
  whose emitters name different materials, so **check it on one from the F2 table with three or more
  distinct emitters** rather than on the cape, whose three emitters make this hard to see. The
  batching is a run per consecutive span of one material, so a boundary in the wrong place draws one
  emitter's particles with another's texture.
- **Not a defect, and expected on the first frame or two.** Squares that turn into orbs shortly after
  the model loads. The texture graph is evaluated off the UI thread deliberately, because evaluating
  one inside the paint handler would freeze the window; the quads draw white until it lands.
- **Not a defect.** The orbs are *dark*, not grey. Emitter 157 spawns near-black, and the shader
  multiplies the texture by the particle's own colour and alpha - which is the client's MODULATE
  combine, `Class238.anInt1821 == 0`, and is what 913 of the vanilla capture's 915 materials ask for.

**What is already pinned numerically and needs no eye**:
`FlashEditor.Tests/Rendering/ParticleMaterialTests.cs` for the material's alpha profile, its
rasterisation mode and the combine census, and
`FlashEditor.Tests/Rendering/ParticleMaterialBatchTests.cs` for the batching. So this section is
asking a human only for what those cannot see - that the pixels reach the screen at all.

**The peak-live figures in the table above predate the clock fix.** They were measured by driving
the system at 33 ms advances under the reading of a step as a millisecond, so each advance ran 33
steps rather than one or two. Every peak in that table is therefore a figure for a simulation
running twenty times too fast, and several of the notes derived from them - 63586 and 59584 peaking
at zero because they "spawn and kill inside one 33 ms step", 62446 being a sputter - are properties
of the old clock rather than of build 639. **Re-measure before treating any of them as a target.**
The attachment counts in the Emitters column are read from the model's tail block and are unaffected.

### Finding one in the editor

There is currently **no way to know a model has an emitter except to select it and read the
readout**, which over 63,607 models is not a search. The Models grid deliberately decodes nothing -
`ModelListDescriptor.ReadsPayload` is false precisely so that visiting the tab does not inflate every
model in the cache - so an always-on Emitters column is the wrong shape. The right shape is an
opt-in scan: a button beside Export and Import on `EntityBrowserPanel`'s tool strip that runs one
background pass over index 7, keeps the resulting id set on the panel, and lights up a Particles
column in `ModelListDescriptor`. One pass, on demand, and the 211 become filterable. **Not built.**

## G. The render timer

The timer used to run at 30 Hz from the constructor until the form closed, on every page, with
nothing animating.

- Sit on the Items or Entities grid with no model loaded: **no repainting** of the model page, CPU
  idle.
- Models tab, a static model, no animation: still **no continuous repaint**, and a camera drag still
  redraws immediately.
- Play animation 12358: repaints resume. With Loop off, when it finishes they **stop**, and the
  readout freezes on the last step rather than a step early.
- **Wrong.** A continuous 30 Hz repaint on any page with nothing animating. That is the old
  behaviour returning.

## H. A multi-part entity holds together

Entities tab, NPC **1** ("Man") - eight model files. Play animation **811**, the one the defect was
reported on. This is the case where the jaw came off the face, the hands off the arms and the boots
sat inboard of the ankles.

An entity is not one model, and the client never poses its parts separately: `Class141.java:801`
merges them with `new Model(models, models.length)` whenever there is more than one, so the pivot a
rotation turns about is summed over the **whole body** (`Renderable_Sub2.java:2803-2827`). Posing
part by part gave each part its own centre, and a part carrying none of the pivot bone's labels fell
back to the model origin - the floor between the feet.

- **Correct.** The body reads as one object at every step. The jaw stays seated in the head through
  the whole cycle; both hands stay at the ends of their arms; each boot sits **under** its trouser
  cuff rather than inboard of it, and does so on every frame including the first. Nothing detaches at
  the neck, the wrists or the ankles.
- **Wrong, and the original defect.** Extremities separate and the gap **grows and shrinks across the
  cycle** rather than being constant. A varying excursion is the signature of a rotation about the
  wrong centre, which is what a per-part pivot produces.
- **Wrong, and the failure a single shared pivot would produce instead.** The parts move together and
  stay joined, but the **whole body** pivots oddly - leaning or swinging about a point that is not
  where a joint is, most visibly a torso that rotates about the feet or about a point outside the
  model. That is what taking one part's pivot and pushing it into all of them looks like, and it is
  the plausible wrong result that reads as "fixed" at a glance.
- **Wrong, welding.** A single vertex spike, a thin triangle stretched between two parts at the neck,
  a wrist or an ankle. A seam vertex that failed to weld is driven by two bones and drags a face
  between them.

**Two things here are not defects, so do not chase them.**

- **The legs and boots do not move.** Below about mid-thigh the render is identical on every frame,
  measured at 10,505 pixels in common with none differing between most pairs. Animation 811 appears
  to be an upper body animation. The waist and belt **do** tilt with the torso, so the boundary sits
  at mid-thigh and not at the belt. Frozen legs here are the animation, not the viewer. Check what
  the frames actually transform before treating still legs as a fault.
- **The transform count does not reach the denominator.** It reads 15 to 17 applied out of 19 to 21
  resolved depending on the frame, and never 0. The shortfall does not track how bad a frame looks -
  the cleanest render is one of the frames with the lower denominator - so an unreached transform
  here is a bone no loaded part carries rather than a join that is failing.

**What is measured and needs no eye.** Seam coherence is a number, and
`FlashEditor.Tests/Rendering/SeamCoherenceTests.cs` asserts it: over NPC 1 at index-0 frame
**15204474**, 46 rest coordinates are carried by two of the eight parts, and after posing the worst
of them is **0** model units out of place, in both caches. Before the parts were merged it was
**695**, and the 46 totalled **9677**; with the parts merged but the weld disabled it is **11**,
which is why the sweep's tolerance is 0 and not a margin. So this section is asking a human only for
the things a distance cannot see - whether the body reads as one object, and whether it pivots where
a joint is. A DWM-composited grab settles both without a second pair of eyes.

**One thing this section deliberately does not ask about.** Where a part does separate, the interior
renders near-black rather than as background, and both wrists show the same open tube. That is
backfaces being drawn rather than culled, it is a property of the render state and not of the pose,
and it is out of scope here.

## I. Layout

Drag the splitter narrow and resize the window. The tool strip **wraps** rather than clipping, every
caption stays readable, and the GL rectangle resizes with it without overlapping the strip.

---

## What is still unverified after all of this

This checklist covers what a person can judge in one pass. It does **not** establish that the
renderer matches the client - the viewer deliberately omits frame blending, scene lighting and
particle scene collision, and says so in the view. It is a defect check, not a conformance check.
