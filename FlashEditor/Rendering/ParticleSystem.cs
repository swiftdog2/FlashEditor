using System.Collections.Generic;
using System;
using FlashEditor.Definitions.Particles;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Simulates the particle emitters and effectors attached to a set of models.
    /// </summary>
    /// <remarks>
    ///     A transcription of the client's particle path - <c>Particle_Sub9</c> for the emitters and
    ///     <c>Particle_Sub4_Sub2_Sub1.method3109</c> for the per-particle step - reduced to what a
    ///     model viewer can honour.
    ///     <para>
    ///     <b>What is deliberately not here.</b> Opcodes 12, 13 and 33 destroy a particle against the
    ///     scene's terrain and roof, and a model previewed on its own has no scene. So an effect that
    ///     relies on a floor to stop its particles looks wrong in the viewport and is right in the
    ///     client, and <see cref="SimulatesSceneBounds"/> exists to say so in the diagnostics panel
    ///     rather than leaving someone to find it. Emitter opcode 25's effector references are in the
    ///     same position: the client resolves them through a scene registry
    ///     (<c>Particle_Sub4_Sub2_Sub1.java:209-280</c>), so they are counted into
    ///     <see cref="SkippedAttachmentKeyReferences"/> and skipped.
    ///     </para>
    ///     <para>
    ///     Every count on this type is public for the same reason the animator's are. A truncated
    ///     effect, a missing definition and a working effect are indistinguishable on a viewport
    ///     nothing can capture; a steadily rising refusal count is how a human tells which they are
    ///     looking at.
    ///     </para>
    /// </remarks>
    public sealed class ParticleSystem
    {
        /// <summary>Default cap on live particles, which is the client's lowest detail ceiling.</summary>
        /// <remarks>See <see cref="ClientDetailCaps"/>.</remarks>
        public const int DefaultMaximumParticles = 2047;

        /// <summary>
        ///     Longest step <see cref="Advance"/> will simulate before giving up on the backlog.
        /// </summary>
        /// <remarks>
        ///     A UI thread that stalls - loading a model off the cache will do it - must not come back
        ///     and run ten seconds of emission inside one frame. At a realistic rate that is tens of
        ///     thousands of spawns the cap immediately throws away, and the frame it lands on is the
        ///     one the user sees stutter.
        /// </remarks>
        public const int MaximumStepMilliseconds = 750;

        /// <summary>Default detail level, which is the client's highest.</summary>
        /// <remarks>
        ///     An emitter declares the lowest detail level it should appear at
        ///     (<c>Particle_Sub9.java:148</c>). The editor defaults to showing everything, because
        ///     hiding a definition someone came here to inspect is worse than drawing a busy scene.
        /// </remarks>
        public const int DefaultDetailLevel = 2;

        /// <summary>Where emitter and effector definitions come from.</summary>
        private readonly IParticleDataSource source;

        /// <summary>Emitters attached to the loaded models, in attachment order.</summary>
        /// <remarks>
        ///     A particle refers back into this list by index, so entries are never removed while
        ///     particles are alive - the list is rebuilt wholesale by <see cref="SetModels"/>, which
        ///     also clears the particles.
        /// </remarks>
        private readonly List<ParticleEmitterInstance> emitters = new List<ParticleEmitterInstance>();

        /// <summary>Effectors attached to the loaded models, each anchored to a vertex.</summary>
        private readonly List<ParticleEffectorInstance> effectors = new List<ParticleEffectorInstance>();

        /// <summary>Effectors an emitter named through opcode 10, which act everywhere.</summary>
        /// <remarks>
        ///     Deduplicated across emitters, since several emitters commonly name the same wind.
        /// </remarks>
        private readonly List<ParticleEffectorRuntime> globalEffectors = new List<ParticleEffectorRuntime>();

        /// <summary>Derived effector values by id, so an effector named twice is derived once.</summary>
        private readonly Dictionary<int, ParticleEffectorRuntime> effectorRuntimes =
            new Dictionary<int, ParticleEffectorRuntime>();

        /// <summary>The shared random source. One stream, so a seeded run is reproducible as a whole.</summary>
        private readonly ParticleRandom random;

        /// <summary>
        ///     Live particles, packed into the front of the array.
        /// </summary>
        /// <remarks>
        ///     Allocated once at the cap and never resized. A dead particle is replaced by the last
        ///     live one rather than the array being compacted, which makes removal constant-time at
        ///     the cost of the order changing - and nothing depends on the order, because each
        ///     particle carries its own emitter slot.
        /// </remarks>
        private readonly Particle[] particles;

        /// <summary>The models the attachments were read from.</summary>
        private ModelDefinition[] models = Array.Empty<ModelDefinition>();

        /// <summary>Current vertex x per model - the pose's array, or the rest model's.</summary>
        /// <remarks>
        ///     Aliased rather than copied. <see cref="ApplyPose"/> points these at the posed mesh's own
        ///     buffers, so an attachment follows the animation without anything being copied per frame.
        /// </remarks>
        private int[][] vertexX = Array.Empty<int[]>();

        /// <summary>Current vertex y per model.</summary>
        private int[][] vertexY = Array.Empty<int[]>();

        /// <summary>Current vertex z per model.</summary>
        private int[][] vertexZ = Array.Empty<int[]>();

        /// <summary>Elapsed time not yet worth a whole millisecond, carried into the next advance.</summary>
        /// <remarks>
        ///     A 30 fps redraw is 33.33 ms. Dropping the third of a millisecond each time would run
        ///     every effect one percent slow, which is invisible and wrong.
        /// </remarks>
        private double carriedMilliseconds;

        /// <summary>
        ///     The client's three particle caps, one per detail level.
        /// </summary>
        /// <remarks>
        ///     Here so a panel can offer them rather than an arbitrary number, and so the default is
        ///     visibly one of the client's rather than something invented.
        /// </remarks>
        public static IReadOnlyList<int> ClientDetailCaps { get; } = new int[3] { 2047, 16383, 65535 };

        /// <summary>The cap this system was built with.</summary>
        public int MaximumParticles { get; }

        /// <summary>Detail level. Emitters declaring a higher minimum are skipped.</summary>
        public int DetailLevel { get; set; } = DefaultDetailLevel;

        /// <summary>How many particles are alive.</summary>
        public int LiveParticleCount { get; private set; }

        /// <summary>How many emitters are attached, whether or not they are spawning.</summary>
        public int EmitterCount => emitters.Count;

        /// <summary>How many emitters actually produced a particle on the last advance.</summary>
        /// <remarks>
        ///     Below <see cref="EmitterCount"/> whenever an emitter is outside its duty cycle, on a
        ///     collapsed face, or above the detail level - so the pair is what says whether an idle
        ///     effect is idle by design.
        /// </remarks>
        public int ActiveEmitterCount { get; private set; }

        /// <summary>How many effectors are attached to vertices.</summary>
        public int EffectorCount => effectors.Count;

        /// <summary>How many emitter attachments named a definition the source does not hold.</summary>
        public int MissingEmitterCount { get; private set; }

        /// <summary>How many effector references named a definition the source does not hold.</summary>
        public int MissingEffectorCount { get; private set; }

        /// <summary>How many attachments named a face or vertex the model does not have.</summary>
        public int OutOfRangeAttachmentCount { get; private set; }

        /// <summary>How many opcode-25 effector references were skipped.</summary>
        /// <remarks>See the note on scene simulation in the type's own remarks.</remarks>
        public int SkippedAttachmentKeyReferences { get; private set; }

        /// <summary>How many spawns were refused because the cap was reached.</summary>
        public long SpawnsRefusedByCap { get; private set; }

        /// <summary>How many particles have been spawned since the last reset.</summary>
        public long TotalSpawned { get; private set; }

        /// <summary>Simulated time since the last reset.</summary>
        /// <remarks>The emitters' duty cycles are measured against this, not against wall time.</remarks>
        public long ElapsedMilliseconds { get; private set; }

        /// <summary>Simulated time thrown away to <see cref="MaximumStepMilliseconds"/>.</summary>
        public long DroppedMilliseconds { get; private set; }

        /// <summary>The last definition that failed to derive, or null.</summary>
        public string? LastError { get; private set; }

        /// <summary>
        ///     Whether particles are destroyed against the scene. They are not.
        /// </summary>
        /// <remarks>
        ///     A constant, and reported rather than omitted, because the difference it makes is visible
        ///     and would otherwise look like a defect in the simulation. See the type's own remarks.
        /// </remarks>
        public bool SimulatesSceneBounds => false;

        /// <summary>One line describing what the system is doing, for the status bar.</summary>
        public string Status
        {
            get
            {
                if (LastError != null)
                {
                    return "error: " + LastError;
                }

                if (emitters.Count == 0)
                {
                    return "no emitters attached";
                }

                //Attached but producing nothing. Distinct from the line below, which would read
                //"0/2047 particles, 0/1 emitters spawning" and look like a broken simulation rather
                //than an emitter waiting for its duty cycle.
                if (LiveParticleCount == 0 && ActiveEmitterCount == 0)
                {
                    return emitters.Count + " emitter(s), none spawning";
                }

                return LiveParticleCount + "/" + MaximumParticles + " particles, "
                    + ActiveEmitterCount + "/" + emitters.Count + " emitters spawning";
            }
        }

        /// <summary>Name and value rows for the diagnostics panel.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Diagnostics => new KeyValuePair<string, string>[11]
        {
            new KeyValuePair<string, string>("Live particles", LiveParticleCount + " / " + MaximumParticles),
            new KeyValuePair<string, string>("Emitters spawning", ActiveEmitterCount + " / " + emitters.Count),
            new KeyValuePair<string, string>("Effectors", effectors.Count + " attached, " + globalEffectors.Count + " global"),
            new KeyValuePair<string, string>("Spawned", TotalSpawned.ToString()),
            new KeyValuePair<string, string>("Refused by cap", SpawnsRefusedByCap.ToString()),
            new KeyValuePair<string, string>("Elapsed", ElapsedMilliseconds + " ms"),
            new KeyValuePair<string, string>("Dropped", DroppedMilliseconds + " ms"),
            new KeyValuePair<string, string>("Missing definitions", MissingEmitterCount + " emitter(s), " + MissingEffectorCount + " effector(s)"),
            new KeyValuePair<string, string>("Out of range attachments", OutOfRangeAttachmentCount.ToString()),
            new KeyValuePair<string, string>("Opcode 25 refs skipped", SkippedAttachmentKeyReferences.ToString()),
            new KeyValuePair<string, string>("Scene bounds simulated", SimulatesSceneBounds ? "yes" : "no")
        };

        /// <summary>The attached emitters, for a panel that lists them.</summary>
        public IReadOnlyList<ParticleEmitterInstance> Emitters => emitters;

        /// <summary>The attached effectors, for a panel that lists them.</summary>
        public IReadOnlyList<ParticleEffectorInstance> Effectors => effectors;

        /// <summary>Creates an empty system.</summary>
        /// <param name="source">Where to read emitter and effector definitions from.</param>
        /// <param name="maximumParticles">
        ///     The cap. Not optional: an uncapped system stalls the viewport, because a spawn rate is
        ///     per millisecond and nothing in the format bounds it.
        /// </param>
        /// <param name="seed">
        ///     Random seed. Fixed by default so a preview replays identically, which is what lets a
        ///     visual comparison mean anything.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The cap is below one.</exception>
        public ParticleSystem(IParticleDataSource source, int maximumParticles = DefaultMaximumParticles,
            int seed = 24301)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));

            if (maximumParticles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumParticles),
                    "A particle system with no room for a particle cannot run.");
            }

            MaximumParticles = maximumParticles;
            random = new ParticleRandom(seed);
            particles = new Particle[maximumParticles];
        }

        /// <summary>Replaces the models and re-reads every attachment from them.</summary>
        /// <remarks>
        ///     Everything is rebuilt, including the counters, because a count of missing definitions
        ///     describes the models that are loaded rather than the session. Attachments start at the
        ///     rest pose, so an emitter is in the right place before the first frame is drawn.
        ///     <para>
        ///     Effectors are attached before emitters, because an emitter's opcode-10 list is resolved
        ///     against the effector runtimes and reusing an already-derived one is the point of the
        ///     cache.
        ///     </para>
        /// </remarks>
        /// <param name="definitions">The models, or null to clear.</param>
        public void SetModels(IReadOnlyList<ModelDefinition>? definitions)
        {
            Reset();
            emitters.Clear();
            effectors.Clear();
            globalEffectors.Clear();
            effectorRuntimes.Clear();
            MissingEmitterCount = 0;
            MissingEffectorCount = 0;
            OutOfRangeAttachmentCount = 0;
            SkippedAttachmentKeyReferences = 0;
            LastError = null;

            if (definitions == null || definitions.Count == 0)
            {
                models = Array.Empty<ModelDefinition>();
                vertexX = Array.Empty<int[]>();
                vertexY = Array.Empty<int[]>();
                vertexZ = Array.Empty<int[]>();
                return;
            }

            models = new ModelDefinition[definitions.Count];
            vertexX = new int[definitions.Count][];
            vertexY = new int[definitions.Count][];
            vertexZ = new int[definitions.Count][];

            for (int modelIndex = 0; modelIndex < definitions.Count; modelIndex++)
            {
                ModelDefinition model = definitions[modelIndex];
                models[modelIndex] = model;
                vertexX[modelIndex] = model.VertX;
                vertexY[modelIndex] = model.VertY;
                vertexZ[modelIndex] = model.VertZ;

                AttachEffectors(model, modelIndex);
                AttachEmitters(model, modelIndex);
            }

            ResolveGlobalEffectors();
            RefreshAttachmentPositions();
        }

        /// <summary>Points the attachments at a pose, or back at the rest models.</summary>
        /// <remarks>
        ///     The join between the animation work and this one. An emitter left at the rest position
        ///     sprays particles out of thin air while the model waves somewhere else, and the client
        ///     rewrites both kinds of attachment every time it transforms the model.
        /// </remarks>
        /// <param name="poses">One pose per model, in the same order. Null returns to the rest models.</param>
        public void ApplyPose(IReadOnlyList<PosedMesh>? poses)
        {
            for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                if (poses != null && modelIndex < poses.Count)
                {
                    PosedMesh pose = poses[modelIndex];
                    vertexX[modelIndex] = pose.VertexX;
                    vertexY[modelIndex] = pose.VertexY;
                    vertexZ[modelIndex] = pose.VertexZ;
                }
                else
                {
                    vertexX[modelIndex] = models[modelIndex].VertX;
                    vertexY[modelIndex] = models[modelIndex].VertY;
                    vertexZ[modelIndex] = models[modelIndex].VertZ;
                }
            }

            RefreshAttachmentPositions();
        }

        /// <summary>Kills every particle and puts the clock back to zero.</summary>
        /// <remarks>
        ///     Leaves the attachments alone - this restarts the simulation, it does not unload the
        ///     models. Emitters keep their priming state, so a reset does not re-prime.
        /// </remarks>
        public void Reset()
        {
            LiveParticleCount = 0;
            ActiveEmitterCount = 0;
            SpawnsRefusedByCap = 0L;
            TotalSpawned = 0L;
            ElapsedMilliseconds = 0L;
            DroppedMilliseconds = 0L;
            carriedMilliseconds = 0.0;
        }

        /// <summary>Runs however many whole milliseconds the elapsed wall-clock time is worth.</summary>
        /// <remarks>
        ///     One step of <paramref name="seconds"/> worth, not a loop of one-millisecond steps. The
        ///     client does the same - every rate in the simulation is multiplied by the step count
        ///     (<c>Particle_Sub4_Sub2_Sub1.method3109</c> takes it as an argument and scales by it) -
        ///     which makes a long step cheap but also means a particle can pass through an effector's
        ///     radius within one step without being deflected. That is the client's behaviour and the
        ///     reason the cap above is in milliseconds rather than in steps.
        /// </remarks>
        /// <param name="seconds">Wall-clock time since the last call.</param>
        /// <returns><c>true</c> when the simulation moved.</returns>
        public bool Advance(double seconds)
        {
            //Nothing to do, and nothing that could start: with no emitters and no live particles the
            //clock may as well not run.
            if (seconds <= 0.0 || (emitters.Count == 0 && LiveParticleCount == 0))
            {
                return false;
            }

            carriedMilliseconds += seconds * 1000.0;

            int steps = (int)carriedMilliseconds;

            if (steps <= 0)
            {
                return false;
            }

            carriedMilliseconds -= steps;

            if (steps > MaximumStepMilliseconds)
            {
                DroppedMilliseconds += steps - MaximumStepMilliseconds;
                steps = MaximumStepMilliseconds;
            }

            ElapsedMilliseconds += steps;

            //Emitters first, so a particle spawned by this step is also stepped by it - which is what
            //the client does, since its per-particle sweep runs over the whole live list including
            //whatever the emitters have just added.
            RunEmitters(steps);
            UpdateParticles(steps);

            return true;
        }

        /// <summary>Copies the live particles into a caller's buffer.</summary>
        /// <param name="destination">The buffer, at least <see cref="LiveParticleCount"/> long.</param>
        /// <returns>How many were copied.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="ArgumentException">The buffer is too small.</exception>
        public int CopyLiveParticles(Particle[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < LiveParticleCount)
            {
                throw new ArgumentException(
                    "The buffer holds " + destination.Length + " particles and " + LiveParticleCount + " are alive.",
                    nameof(destination));
            }

            Array.Copy(particles, destination, LiveParticleCount);
            return LiveParticleCount;
        }

        /// <summary>One live particle, by position in the live set.</summary>
        /// <remarks>
        ///     Returns a copy, which is what keeps the mutable struct from escaping. The position is
        ///     not stable across an advance - a death swaps the last particle into the gap.
        /// </remarks>
        /// <param name="index">Position below <see cref="LiveParticleCount"/>.</param>
        /// <returns>The particle.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The index is not a live particle.</exception>
        public Particle ParticleAt(int index)
        {
            if ((uint)index >= (uint)LiveParticleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return particles[index];
        }

        /// <summary>Attaches one model's emitters to their faces.</summary>
        /// <remarks>
        ///     Three ways an attachment fails, each counted separately because they point at different
        ///     problems: a face index the model does not have is a mismatched model and attachment
        ///     list, a missing definition is an incomplete index 27, and a definition that will not
        ///     derive is a decoder problem worth naming in <see cref="LastError"/>.
        /// </remarks>
        /// <param name="model">The model.</param>
        /// <param name="modelIndex">Its position in the set.</param>
        private void AttachEmitters(ModelDefinition model, int modelIndex)
        {
            if (model.Emitters == null)
            {
                return;
            }

            foreach (ModelParticleEmitter attachment in model.Emitters)
            {
                if ((uint)attachment.FaceIndex >= (uint)model.faceIndices1.Length)
                {
                    OutOfRangeAttachmentCount++;
                    continue;
                }

                ParticleEmitterDefinition? definition = source.GetEmitter(attachment.EmitterId);

                if (definition == null)
                {
                    MissingEmitterCount++;
                    continue;
                }

                try
                {
                    ParticleEmitterRuntime runtime = new ParticleEmitterRuntime(definition);
                    emitters.Add(new ParticleEmitterInstance(runtime, attachment.EmitterId, modelIndex,
                        attachment.FaceIndex, random));
                }
                catch (Exception failure)
                {
                    MissingEmitterCount++;
                    LastError = "emitter " + attachment.EmitterId + ": " + failure.Message;
                    continue;
                }

                //Opcode 25's references need a scene registry to resolve against. Counted so the
                //panel can say how much of the effect is not being shown.
                int[]? attachedEffectorKeys = definition.AttachedEffectorKeys;

                if (attachedEffectorKeys != null && attachedEffectorKeys.Length > 0)
                {
                    SkippedAttachmentKeyReferences += attachedEffectorKeys.Length;
                }
            }
        }

        /// <summary>Attaches one model's effectors to their vertices.</summary>
        /// <param name="model">The model.</param>
        /// <param name="modelIndex">Its position in the set.</param>
        private void AttachEffectors(ModelDefinition model, int modelIndex)
        {
            if (model.Effectors == null)
            {
                return;
            }

            foreach (ModelParticleEffector attachment in model.Effectors)
            {
                if ((uint)attachment.VertexIndex >= (uint)model.VertX.Length)
                {
                    OutOfRangeAttachmentCount++;
                    continue;
                }

                ParticleEffectorRuntime? runtime = ResolveEffector(attachment.EffectorId);

                if (runtime == null)
                {
                    MissingEffectorCount++;
                }
                else
                {
                    effectors.Add(new ParticleEffectorInstance(runtime, attachment.EffectorId, modelIndex,
                        attachment.VertexIndex));
                }
            }
        }

        /// <summary>Resolves the effectors every attached emitter names through opcode 10.</summary>
        /// <remarks>
        ///     Deduplicated, so an effector named by twenty emitters is applied once per particle
        ///     rather than twenty times - which would be twenty times the force, not a redundancy.
        /// </remarks>
        private void ResolveGlobalEffectors()
        {
            HashSet<int> seen = new HashSet<int>();

            foreach (ParticleEmitterInstance emitter in emitters)
            {
                int[]? globalEffectorIds = emitter.Runtime.Definition.GlobalEffectorIds;

                if (globalEffectorIds == null)
                {
                    continue;
                }

                foreach (int effectorId in globalEffectorIds)
                {
                    if (!seen.Add(effectorId))
                    {
                        continue;
                    }

                    ParticleEffectorRuntime? runtime = ResolveEffector(effectorId);

                    if (runtime == null)
                    {
                        MissingEffectorCount++;
                    }
                    else
                    {
                        globalEffectors.Add(runtime);
                    }
                }
            }
        }

        /// <summary>Derives an effector once and remembers it.</summary>
        /// <param name="effectorId">The effector id.</param>
        /// <returns>The runtime, or null when the source does not hold it or it will not derive.</returns>
        private ParticleEffectorRuntime? ResolveEffector(int effectorId)
        {
            if (effectorRuntimes.TryGetValue(effectorId, out ParticleEffectorRuntime? cached))
            {
                return cached;
            }

            ParticleEffectorDefinition? definition = source.GetEffector(effectorId);

            if (definition == null)
            {
                return null;
            }

            try
            {
                ParticleEffectorRuntime runtime = new ParticleEffectorRuntime(definition);
                effectorRuntimes[effectorId] = runtime;
                return runtime;
            }
            catch (Exception failure)
            {
                LastError = "effector " + effectorId + ": " + failure.Message;
                return null;
            }
        }

        /// <summary>Re-reads every attachment's position out of the current vertex arrays.</summary>
        /// <remarks>
        ///     An emitter needs its face's three corners because a particle spawns at a random point
        ///     inside the triangle and leaves along its normal; an effector needs one vertex because
        ///     it is a point source. That asymmetry is the whole reason the two attachment kinds index
        ///     different arrays - see <see cref="ParticleEmitterInstance"/> and
        ///     <see cref="ParticleEffectorInstance"/>.
        ///     <para>
        ///     The bounds check is here rather than at attach time because a pose may be shorter than
        ///     the rest model. An attachment that falls out of range is left where it was, which keeps
        ///     it near the model rather than snapping it to the origin.
        ///     </para>
        /// </remarks>
        private void RefreshAttachmentPositions()
        {
            foreach (ParticleEmitterInstance emitter in emitters)
            {
                ModelDefinition model = models[emitter.ModelIndex];
                int face = emitter.FaceIndex;

                int cornerA = model.faceIndices1[face];
                int cornerB = model.faceIndices2[face];
                int cornerC = model.faceIndices3[face];

                int[] x = vertexX[emitter.ModelIndex];
                int[] y = vertexY[emitter.ModelIndex];
                int[] z = vertexZ[emitter.ModelIndex];

                if ((uint)cornerA < (uint)x.Length && (uint)cornerB < (uint)x.Length && (uint)cornerC < (uint)x.Length)
                {
                    emitter.SetFace(
                        x[cornerA], y[cornerA], z[cornerA],
                        x[cornerB], y[cornerB], z[cornerB],
                        x[cornerC], y[cornerC], z[cornerC]);
                }
            }

            foreach (ParticleEffectorInstance effector in effectors)
            {
                int[] x = vertexX[effector.ModelIndex];
                int[] y = vertexY[effector.ModelIndex];
                int[] z = vertexZ[effector.ModelIndex];
                int vertex = effector.VertexIndex;

                if ((uint)vertex < (uint)x.Length)
                {
                    effector.SetPosition(x[vertex], y[vertex], z[vertex]);
                }
            }
        }

        /// <summary>Runs every eligible emitter for one step.</summary>
        /// <remarks>
        ///     The priming steps are taken as separate one-millisecond emissions rather than as one
        ///     long step, because the spawn rate is drawn afresh per call - a single long step would
        ///     use one random rate for the whole prime and produce a different count.
        /// </remarks>
        /// <param name="steps">Milliseconds elapsed.</param>
        private void RunEmitters(int steps)
        {
            ActiveEmitterCount = 0;

            for (int slot = 0; slot < emitters.Count; slot++)
            {
                ParticleEmitterInstance emitter = emitters[slot];

                if (emitter.Runtime.Definition.MinimumDetailLevel > DetailLevel
                    || !emitter.IsOn(ElapsedMilliseconds))
                {
                    continue;
                }

                int primingSteps = emitter.TakePrimingSteps();
                int spawned = 0;

                for (int step = 0; step < primingSteps; step++)
                {
                    spawned += SpawnFrom(emitter, slot, emitter.Emit(1));
                }

                spawned += SpawnFrom(emitter, slot, emitter.Emit(steps));

                //Counted on having produced something, not on being eligible - an emitter whose rate
                //has not accumulated a whole particle yet is not what "spawning" should mean in the
                //status line.
                if (spawned > 0)
                {
                    ActiveEmitterCount++;
                }
            }
        }

        /// <summary>Adds up to a given number of particles from one emitter.</summary>
        /// <remarks>
        ///     The whole remainder is counted as refused the moment the cap is reached, rather than
        ///     one per iteration, so the count reflects what the emitter asked for.
        /// </remarks>
        /// <param name="emitter">The emitter.</param>
        /// <param name="slot">Its position in <see cref="emitters"/>, stamped onto each particle.</param>
        /// <param name="count">How many to spawn.</param>
        /// <returns>How many were actually spawned.</returns>
        private int SpawnFrom(ParticleEmitterInstance emitter, int slot, int count)
        {
            int spawned = 0;

            for (int i = 0; i < count; i++)
            {
                if (LiveParticleCount >= MaximumParticles)
                {
                    SpawnsRefusedByCap += count - i;
                    break;
                }

                Particle particle = emitter.Spawn();
                particle.EmitterSlot = slot;
                particles[LiveParticleCount++] = particle;
                TotalSpawned++;
                spawned++;
            }

            return spawned;
        }

        /// <summary>Ages every live particle and steps the survivors.</summary>
        /// <remarks>
        ///     A dead particle is replaced by the last live one and the index is <b>not</b> advanced,
        ///     so the particle just moved into the gap is examined on the next pass rather than
        ///     skipped. Getting that wrong lets one particle in two survive a step it should not have.
        /// </remarks>
        /// <param name="steps">Milliseconds elapsed.</param>
        private void UpdateParticles(int steps)
        {
            int index = 0;

            while (index < LiveParticleCount)
            {
                ref Particle particle = ref particles[index];
                particle.Life -= steps;

                if (particle.Life <= 0)
                {
                    particles[index] = particles[--LiveParticleCount];
                    continue;
                }

                StepParticle(ref particle, steps);
                index++;
            }
        }

        /// <summary>Runs one particle forward.</summary>
        /// <remarks>
        ///     <c>Particle_Sub4_Sub2_Sub1.method3109</c>, <c>:35-327</c>, in its order: the ramps, then
        ///     drag, then the effectors, then the position integration. The order matters because each
        ///     stage reads what the previous one wrote.
        /// </remarks>
        /// <param name="particle">The particle, by reference so the struct is not copied.</param>
        /// <param name="steps">Milliseconds elapsed.</param>
        private void StepParticle(ref Particle particle, int steps)
        {
            //A particle whose emitter has gone. Cannot happen while SetModels clears the particles,
            //but the slot is data on the struct and the array outlives any one model load.
            if ((uint)particle.EmitterSlot >= (uint)emitters.Count)
            {
                return;
            }

            ParticleEmitterInstance emitter = emitters[particle.EmitterSlot];
            ParticleEmitterRuntime runtime = emitter.Runtime;

            //How far through its life it is. Each ramp runs from birth for its own share of the
            //maximum lifetime, so they are compared against elapsed life rather than remaining.
            int age = particle.MaxLife - particle.Life;

            if (runtime.HasColourRamp)
            {
                if (age <= runtime.ColourRampSteps)
                {
                    FadeColour(ref particle, runtime, steps);
                }

                //Alpha has its own duration, from a different opcode, so it is tested separately
                //rather than inside the RGB branch.
                if (age <= runtime.AlphaRampSteps)
                {
                    FadeAlpha(ref particle, runtime, steps);
                }
            }

            if (runtime.HasSpeedRamp && age <= runtime.SpeedRampSteps)
            {
                particle.Speed += runtime.SpeedRate * steps;
            }

            if (runtime.HasSizeRamp && age <= runtime.SizeRampSteps)
            {
                particle.Size += runtime.SizeRate * steps;
            }

            ApplyDrag(ref particle, emitter, steps);

            //Accumulated in double precision and written back once. The effectors add forces that are
            //individually far below one unit of a short, so applying each straight to the stored
            //direction would round every one of them to nothing.
            double directionX = particle.DirectionX;
            double directionY = particle.DirectionY;
            double directionZ = particle.DirectionZ;
            bool directionChanged = false;

            ApplyEffectors(ref particle, runtime, steps, ref directionX, ref directionY, ref directionZ,
                ref directionChanged);

            if (directionChanged)
            {
                //Halve the direction and double the speed until the direction fits a short. The
                //product of the two is what the integration below uses, so this preserves the velocity
                //while keeping the stored direction in range - which is why the speed is not simply
                //clamped.
                while (directionX > 32767.0 || directionY > 32767.0 || directionZ > 32767.0
                    || directionX < -32767.0 || directionY < -32767.0 || directionZ < -32767.0)
                {
                    directionX /= 2.0;
                    directionY /= 2.0;
                    directionZ /= 2.0;
                    particle.Speed <<= 1;
                }

                particle.DirectionX = (short)(int)directionX;
                particle.DirectionY = (short)(int)directionY;
                particle.DirectionZ = (short)(int)directionZ;
            }

            //Widened to a long before the multiply: direction is up to 32767 and speed is shifted up
            //two, so the product overflows an int well before the shift brings it back down.
            long velocity = particle.Speed << ParticleUnits.SpeedShift;

            particle.X += (int)((particle.DirectionX * velocity >> ParticleUnits.VelocityShift) * steps);
            particle.Y += (int)((particle.DirectionY * velocity >> ParticleUnits.VelocityShift) * steps);
            particle.Z += (int)((particle.DirectionZ * velocity >> ParticleUnits.VelocityShift) * steps);
        }

        /// <summary>Moves a particle's RGB one step along its fade.</summary>
        /// <remarks>
        ///     <c>Particle_Sub4_Sub2_Sub1.java:47-79</c>. Each channel is reassembled from its whole
        ///     part in <see cref="Particle.Colour"/> and its fractional part in
        ///     <see cref="Particle.ColourFraction"/> into one 16-bit value, moved by the rate, clamped,
        ///     and split back. The shifts look arbitrary and are not: they are aligning an 8.8
        ///     fixed-point channel that is stored across two different words.
        /// </remarks>
        /// <param name="particle">The particle.</param>
        /// <param name="runtime">Its emitter's derived values.</param>
        /// <param name="steps">Milliseconds elapsed.</param>
        private static void FadeColour(ref Particle particle, ParticleEmitterRuntime runtime, int steps)
        {
            int red = Clamp16(((particle.Colour >> 8) & 0xFF00) + ((particle.ColourFraction >> 16) & 0xFF)
                + runtime.RedRate * steps);
            int green = Clamp16((particle.Colour & 0xFF00) + ((particle.ColourFraction >> 8) & 0xFF)
                + runtime.GreenRate * steps);
            int blue = Clamp16(((particle.Colour << 8) & 0xFF00) + (particle.ColourFraction & 0xFF)
                + runtime.BlueRate * steps);

            //Alpha is preserved in both words, which is what lets FadeAlpha run independently.
            particle.Colour = (particle.Colour & -16777216)
                | (((red & 0xFF00) << 8) + (green & 0xFF00) + ((blue & 0xFF00) >> 8));
            particle.ColourFraction = (particle.ColourFraction & -16777216)
                | (((red & 0xFF) << 16) + ((green & 0xFF) << 8) + (blue & 0xFF));
        }

        /// <summary>Moves a particle's alpha one step along its fade.</summary>
        /// <remarks>
        ///     <c>Particle_Sub4_Sub2_Sub1.java:81-95</c>. The same 8.8 split as
        ///     <see cref="FadeColour"/>, on the channel the other one leaves alone.
        /// </remarks>
        /// <param name="particle">The particle.</param>
        /// <param name="runtime">Its emitter's derived values.</param>
        /// <param name="steps">Milliseconds elapsed.</param>
        private static void FadeAlpha(ref Particle particle, ParticleEmitterRuntime runtime, int steps)
        {
            int alpha = Clamp16(((particle.Colour >> 16) & 0xFF00) + ((particle.ColourFraction >>> 24) & 0xFF)
                + runtime.AlphaRate * steps);

            particle.Colour = (particle.Colour & 0xFFFFFF) | ((alpha & 0xFF00) << 16);
            particle.ColourFraction = (particle.ColourFraction & 0xFFFFFF) | ((alpha & 0xFF) << 24);
        }

        /// <summary>Slows a particle according to its distance from the emitter.</summary>
        /// <remarks>
        ///     <c>Particle_Sub4_Sub2_Sub1.java:111-127</c>. Mode 1 is linear in distance and mode 2 is
        ///     quadratic, and the two use different shifts because the quantity being divided differs
        ///     by a square. Mode 1 also shifts the distance down two before using it, which mode 2 does
        ///     not - so the two are not the same law with a different exponent and cannot be folded
        ///     together.
        ///     <para>
        ///     Distance is measured from the emitter's face centre, so a particle slows as it leaves
        ///     the thing that produced it rather than as it leaves the origin.
        ///     </para>
        /// </remarks>
        /// <param name="particle">The particle.</param>
        /// <param name="emitter">The emitter it came from.</param>
        /// <param name="steps">Milliseconds elapsed.</param>
        private static void ApplyDrag(ref Particle particle, ParticleEmitterInstance emitter, int steps)
        {
            ParticleEmitterDefinition definition = emitter.Runtime.Definition;

            if (definition.DragMode != 1 && definition.DragMode != 2)
            {
                return;
            }

            int offsetX = (particle.X >> ParticleUnits.PositionFractionBits) - emitter.CentroidX;
            int offsetY = (particle.Y >> ParticleUnits.PositionFractionBits) - emitter.CentroidY;
            int offsetZ = (particle.Z >> ParticleUnits.PositionFractionBits) - emitter.CentroidZ;

            if (definition.DragMode == 1)
            {
                int distance = (int)Math.Sqrt(
                    (double)offsetX * offsetX + (double)offsetY * offsetY + (double)offsetZ * offsetZ) >> 2;

                long drag = definition.DragStrength * distance * steps;
                particle.Speed -= (int)(particle.Speed * drag >> ParticleUnits.LinearDragShift);
            }
            else
            {
                int squaredDistance = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;

                long drag = definition.DragStrength * squaredDistance * steps;
                particle.Speed -= (int)(particle.Speed * drag >> ParticleUnits.QuadraticDragShift);
            }
        }

        /// <summary>Applies every effector that reaches this particle.</summary>
        /// <remarks>
        ///     Two lists, because the client resolves them differently.
        ///     <para>
        ///     <b>Opcode 9</b> names effectors to be found among those placed in the scene
        ///     (<c>Particle_Sub4_Sub2_Sub1.java:129-207</c>), matched by id and skipping any whose
        ///     mode is 1. Here the search is over the effectors attached to the loaded models, which
        ///     are the only placed ones a model viewer has. Those carry positions, so they get the
        ///     full distance and cone test.
        ///     </para>
        ///     <para>
        ///     <b>Opcode 10</b> names effectors in the global registry (<c>:283-308</c>), and the
        ///     client applies their force <i>directly</i> - no position, no distance, no cone. That is
        ///     not a simplification made here; the client's loop has no positional test in it, which
        ///     is what makes opcode 10 the way an emitter says "wind", as against opcode 9's "this
        ///     particular thing over there".
        ///     </para>
        /// </remarks>
        /// <param name="particle">The particle.</param>
        /// <param name="runtime">Its emitter's derived values.</param>
        /// <param name="steps">Milliseconds elapsed.</param>
        /// <param name="directionX">Accumulating direction x.</param>
        /// <param name="directionY">Accumulating direction y.</param>
        /// <param name="directionZ">Accumulating direction z.</param>
        /// <param name="directionChanged">Set when anything wrote to the direction.</param>
        private void ApplyEffectors(ref Particle particle, ParticleEmitterRuntime runtime, int steps,
            ref double directionX, ref double directionY, ref double directionZ, ref bool directionChanged)
        {
            int[]? sceneEffectorIds = runtime.Definition.SceneEffectorIds;

            if (sceneEffectorIds != null && sceneEffectorIds.Length > 0)
            {
                foreach (ParticleEffectorInstance effector in effectors)
                {
                    if (effector.Runtime.Definition.Mode != 1
                        && Array.IndexOf(sceneEffectorIds, effector.EffectorId) >= 0)
                    {
                        ApplyPositionalEffector(ref particle, effector, steps,
                            ref directionX, ref directionY, ref directionZ, ref directionChanged);
                    }
                }
            }

            foreach (ParticleEffectorRuntime globalEffector in globalEffectors)
            {
                ParticleEffectorDefinition definition = globalEffector.Definition;

                if (!definition.MovesPositionRatherThanVelocity)
                {
                    directionX += (double)definition.DirectionX * steps;
                    directionY += (double)definition.DirectionY * steps;
                    directionZ += (double)definition.DirectionZ * steps;
                    directionChanged = true;
                }
                else
                {
                    particle.X += definition.DirectionX * steps;
                    particle.Y += definition.DirectionY * steps;
                    particle.Z += definition.DirectionZ * steps;
                }
            }
        }

        /// <summary>Applies one placed effector to a particle, if it is within reach and inside the cone.</summary>
        /// <remarks>
        ///     <c>Particle_Sub4_Sub2_Sub1.java:148-206</c>. Three gates in order, each cheaper than the
        ///     next: the squared distance against the radius bound, the bearing against the cone
        ///     cosine, then the falloff. Note the radius test is squared-against-squared for falloff
        ///     mode 1 and squared-against-linear for mode 2, which is the client's asymmetry - see
        ///     <see cref="ParticleEffectorRuntime.RadiusBound"/>.
        ///     <para>
        ///     A radial effector pushes along the line from itself to the particle; a directional one
        ///     pushes along its stored vector, reduced by the falloff. And either can move the
        ///     particle's position instead of its velocity, which is opcode 8 - a positional effector
        ///     displaces without changing where the particle is heading.
        ///     </para>
        /// </remarks>
        /// <param name="particle">The particle.</param>
        /// <param name="instance">The effector and where it is.</param>
        /// <param name="steps">Milliseconds elapsed.</param>
        /// <param name="directionX">Accumulating direction x.</param>
        /// <param name="directionY">Accumulating direction y.</param>
        /// <param name="directionZ">Accumulating direction z.</param>
        /// <param name="directionChanged">Set when anything wrote to the direction.</param>
        private static void ApplyPositionalEffector(ref Particle particle, ParticleEffectorInstance instance,
            int steps, ref double directionX, ref double directionY, ref double directionZ,
            ref bool directionChanged)
        {
            ParticleEffectorRuntime runtime = instance.Runtime;
            ParticleEffectorDefinition definition = runtime.Definition;

            double offsetX = (particle.X >> ParticleUnits.PositionFractionBits) - instance.X;
            double offsetY = (particle.Y >> ParticleUnits.PositionFractionBits) - instance.Y;
            double offsetZ = (particle.Z >> ParticleUnits.PositionFractionBits) - instance.Z;

            double squaredDistance = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;

            if (squaredDistance > runtime.RadiusBound)
            {
                return;
            }

            double distance = Math.Sqrt(squaredDistance);

            //A particle exactly on the effector has no bearing. One rather than a skip, so it still
            //gets the force rather than being the single point the effect has a hole at.
            if (distance == 0.0)
            {
                distance = 1.0;
            }

            //A zero-length force vector has no bearing to compare against either, and would divide by
            //zero below.
            if (runtime.Magnitude == 0)
            {
                return;
            }

            //Cosine of the angle between the effector's force vector and the line to the particle,
            //scaled to the trig table's fixed point so it can be compared with ConeCosine.
            double bearingCosine = (offsetX * definition.DirectionX + offsetY * definition.DirectionY
                + offsetZ * definition.DirectionZ) * 65535.0 / (runtime.Magnitude * distance);

            if (bearingCosine < runtime.ConeCosine)
            {
                return;
            }

            //Distance is in sixteenths here, which is where the /16 comes from. Mode 0 has no falloff
            //at all, so the force is applied at full strength anywhere inside the unbounded radius.
            double falloff = definition.FalloffMode switch
            {
                1 => distance / 16.0 * runtime.Divisor,
                2 => distance / 16.0 * (distance / 16.0) * runtime.Divisor,
                _ => 0.0,
            };

            double forceX;
            double forceY;
            double forceZ;

            if (!definition.IsRadial)
            {
                //The falloff is subtracted from each component rather than scaling them, which is the
                //client's and is not the same thing - a component smaller than the falloff reverses.
                forceX = definition.DirectionX - falloff;
                forceY = definition.DirectionY - falloff;
                forceZ = definition.DirectionZ - falloff;
            }
            else
            {
                //Along the line from the effector, at the force's own magnitude - which is negative
                //for an inverted effector, turning the push into a pull.
                forceX = offsetX / distance * runtime.Magnitude;
                forceY = offsetY / distance * runtime.Magnitude;
                forceZ = offsetZ / distance * runtime.Magnitude;
            }

            if (!definition.MovesPositionRatherThanVelocity)
            {
                directionX += forceX * steps;
                directionY += forceY * steps;
                directionZ += forceZ * steps;
                directionChanged = true;
            }
            else
            {
                particle.X += (int)(forceX * steps);
                particle.Y += (int)(forceY * steps);
                particle.Z += (int)(forceZ * steps);
            }
        }

        /// <summary>Clamps a channel to sixteen bits.</summary>
        /// <remarks>
        ///     Sixteen rather than eight, because the value being clamped is an 8.8 fixed-point channel
        ///     - whole part and fraction together. Clamping to 255 here would clamp the colour to
        ///     roughly 1/256th of its range.
        /// </remarks>
        /// <param name="value">The value.</param>
        /// <returns>The value, clamped to 0..65535.</returns>
        private static int Clamp16(int value)
        {
            return value >= 0 ? value > 65535 ? 65535 : value : 0;
        }
    }
}
