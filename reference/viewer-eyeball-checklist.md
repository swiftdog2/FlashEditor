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
| **F** particles, model 62810 | **PASS.** `particles 68/2047, emitters 2/2`, against a predicted peak near 73. Cap honoured, both emitters resolved |
| **D** hover overlay | **OPEN.** Amber and blue marks are present near the shape. Whether they read as `face N` and `vN`, and whether the numbers fall in 0-7 and 0-23, is not yet settled |
| **H** multi-part entity | **NOT YET RUN, and it is the one to run first.** A human reported the defect it covers - an NPC's jaw, hands and boots coming away under animation - and the merge that fixes it landed on 2026-08-09. The seam half of it is now measured and passes at 0 model units in both caches; what is unverified is whether the body reads as one object and pivots where a joint is |
| **B, C, E, G, I** | **NOT YET RUN** |

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
