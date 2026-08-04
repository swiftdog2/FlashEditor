using System;

namespace FlashEditor.Rendering
{
    public static class SkeletalTrig
    {
        public const int AngleSteps = 16384;

        public const int FractionBits = 14;

        public const int One = 16384;

        public const int ShiftBias = 16383;

        private static readonly int[] SinTable = Build(Math.Sin);

        private static readonly int[] CosTable = Build(Math.Cos);

        private const double RadiansPerStep = 0.0003834951969714103;

        public static int Sin(int angle)
        {
            return SinTable[angle & 0x3FFF];
        }

        public static int Cos(int angle)
        {
            return CosTable[angle & 0x3FFF];
        }

        private static int[] Build(Func<double, double> function)
        {
            int[] array = new int[16384];
            for (int i = 0; i < 16384; i++)
            {
                array[i] = (int)(16384.0 * function((double)i * 0.0003834951969714103));
            }
            return array;
        }
    }
}
