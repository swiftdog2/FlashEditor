using System.Collections.Generic;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public enum TransformOutcome
    {
        Applied,
        NoTargets,
        Unsupported
    }

    public sealed class PosedMesh
    {
        public const int TypePivot = 0;

        public const int TypeTranslate = 1;

        public const int TypeRotate = 2;

        public const int TypeScale = 3;

        public const int TypeAlpha = 5;

        public const int TypeColour = 7;

        private const int SubUnitBits = 4;

        private const int SubUnitBias = 7;

        private const int ScaleBits = 7;

        private const int AlphaStep = 8;

        private const int MaxAlpha = 255;

        private const int HueShift = 10;

        private const int HueMask = 63;

        private const int SaturationShift = 7;

        private const int SaturationMask = 7;

        private const int LightnessMask = 127;

        private const int SaturationDivisor = 4;

        public SkinnedModel Skin { get; }

        public int[] VertexX { get; }

        public int[] VertexY { get; }

        public int[] VertexZ { get; }

        public byte[] FaceAlpha { get; }

        public short[] FaceColour { get; }

        public bool FaceAlphaChanged { get; private set; }

        public bool FaceColourChanged { get; private set; }

        public bool IsScaled { get; private set; }

        public int PivotX { get; private set; }

        public int PivotY { get; private set; }

        public int PivotZ { get; private set; }

        public PosedMesh(SkinnedModel skin)
        {
            Skin = skin ?? throw new ArgumentNullException("skin");
            ModelDefinition model = skin.Model;
            VertexX = new int[model.VertX.Length];
            VertexY = new int[model.VertX.Length];
            VertexZ = new int[model.VertX.Length];
            FaceAlpha = new byte[model.faceIndices1.Length];
            FaceColour = new short[model.faceIndices1.Length];
            Reset();
        }

        public void Reset()
        {
            ModelDefinition model = Skin.Model;
            Array.Copy(model.VertX, VertexX, Math.Min(VertexX.Length, model.VertX.Length));
            Array.Copy(model.VertY, VertexY, Math.Min(VertexY.Length, model.VertY.Length));
            Array.Copy(model.VertZ, VertexZ, Math.Min(VertexZ.Length, model.VertZ.Length));
            if (model.FaceAlpha != null)
            {
                int num = Math.Min(FaceAlpha.Length, model.FaceAlpha.Length);
                for (int i = 0; i < num; i++)
                {
                    FaceAlpha[i] = (byte)model.FaceAlpha[i];
                }
            }
            else
            {
                Array.Clear(FaceAlpha, 0, FaceAlpha.Length);
            }
            Array.Copy(model.FaceColour, FaceColour, Math.Min(FaceColour.Length, model.FaceColour.Length));
            IsScaled = false;
            FaceAlphaChanged = false;
            FaceColourChanged = false;
            int num3 = (PivotZ = 0);
            int pivotX = (PivotY = num3);
            PivotX = pivotX;
        }

        public TransformOutcome Apply(int transformType, IReadOnlyList<int> labels, int x, int y, int z)
        {
            if (labels == null)
            {
                throw new ArgumentNullException("labels");
            }
            return transformType switch
            {
                0 => SetPivot(labels, x, y, z), 
                1 => Translate(labels, x, y, z), 
                2 => Rotate(labels, x, y, z), 
                3 => Scale(labels, x, y, z), 
                5 => ShiftAlpha(labels, x), 
                7 => ShiftColour(labels, x, y, z), 
                _ => TransformOutcome.Unsupported, 
            };
        }

        public void Finish()
        {
            if (IsScaled)
            {
                for (int i = 0; i < VertexX.Length; i++)
                {
                    VertexX[i] = VertexX[i] + 7 >> 4;
                    VertexY[i] = VertexY[i] + 7 >> 4;
                    VertexZ[i] = VertexZ[i] + 7 >> 4;
                }
                PivotX = PivotX + 7 >> 4;
                PivotY = PivotY + 7 >> 4;
                PivotZ = PivotZ + 7 >> 4;
                IsScaled = false;
            }
        }

        private TransformOutcome SetPivot(IReadOnlyList<int> labels, int x, int y, int z)
        {
            EnsureScaled();
            int num = x << 4;
            int num2 = y << 4;
            int num3 = z << 4;
            int num4 = 0;
            int num5 = 0;
            int num6 = 0;
            int num7 = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                int[] array = Skin.VerticesFor(labels[i]);
                foreach (int num8 in array)
                {
                    num4 += VertexX[num8];
                    num5 += VertexY[num8];
                    num6 += VertexZ[num8];
                    num7++;
                }
            }
            if (num7 <= 0)
            {
                PivotX = num;
                PivotY = num2;
                PivotZ = num3;
                return TransformOutcome.NoTargets;
            }
            PivotX = num4 / num7 + num;
            PivotY = num5 / num7 + num2;
            PivotZ = num6 / num7 + num3;
            return TransformOutcome.Applied;
        }

        private TransformOutcome Translate(IReadOnlyList<int> labels, int x, int y, int z)
        {
            EnsureScaled();
            int num = x << 4;
            int num2 = y << 4;
            int num3 = z << 4;
            bool flag = false;
            for (int i = 0; i < labels.Count; i++)
            {
                int[] array = Skin.VerticesFor(labels[i]);
                foreach (int num4 in array)
                {
                    VertexX[num4] += num;
                    VertexY[num4] += num2;
                    VertexZ[num4] += num3;
                    flag = true;
                }
            }
            return (!flag) ? TransformOutcome.NoTargets : TransformOutcome.Applied;
        }

        private TransformOutcome Rotate(IReadOnlyList<int> labels, int x, int y, int z)
        {
            bool flag = false;
            for (int i = 0; i < labels.Count; i++)
            {
                int[] array = Skin.VerticesFor(labels[i]);
                foreach (int num in array)
                {
                    int num2 = VertexX[num] - PivotX;
                    int num3 = VertexY[num] - PivotY;
                    int num4 = VertexZ[num] - PivotZ;
                    /* The order is z, then x, then y - Renderable_Sub2.method2344 tests the z value
                       first. Rotations do not commute, so this is not a detail: applied in the
                       written order of the fields, a limb that should bend twists sideways. */
                    if (z != 0)
                    {
                        int num11 = SkeletalTrig.Sin(z);
                        int num12 = SkeletalTrig.Cos(z);
                        int num13 = num12 * num2 + num11 * num3 + 16383 >> 14;
                        num3 = num12 * num3 - num11 * num2 + 16383 >> 14;
                        num2 = num13;
                    }
                    if (x != 0)
                    {
                        int num5 = SkeletalTrig.Sin(x);
                        int num6 = SkeletalTrig.Cos(x);
                        int num7 = num6 * num3 - num5 * num4 + 16383 >> 14;
                        num4 = num5 * num3 + num6 * num4 + 16383 >> 14;
                        num3 = num7;
                    }
                    if (y != 0)
                    {
                        int num8 = SkeletalTrig.Sin(y);
                        int num9 = SkeletalTrig.Cos(y);
                        int num10 = num9 * num2 + num8 * num4 + 16383 >> 14;
                        num4 = num9 * num4 - num8 * num2 + 16383 >> 14;
                        num2 = num10;
                    }
                    VertexX[num] = num2 + PivotX;
                    VertexY[num] = num3 + PivotY;
                    VertexZ[num] = num4 + PivotZ;
                    flag = true;
                }
            }
            return (!flag) ? TransformOutcome.NoTargets : TransformOutcome.Applied;
        }

        private TransformOutcome Scale(IReadOnlyList<int> labels, int x, int y, int z)
        {
            bool flag = false;
            for (int i = 0; i < labels.Count; i++)
            {
                int[] array = Skin.VerticesFor(labels[i]);
                foreach (int num in array)
                {
                    VertexX[num] = ((VertexX[num] - PivotX) * x >> 7) + PivotX;
                    VertexY[num] = ((VertexY[num] - PivotY) * y >> 7) + PivotY;
                    VertexZ[num] = ((VertexZ[num] - PivotZ) * z >> 7) + PivotZ;
                    flag = true;
                }
            }
            return (!flag) ? TransformOutcome.NoTargets : TransformOutcome.Applied;
        }

        private TransformOutcome ShiftAlpha(IReadOnlyList<int> labels, int x)
        {
            bool flag = false;
            for (int i = 0; i < labels.Count; i++)
            {
                int[] array = Skin.FacesFor(labels[i]);
                foreach (int num in array)
                {
                    int value = x * 8 + (FaceAlpha[num] & 0xFF);
                    FaceAlpha[num] = (byte)Math.Clamp(value, 0, 255);
                    flag = true;
                }
            }
            if (flag)
            {
                FaceAlphaChanged = true;
            }
            return (!flag) ? TransformOutcome.NoTargets : TransformOutcome.Applied;
        }

        private TransformOutcome ShiftColour(IReadOnlyList<int> labels, int x, int y, int z)
        {
            bool flag = false;
            for (int i = 0; i < labels.Count; i++)
            {
                int[] array = Skin.FacesFor(labels[i]);
                foreach (int num in array)
                {
                    int num2 = FaceColour[num] & 0xFFFF;
                    int num3 = ((num2 >> 10) + x) & 0x3F;
                    int num4 = Math.Clamp(((num2 >> 7) & 7) + y / 4, 0, 7);
                    int num5 = Math.Clamp((num2 & 0x7F) + z, 0, 127);
                    FaceColour[num] = (short)((num3 << 10) | (num4 << 7) | num5);
                    flag = true;
                }
            }
            if (flag)
            {
                FaceColourChanged = true;
            }
            return (!flag) ? TransformOutcome.NoTargets : TransformOutcome.Applied;
        }

        private void EnsureScaled()
        {
            if (!IsScaled)
            {
                for (int i = 0; i < VertexX.Length; i++)
                {
                    VertexX[i] <<= 4;
                    VertexY[i] <<= 4;
                    VertexZ[i] <<= 4;
                }
                PivotX <<= 4;
                PivotY <<= 4;
                PivotZ <<= 4;
                IsScaled = true;
            }
        }
    }
}
