using System;
using FlashEditor.Definitions.Particles;

namespace FlashEditor.Rendering
{
    public struct Particle
    {
        public int X;

        public int Y;

        public int Z;

        public short DirectionX;

        public short DirectionY;

        public short DirectionZ;

        public int Speed;

        public int Size;

        public int Colour;

        public int ColourFraction;

        public int Life;

        public int MaxLife;

        public int MaterialId;

        public int EmitterSlot;

        public readonly int Red => (Colour >> 16) & 0xFF;

        public readonly int Green => (Colour >> 8) & 0xFF;

        public readonly int Blue => Colour & 0xFF;

        public readonly int Alpha => Colour >>> 24;
    }

    public sealed class ParticleEmitterInstance
    {
        private readonly int[] currentX = new int[3];

        private readonly int[] currentY = new int[3];

        private readonly int[] currentZ = new int[3];

        private readonly int[] previousX = new int[3];

        private readonly int[] previousY = new int[3];

        private readonly int[] previousZ = new int[3];

        private int accumulator;

        private int centroidX;

        private int centroidY;

        private int centroidZ;

        private bool centroidValid;

        private int yawBase;

        private int pitchBase;

        public ParticleEmitterRuntime Runtime { get; }

        public int EmitterId { get; }

        public int ModelIndex { get; }

        public int FaceIndex { get; }

        public ParticleRandom Random { get; }

        public bool Primed { get; private set; }

        public bool FaceIsDegenerate { get; private set; }

        public int NormalX { get; private set; }

        public int NormalY { get; private set; }

        public int NormalZ { get; private set; }

        public int CentroidX => centroidX;

        public int CentroidY => centroidY;

        public int CentroidZ => centroidZ;

        public ParticleEmitterInstance(ParticleEmitterRuntime runtime, int emitterId, int modelIndex, int faceIndex, ParticleRandom random)
        {
            Runtime = runtime ?? throw new ArgumentNullException("runtime");
            Random = random ?? throw new ArgumentNullException("random");
            EmitterId = emitterId;
            ModelIndex = modelIndex;
            FaceIndex = faceIndex;
            accumulator = random.NextScaled(64);
        }

        public void SetFace(int ax, int ay, int az, int bx, int by, int bz, int cx, int cy, int cz)
        {
            Array.Copy(currentX, previousX, 3);
            Array.Copy(currentY, previousY, 3);
            Array.Copy(currentZ, previousZ, 3);
            currentX[0] = ax;
            currentY[0] = ay;
            currentZ[0] = az;
            currentX[1] = bx;
            currentY[1] = by;
            currentZ[1] = bz;
            currentX[2] = cx;
            currentY[2] = cy;
            currentZ[2] = cz;
            if (!centroidValid)
            {
                Array.Copy(currentX, previousX, 3);
                Array.Copy(currentY, previousY, 3);
                Array.Copy(currentZ, previousZ, 3);
            }
            FaceIsDegenerate = ax == bx && bx == cx && ay == by && by == cy && az == bz && bz == cz;
            int num = (ax + bx + cx) / 3;
            int num2 = (ay + by + cy) / 3;
            int num3 = (az + bz + cz) / 3;
            if (!centroidValid || num != centroidX || num2 != centroidY || num3 != centroidZ)
            {
                centroidX = num;
                centroidY = num2;
                centroidZ = num3;
                centroidValid = true;
                RecomputeNormal();
            }
        }

        public bool IsOn(long elapsedMilliseconds)
        {
            if (FaceIsDegenerate)
            {
                return false;
            }
            ParticleEmitterDefinition definition = Runtime.Definition;
            if (definition.CyclePeriod == -1)
            {
                return true;
            }
            long num = elapsedMilliseconds;
            if (!definition.CycleRepeats && num > definition.CyclePeriod)
            {
                return false;
            }
            num %= definition.CyclePeriod;
            if (!definition.EmitsBeforeThreshold && num < definition.CycleThreshold)
            {
                return false;
            }
            if (definition.EmitsBeforeThreshold && definition.CycleThreshold <= num)
            {
                return false;
            }
            return true;
        }

        public int Emit(int steps)
        {
            ParticleEmitterDefinition definition = Runtime.Definition;
            accumulator += (int)((double)steps * (Random.NextFraction() * (double)(definition.SpawnRateMax - definition.SpawnRateMin) + (double)definition.SpawnRateMin));
            if (accumulator < 64)
            {
                return 0;
            }
            int result = accumulator >> 6;
            accumulator &= 63;
            return result;
        }

        public int TakePrimingSteps()
        {
            if (Primed)
            {
                return 0;
            }
            Primed = true;
            return Runtime.Definition.PrimeSteps;
        }

        public Particle Spawn()
        {
            ParticleEmitterDefinition definition = Runtime.Definition;
            Particle result = default(Particle);
            PickDirection(out var x, out var y, out var z);
            PickPosition(out var x2, out var y2, out var z2);
            result.X = x2 << 12;
            result.Y = y2 << 12;
            result.Z = z2 << 12;
            result.DirectionX = (short)x;
            result.DirectionY = (short)y;
            result.DirectionZ = (short)z;
            result.Speed = Random.NextScaled(definition.SpeedMax - definition.SpeedMin) + definition.SpeedMin;
            result.Life = (result.MaxLife = (short)(Random.NextScaled(definition.LifetimeMax - definition.LifetimeMin) + definition.LifetimeMin));
            result.Size = Runtime.SizeMin + Random.NextScaled(Runtime.SizeMax - Runtime.SizeMin);
            result.Colour = PickColour();
            result.ColourFraction = 0;
            result.MaterialId = definition.MaterialId;
            return result;
        }

