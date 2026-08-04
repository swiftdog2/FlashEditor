using System.Collections.Generic;
using System;
using FlashEditor.Definitions.Particles;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public sealed class ParticleSystem
    {
        public const int DefaultMaximumParticles = 2047;

        public const int MaximumStepMilliseconds = 750;

        public const int DefaultDetailLevel = 2;

        private readonly IParticleDataSource source;

        private readonly List<ParticleEmitterInstance> emitters = new List<ParticleEmitterInstance>();

        private readonly List<ParticleEffectorInstance> effectors = new List<ParticleEffectorInstance>();

        private readonly List<ParticleEffectorRuntime> globalEffectors = new List<ParticleEffectorRuntime>();

        private readonly Dictionary<int, ParticleEffectorRuntime> effectorRuntimes = new Dictionary<int, ParticleEffectorRuntime>();

        private readonly ParticleRandom random;

        private readonly Particle[] particles;

        private ModelDefinition[] models = Array.Empty<ModelDefinition>();

        private int[][] vertexX = Array.Empty<int[]>();

        private int[][] vertexY = Array.Empty<int[]>();

        private int[][] vertexZ = Array.Empty<int[]>();

        private double carriedMilliseconds;

        public static IReadOnlyList<int> ClientDetailCaps { get; } = new int[3] { 2047, 16383, 65535 };


        public int MaximumParticles { get; }

        public int DetailLevel { get; set; } = 2;


        public int LiveParticleCount { get; private set; }

        public int EmitterCount => emitters.Count;

        public int ActiveEmitterCount { get; private set; }

        public int EffectorCount => effectors.Count;

        public int MissingEmitterCount { get; private set; }

        public int MissingEffectorCount { get; private set; }

        public int OutOfRangeAttachmentCount { get; private set; }

        public int SkippedAttachmentKeyReferences { get; private set; }

        public long SpawnsRefusedByCap { get; private set; }

        public long TotalSpawned { get; private set; }

        public long ElapsedMilliseconds { get; private set; }

        public long DroppedMilliseconds { get; private set; }

        public string? LastError { get; private set; }

        public bool SimulatesSceneBounds => false;

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
                if (LiveParticleCount == 0 && ActiveEmitterCount == 0)
                {
                    return emitters.Count + " emitter(s), none spawning";
                }
                return LiveParticleCount + "/" + MaximumParticles + " particles, " + ActiveEmitterCount + "/" + emitters.Count + " emitters spawning";
            }
        }

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

        public IReadOnlyList<ParticleEmitterInstance> Emitters => emitters;

        public IReadOnlyList<ParticleEffectorInstance> Effectors => effectors;

        public ParticleSystem(IParticleDataSource source, int maximumParticles = 2047, int seed = 24301)
        {
            this.source = source ?? throw new ArgumentNullException("source");
            if (maximumParticles < 1)
            {
                throw new ArgumentOutOfRangeException("maximumParticles", "A particle system with no room for a particle cannot run.");
            }
            MaximumParticles = maximumParticles;
            random = new ParticleRandom(seed);
            particles = new Particle[maximumParticles];
        }

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
            for (int i = 0; i < definitions.Count; i++)
            {
                ModelDefinition modelDefinition = definitions[i];
                models[i] = modelDefinition;
                vertexX[i] = modelDefinition.VertX;
                vertexY[i] = modelDefinition.VertY;
                vertexZ[i] = modelDefinition.VertZ;
                AttachEffectors(modelDefinition, i);
                AttachEmitters(modelDefinition, i);
            }
            ResolveGlobalEffectors();
            RefreshAttachmentPositions();
        }

        public void ApplyPose(IReadOnlyList<PosedMesh>? poses)
        {
            for (int i = 0; i < models.Length; i++)
            {
                if (poses != null && i < poses.Count)
                {
                    PosedMesh posedMesh = poses[i];
                    vertexX[i] = posedMesh.VertexX;
                    vertexY[i] = posedMesh.VertexY;
                    vertexZ[i] = posedMesh.VertexZ;
                }
                else
                {
                    vertexX[i] = models[i].VertX;
                    vertexY[i] = models[i].VertY;
                    vertexZ[i] = models[i].VertZ;
                }
            }
            RefreshAttachmentPositions();
        }

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

        public bool Advance(double seconds)
        {
            if (seconds <= 0.0 || (emitters.Count == 0 && LiveParticleCount == 0))
            {
                return false;
            }
            carriedMilliseconds += seconds * 1000.0;
            int num = (int)carriedMilliseconds;
            if (num <= 0)
            {
                return false;
            }
            carriedMilliseconds -= num;
            if (num > 750)
            {
                DroppedMilliseconds += num - 750;
                num = 750;
            }
            ElapsedMilliseconds += num;
            RunEmitters(num);
            UpdateParticles(num);
            return true;
        }

        public int CopyLiveParticles(Particle[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }
            if (destination.Length < LiveParticleCount)
            {
                throw new ArgumentException("The buffer holds " + destination.Length + " particles and " + LiveParticleCount + " are alive.", "destination");
            }
            Array.Copy(particles, destination, LiveParticleCount);
            return LiveParticleCount;
        }

        public Particle ParticleAt(int index)
        {
            if ((uint)index >= (uint)LiveParticleCount)
            {
                throw new ArgumentOutOfRangeException("index");
            }
            return particles[index];
        }

        private void AttachEmitters(ModelDefinition model, int modelIndex)
        {
            if (model.Emitters == null)
            {
                return;
            }
            ModelParticleEmitter[] array = model.Emitters;
            for (int i = 0; i < array.Length; i++)
            {
                ModelParticleEmitter modelParticleEmitter = array[i];
                if ((uint)modelParticleEmitter.FaceIndex >= (uint)model.faceIndices1.Length)
                {
                    OutOfRangeAttachmentCount++;
                    continue;
                }
                ParticleEmitterDefinition? emitter = source.GetEmitter(modelParticleEmitter.EmitterId);
                if (emitter == null)
                {
                    MissingEmitterCount++;
                    continue;
                }
                try
                {
                    ParticleEmitterRuntime runtime = new ParticleEmitterRuntime(emitter);
                    emitters.Add(new ParticleEmitterInstance(runtime, modelParticleEmitter.EmitterId, modelIndex, modelParticleEmitter.FaceIndex, random));
                }
                catch (Exception ex)
                {
                    MissingEmitterCount++;
                    LastError = "emitter " + modelParticleEmitter.EmitterId + ": " + ex.Message;
                    continue;
                }
                int[]? attachedEffectorKeys = emitter.AttachedEffectorKeys;
                if (attachedEffectorKeys != null && attachedEffectorKeys.Length > 0)
                {
                    SkippedAttachmentKeyReferences += attachedEffectorKeys.Length;
                }
            }
        }

        private void AttachEffectors(ModelDefinition model, int modelIndex)
        {
            if (model.Effectors == null)
            {
                return;
            }
            ModelParticleEffector[] array = model.Effectors;
            for (int i = 0; i < array.Length; i++)
            {
                ModelParticleEffector modelParticleEffector = array[i];
                if ((uint)modelParticleEffector.VertexIndex >= (uint)model.VertX.Length)
                {
                    OutOfRangeAttachmentCount++;
                    continue;
                }
                ParticleEffectorRuntime? particleEffectorRuntime = ResolveEffector(modelParticleEffector.EffectorId);
                if (particleEffectorRuntime == null)
                {
                    MissingEffectorCount++;
                }
                else
                {
                    effectors.Add(new ParticleEffectorInstance(particleEffectorRuntime, modelParticleEffector.EffectorId, modelIndex, modelParticleEffector.VertexIndex));
                }
            }
        }

        private void ResolveGlobalEffectors()
        {
            HashSet<int> hashSet = new HashSet<int>();
            foreach (ParticleEmitterInstance emitter in emitters)
            {
                int[]? globalEffectorIds = emitter.Runtime.Definition.GlobalEffectorIds;
                if (globalEffectorIds == null)
                {
                    continue;
                }
                int[] array = globalEffectorIds;
                foreach (int num in array)
                {
                    if (hashSet.Add(num))
                    {
                        ParticleEffectorRuntime? particleEffectorRuntime = ResolveEffector(num);
                        if (particleEffectorRuntime == null)
                        {
                            MissingEffectorCount++;
                        }
                        else
                        {
                            globalEffectors.Add(particleEffectorRuntime);
                        }
                    }
                }
            }
        }

        private ParticleEffectorRuntime? ResolveEffector(int effectorId)
        {
            if (effectorRuntimes.TryGetValue(effectorId, out ParticleEffectorRuntime? value))
            {
                return value;
            }
            ParticleEffectorDefinition? effector = source.GetEffector(effectorId);
            if (effector == null)
            {
                return null;
            }
            try
            {
                ParticleEffectorRuntime particleEffectorRuntime = new ParticleEffectorRuntime(effector);
                effectorRuntimes[effectorId] = particleEffectorRuntime;
                return particleEffectorRuntime;
            }
            catch (Exception ex)
            {
                LastError = "effector " + effectorId + ": " + ex.Message;
                return null;
            }
        }

        private void RefreshAttachmentPositions()
        {
            foreach (ParticleEmitterInstance emitter in emitters)
            {
                ModelDefinition modelDefinition = models[emitter.ModelIndex];
                int faceIndex = emitter.FaceIndex;
                int num = modelDefinition.faceIndices1[faceIndex];
                int num2 = modelDefinition.faceIndices2[faceIndex];
                int num3 = modelDefinition.faceIndices3[faceIndex];
                int[] array = vertexX[emitter.ModelIndex];
                int[] array2 = vertexY[emitter.ModelIndex];
                int[] array3 = vertexZ[emitter.ModelIndex];
                if ((uint)num < (uint)array.Length && (uint)num2 < (uint)array.Length && (uint)num3 < (uint)array.Length)
                {
                    emitter.SetFace(array[num], array2[num], array3[num], array[num2], array2[num2], array3[num2], array[num3], array2[num3], array3[num3]);
                }
            }
            foreach (ParticleEffectorInstance effector in effectors)
            {
                int[] array4 = vertexX[effector.ModelIndex];
                int[] array5 = vertexY[effector.ModelIndex];
                int[] array6 = vertexZ[effector.ModelIndex];
                int vertexIndex = effector.VertexIndex;
                if ((uint)vertexIndex < (uint)array4.Length)
                {
                    effector.SetPosition(array4[vertexIndex], array5[vertexIndex], array6[vertexIndex]);
                }
            }
        }

        private void RunEmitters(int steps)
        {
            ActiveEmitterCount = 0;
            for (int i = 0; i < emitters.Count; i++)
            {
                ParticleEmitterInstance particleEmitterInstance = emitters[i];
                if (particleEmitterInstance.Runtime.Definition.MinimumDetailLevel <= DetailLevel && particleEmitterInstance.IsOn(ElapsedMilliseconds))
                {
                    int num = particleEmitterInstance.TakePrimingSteps();
                    int num2 = 0;
                    for (int j = 0; j < num; j++)
                    {
                        num2 += SpawnFrom(particleEmitterInstance, i, particleEmitterInstance.Emit(1));
                    }
                    num2 += SpawnFrom(particleEmitterInstance, i, particleEmitterInstance.Emit(steps));
                    if (num2 > 0)
                    {
                        ActiveEmitterCount++;
                    }
                }
            }
        }

        private int SpawnFrom(ParticleEmitterInstance emitter, int slot, int count)
        {
            int num = 0;
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
                num++;
            }
            return num;
        }

        private void UpdateParticles(int steps)
        {
            int num = 0;
            while (num < LiveParticleCount)
            {
                ref Particle reference = ref particles[num];
                reference.Life -= steps;
                if (reference.Life <= 0)
                {
                    particles[num] = particles[--LiveParticleCount];
                    continue;
                }
                StepParticle(ref reference, steps);
                num++;
            }
        }

        private void StepParticle(ref Particle particle, int steps)
        {
            if ((uint)particle.EmitterSlot >= (uint)emitters.Count)
            {
                return;
            }
            ParticleEmitterInstance particleEmitterInstance = emitters[particle.EmitterSlot];
            ParticleEmitterRuntime runtime = particleEmitterInstance.Runtime;
            int num = particle.MaxLife - particle.Life;
            if (runtime.HasColourRamp)
            {
                if (num <= runtime.ColourRampSteps)
                {
                    FadeColour(ref particle, runtime, steps);
                }
                if (num <= runtime.AlphaRampSteps)
                {
                    FadeAlpha(ref particle, runtime, steps);
                }
            }
            if (runtime.HasSpeedRamp && num <= runtime.SpeedRampSteps)
            {
                particle.Speed += runtime.SpeedRate * steps;
            }
            if (runtime.HasSizeRamp && num <= runtime.SizeRampSteps)
            {
                particle.Size += runtime.SizeRate * steps;
            }
            ApplyDrag(ref particle, particleEmitterInstance, steps);
            double dirX = particle.DirectionX;
            double dirY = particle.DirectionY;
            double dirZ = particle.DirectionZ;
            bool directionChanged = false;
            ApplyEffectors(ref particle, runtime, steps, ref dirX, ref dirY, ref dirZ, ref directionChanged);
            if (directionChanged)
            {
                while (dirX > 32767.0 || dirY > 32767.0 || dirZ > 32767.0 || dirX < -32767.0 || dirY < -32767.0 || dirZ < -32767.0)
                {
                    dirX /= 2.0;
                    dirY /= 2.0;
                    dirZ /= 2.0;
                    particle.Speed <<= 1;
                }
                particle.DirectionX = (short)(int)dirX;
                particle.DirectionY = (short)(int)dirY;
                particle.DirectionZ = (short)(int)dirZ;
            }
            long num2 = particle.Speed << 2;
            particle.X += (int)((particle.DirectionX * num2 >> 23) * steps);
            particle.Y += (int)((particle.DirectionY * num2 >> 23) * steps);
            particle.Z += (int)((particle.DirectionZ * num2 >> 23) * steps);
        }

        private static void FadeColour(ref Particle particle, ParticleEmitterRuntime runtime, int steps)
        {
            int num = Clamp16(((particle.Colour >> 8) & 0xFF00) + ((particle.ColourFraction >> 16) & 0xFF) + runtime.RedRate * steps);
            int num2 = Clamp16((particle.Colour & 0xFF00) + ((particle.ColourFraction >> 8) & 0xFF) + runtime.GreenRate * steps);
            int num3 = Clamp16(((particle.Colour << 8) & 0xFF00) + (particle.ColourFraction & 0xFF) + runtime.BlueRate * steps);
            particle.Colour = (particle.Colour & -16777216) | (((num & 0xFF00) << 8) + (num2 & 0xFF00) + ((num3 & 0xFF00) >> 8));
            particle.ColourFraction = (particle.ColourFraction & -16777216) | (((num & 0xFF) << 16) + ((num2 & 0xFF) << 8) + (num3 & 0xFF));
        }

        private static void FadeAlpha(ref Particle particle, ParticleEmitterRuntime runtime, int steps)
        {
            int num = Clamp16(((particle.Colour >> 16) & 0xFF00) + ((particle.ColourFraction >>> 24) & 0xFF) + runtime.AlphaRate * steps);
            particle.Colour = (particle.Colour & 0xFFFFFF) | ((num & 0xFF00) << 16);
            particle.ColourFraction = (particle.ColourFraction & 0xFFFFFF) | ((num & 0xFF) << 24);
        }

        private static void ApplyDrag(ref Particle particle, ParticleEmitterInstance emitter, int steps)
        {
            ParticleEmitterDefinition definition = emitter.Runtime.Definition;
            if (definition.DragMode == 1 || definition.DragMode == 2)
            {
                int num = (particle.X >> 12) - emitter.CentroidX;
                int num2 = (particle.Y >> 12) - emitter.CentroidY;
                int num3 = (particle.Z >> 12) - emitter.CentroidZ;
                if (definition.DragMode == 1)
                {
                    int num4 = (int)Math.Sqrt((double)num * (double)num + (double)num2 * (double)num2 + (double)num3 * (double)num3) >> 2;
                    long num5 = definition.DragStrength * num4 * steps;
                    particle.Speed -= (int)(particle.Speed * num5 >> 18);
                }
                else
                {
                    int num6 = num * num + num2 * num2 + num3 * num3;
                    long num7 = definition.DragStrength * num6 * steps;
                    particle.Speed -= (int)(particle.Speed * num7 >> 28);
                }
            }
        }

        private void ApplyEffectors(ref Particle particle, ParticleEmitterRuntime runtime, int steps, ref double dirX, ref double dirY, ref double dirZ, ref bool directionChanged)
        {
            int[]? sceneEffectorIds = runtime.Definition.SceneEffectorIds;
            if (sceneEffectorIds != null && sceneEffectorIds.Length > 0)
            {
                foreach (ParticleEffectorInstance effector in effectors)
                {
                    if (effector.Runtime.Definition.Mode != 1 && Array.IndexOf(sceneEffectorIds, effector.EffectorId) >= 0)
                    {
                        ApplyPositionalEffector(ref particle, effector, steps, ref dirX, ref dirY, ref dirZ, ref directionChanged);
                    }
                }
            }
            foreach (ParticleEffectorRuntime globalEffector in globalEffectors)
            {
                ParticleEffectorDefinition definition = globalEffector.Definition;
                if (!definition.MovesPositionRatherThanVelocity)
                {
                    dirX += (double)definition.DirectionX * (double)steps;
                    dirY += (double)definition.DirectionY * (double)steps;
                    dirZ += (double)definition.DirectionZ * (double)steps;
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

        private static void ApplyPositionalEffector(ref Particle particle, ParticleEffectorInstance instance, int steps, ref double dirX, ref double dirY, ref double dirZ, ref bool directionChanged)
        {
            ParticleEffectorRuntime runtime = instance.Runtime;
            ParticleEffectorDefinition definition = runtime.Definition;
            double num = (particle.X >> 12) - instance.X;
            double num2 = (particle.Y >> 12) - instance.Y;
            double num3 = (particle.Z >> 12) - instance.Z;
            double num4 = num * num + num2 * num2 + num3 * num3;
            if (num4 > (double)runtime.RadiusBound)
            {
                return;
            }
            double num5 = Math.Sqrt(num4);
            if (num5 == 0.0)
            {
                num5 = 1.0;
            }
            if (runtime.Magnitude == 0)
            {
                return;
            }
            double num6 = (num * (double)definition.DirectionX + num2 * (double)definition.DirectionY + num3 * (double)definition.DirectionZ) * 65535.0 / ((double)runtime.Magnitude * num5);
            if (!(num6 < (double)runtime.ConeCosine))
            {
                int falloffMode = definition.FalloffMode;
                if (1 == 0)
                {
                }
                double num7 = falloffMode switch
                {
                    1 => num5 / 16.0 * (double)runtime.Divisor, 
                    2 => num5 / 16.0 * (num5 / 16.0) * (double)runtime.Divisor, 
                    _ => 0.0, 
                };
                if (1 == 0)
                {
                }
                double num8 = num7;
                double num9;
                double num10;
                double num11;
                if (!definition.IsRadial)
                {
                    num9 = (double)definition.DirectionX - num8;
                    num10 = (double)definition.DirectionY - num8;
                    num11 = (double)definition.DirectionZ - num8;
                }
                else
                {
                    num9 = num / num5 * (double)runtime.Magnitude;
                    num10 = num2 / num5 * (double)runtime.Magnitude;
                    num11 = num3 / num5 * (double)runtime.Magnitude;
                }
                if (!definition.MovesPositionRatherThanVelocity)
                {
                    dirX += num9 * (double)steps;
                    dirY += num10 * (double)steps;
                    dirZ += num11 * (double)steps;
                    directionChanged = true;
                }
                else
                {
                    particle.X += (int)(num9 * (double)steps);
                    particle.Y += (int)(num10 * (double)steps);
                    particle.Z += (int)(num11 * (double)steps);
                }
            }
        }

        private static int Clamp16(int value)
        {
            return (value >= 0) ? ((value > 65535) ? 65535 : value) : 0;
        }
    }
}
