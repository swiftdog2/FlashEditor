using System;
using FlashEditor.Definitions.Particles;

namespace FlashEditor.Rendering
{
    public static class ParticleUnits
    {
        public const int MillisecondsPerStep = 1;

        public const int PositionFractionBits = 12;

        public const int SizeFractionBits = 12;

        public const int DirectionScale = 32767;

        public const int SpeedShift = 2;

        public const int VelocityShift = 23;

        public const int SpawnAccumulatorPerParticle = 64;

        public const int LinearDragShift = 18;

        public const int QuadraticDragShift = 28;

        public const int ConeAngleShift = 3;
    }

    public sealed class ParticleRandom
    {
        private uint state;

        public ParticleRandom(int seed)
        {
            state = ((seed == 0) ? 2654435769u : ((uint)seed));
        }

        public double NextFraction()
        {
            return (double)NextUInt() / 4294967296.0;
        }

        public int NextScaled(int range)
        {
            return (int)(NextFraction() * (double)range);
        }

        private uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }

    public sealed class ParticleEmitterRuntime
    {
        public ParticleEmitterDefinition Definition { get; }

        public int RedBase { get; }

        public int RedSpan { get; }

        public int GreenBase { get; }

        public int GreenSpan { get; }

        public int BlueBase { get; }

        public int BlueSpan { get; }

        public int AlphaBase { get; }

        public int AlphaSpan { get; }

        public bool HasHeightBound { get; }

        public int SizeMin { get; }

        public int SizeMax { get; }

        public bool HasColourRamp { get; }

        public int ColourRampSteps { get; }

        public int AlphaRampSteps { get; }

        public int RedRate { get; }

        public int GreenRate { get; }

        public int BlueRate { get; }

        public int AlphaRate { get; }

        public bool HasSizeRamp { get; }

        public int EndSize { get; }

        public int SizeRampSteps { get; }

        public int SizeRate { get; }

        public bool HasSpeedRamp { get; }

        public int SpeedRampSteps { get; }

        public int SpeedRate { get; }

        public int YawStart { get; }

        public int YawEnd { get; }

        public int PitchStart { get; }

        public int PitchEnd { get; }

        public int YawSpread { get; }

        public int PitchSpread { get; }

        public bool SpawnsAlongAnAngleRange { get; }

        public ParticleEmitterRuntime(ParticleEmitterDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException("definition");
            RedBase = (definition.SpawnColourStart >> 16) & 0xFF;
            GreenBase = (definition.SpawnColourStart >> 8) & 0xFF;
            BlueBase = definition.SpawnColourStart & 0xFF;
            AlphaBase = (definition.SpawnColourStart >> 24) & 0xFF;
            RedSpan = ((definition.SpawnColourEnd >> 16) & 0xFF) - RedBase;
            GreenSpan = ((definition.SpawnColourEnd >> 8) & 0xFF) - GreenBase;
            BlueSpan = (definition.SpawnColourEnd & 0xFF) - BlueBase;
            AlphaSpan = ((definition.SpawnColourEnd >> 24) & 0xFF) - AlphaBase;
            HasHeightBound = definition.CeilingPlane > -2 || definition.FloorPlane > -2;
            SizeMin = definition.SizeMinStored << 14;
            SizeMax = definition.SizeMaxStored << 14;
            HasColourRamp = definition.FadeColour != 0;
            if (HasColourRamp)
            {
                ColourRampSteps = AtLeastOne(definition.FadeColourPercent * definition.LifetimeMax / 100);
                AlphaRampSteps = AtLeastOne(definition.LifetimeMax * definition.FadeAlphaPercent / 100);
                RedRate = Bias(((definition.FadeColour >> 16) & 0xFF) - RedBase - RedSpan / 2 << 8, ColourRampSteps);
                GreenRate = Bias(((definition.FadeColour >> 8) & 0xFF) - GreenBase - GreenSpan / 2 << 8, ColourRampSteps);
                BlueRate = Bias((definition.FadeColour & 0xFF) - BlueBase - BlueSpan / 2 << 8, ColourRampSteps);
                AlphaRate = Bias(((definition.FadeColour >> 24) & 0xFF) - (AlphaSpan / 2 + AlphaBase) << 8, AlphaRampSteps);
            }
            HasSizeRamp = definition.EndSizeStored != -1;
            if (HasSizeRamp)
            {
                EndSize = definition.EndSizeStored << 14;
                SizeRampSteps = AtLeastOne(definition.SizeRampPercent * definition.LifetimeMax / 100);
                SizeRate = (EndSize - SizeMin - (SizeMax - SizeMin) / 2) / SizeRampSteps;
            }
            HasSpeedRamp = definition.EndSpeed != -1;
            if (HasSpeedRamp)
            {
                SpeedRampSteps = AtLeastOne(definition.LifetimeMax * definition.SpeedRampPercent / 100);
                SpeedRate = (definition.EndSpeed - (definition.SpeedMax - definition.SpeedMin) / 2 - definition.SpeedMin) / SpeedRampSteps;
            }
            YawStart = Shifted(definition.YawStartStored);
            YawEnd = Shifted(definition.YawEndStored);
            PitchStart = Shifted(definition.PitchStartStored);
            PitchEnd = Shifted(definition.PitchEndStored);
            YawSpread = YawEnd - YawStart;
            PitchSpread = PitchEnd - PitchStart;
            SpawnsAlongAnAngleRange = YawEnd > 0 || PitchEnd > 0;
        }

        private static int Bias(int numerator, int steps)
        {
            int num = numerator / steps;
            return num + ((num <= 0) ? 4 : (-4));
        }

        private static int AtLeastOne(int steps)
        {
            return (steps == 0) ? 1 : steps;
        }

        private static int Shifted(int stored)
        {
            return (short)(stored << 3);
        }
    }

    public sealed class ParticleEffectorRuntime
    {
        public ParticleEffectorDefinition Definition { get; }

        public int ConeCosine { get; }

        public int Magnitude { get; }

        public int Divisor { get; }

        public long RadiusBound { get; }

        public ParticleEffectorRuntime(ParticleEffectorDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException("definition");
            ConeCosine = SkeletalTrig.Cos(definition.ConeAngleStored << 3);
            long num = definition.DirectionX;
            long num2 = definition.DirectionY;
            long num3 = definition.DirectionZ;
            int num4 = (int)Math.Sqrt(num * num + num2 * num2 + num3 * num3);
            Divisor = ((definition.Strength == 0) ? 1 : definition.Strength);
            int falloffMode = definition.FalloffMode;
            if (1 == 0)
            {
            }
            long num5 = falloffMode switch
            {
                1 => Square(8L * (long)num4 / Divisor), 
                2 => 8L * (long)num4 / Divisor, 
                _ => 2147483647L, 
            };
            if (1 == 0)
            {
            }
            RadiusBound = num5;
            Magnitude = (definition.IsInverted ? (-num4) : num4);
        }

        private static long Square(long value)
        {
            return value * value;
        }
    }
}