        private void PickDirection(out int x, out int y, out int z)
        {
            if (!Runtime.SpawnsAlongAnAngleRange)
            {
                x = NormalX;
                y = NormalY;
                z = NormalZ;
                return;
            }
            int angle = (Random.NextScaled(Runtime.YawSpread) + yawBase) & 0x3FFF;
            int num = SkeletalTrig.Sin(angle);
            int num2 = SkeletalTrig.Cos(angle);
            int angle2 = (Random.NextScaled(Runtime.PitchSpread) + pitchBase) & 0x1FFF;
            int num3 = SkeletalTrig.Sin(angle2);
            int num4 = SkeletalTrig.Cos(angle2);
            x = num2 * num3 >> 13;
            y = (num4 << 1) * -1;
            z = num * num3 >> 13;
        }

        private void PickPosition(out int x, out int y, out int z)
        {
            float num = (float)Random.NextFraction();
            float num2 = (float)Random.NextFraction();
            if (num + num2 > 1f)
            {
                num = 1f - num;
                num2 = 1f - num2;
            }
            float num3 = 1f - (num2 + num);
            int num4 = (int)((float)currentX[1] * num2 + (float)currentX[0] * num + (float)currentX[2] * num3);
            int num5 = (int)(num3 * (float)currentY[2] + ((float)currentY[0] * num + num2 * (float)currentY[1]));
            int num6 = (int)((float)currentZ[2] * num3 + ((float)currentZ[0] * num + (float)currentZ[1] * num2));
            int num7 = (int)((float)previousX[0] * num + num2 * (float)previousX[1] + num3 * (float)previousX[2]);
            int num8 = (int)(num3 * (float)previousY[2] + ((float)previousY[1] * num2 + (float)previousY[0] * num));
            int num9 = (int)(num2 * (float)previousZ[1] + num * (float)previousZ[0] + (float)previousZ[2] * num3);
            x = (int)((double)(num4 - num7) * Random.NextFraction() + (double)num7);
            y = (int)((double)num8 + (double)(num5 - num8) * Random.NextFraction());
            z = (int)((double)num9 + Random.NextFraction() * (double)(num6 - num9));
        }

        private int PickColour()
        {
            ParticleEmitterRuntime runtime = Runtime;
            if (runtime.Definition.RandomisesColourChannelsIndependently)
            {
                int num = Random.NextScaled(runtime.AlphaSpan) + runtime.AlphaBase;
                int num2 = runtime.RedBase + Random.NextScaled(runtime.RedSpan);
                int num3 = Random.NextScaled(runtime.GreenSpan) + runtime.GreenBase;
                int num4 = runtime.BlueBase + Random.NextScaled(runtime.BlueSpan);
                return (num << 24) | (num2 << 16) | (num3 << 8) | num4;
            }
            double num5 = Random.NextFraction();
            int num6 = (int)((double)runtime.RedBase + num5 * (double)runtime.RedSpan);
            int num7 = (int)((double)runtime.GreenSpan * num5 + (double)runtime.GreenBase);
            int num8 = (int)(num5 * (double)runtime.BlueSpan + (double)runtime.BlueBase);
            int num9 = Random.NextScaled(runtime.AlphaSpan) + runtime.AlphaBase;
            return (num9 << 24) | (num6 << 16) | (num7 << 8) | num8;
        }

        private void RecomputeNormal()
        {
            int num = currentX[1] - currentX[0];
            int num2 = currentY[1] - currentY[0];
            int num3 = currentZ[1] - currentZ[0];
            int num4 = currentX[2] - currentX[0];
            int num5 = currentY[2] - currentY[0];
            int num6 = currentZ[2] - currentZ[0];
            int num7 = num * num5 - num2 * num4;
            int num8 = num2 * num6 - num3 * num5;
            int num9 = num3 * num4 - num * num6;
            while (num8 > 32767 || num9 > 32767 || num7 > 32767 || num8 < -32767 || num9 < -32767 || num7 < -32767)
            {
                num8 >>= 1;
                num9 >>= 1;
                num7 >>= 1;
            }
            int num10 = (int)Math.Sqrt((double)num7 * (double)num7 + (double)num8 * (double)num8 + (double)num9 * (double)num9);
            if (num10 <= 0)
            {
                num10 = 1;
            }
            NormalX = num8 * 32767 / num10;
            NormalY = num9 * 32767 / num10;
            NormalZ = num7 * 32767 / num10;
            if (Runtime.SpawnsAlongAnAngleRange)
            {
                int num11 = (int)(8192.0 / Math.PI * Math.Atan2(NormalZ, NormalX));
                int num12 = (int)(Math.Atan2(NormalY, Math.Sqrt((double)NormalZ * (double)NormalZ + (double)NormalX * (double)NormalX)) * (8192.0 / Math.PI));
                yawBase = Runtime.YawStart + num11 - (Runtime.YawSpread >> 1);
                pitchBase = Runtime.PitchStart + num12 - (Runtime.PitchSpread >> 1);
            }
        }
    }

    public sealed class ParticleEffectorInstance
    {
        public ParticleEffectorRuntime Runtime { get; }

        public int EffectorId { get; }

        public int ModelIndex { get; }

        public int VertexIndex { get; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public int Z { get; private set; }

        public ParticleEffectorInstance(ParticleEffectorRuntime runtime, int effectorId, int modelIndex, int vertexIndex)
        {
            Runtime = runtime ?? throw new ArgumentNullException("runtime");
            EffectorId = effectorId;
            ModelIndex = modelIndex;
            VertexIndex = vertexIndex;
        }

        public void SetPosition(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
