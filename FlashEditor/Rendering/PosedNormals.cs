using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public static class PosedNormals
    {
        private const int ComponentLimit = 8192;

        private const int NormalLength = 256;

        private const int FlatRenderType = 1;

        private const int SmoothRenderType = 0;

        public static float[][] ComputeFaceVertexNormals(PosedMesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException("mesh");
            }
            ModelDefinition model = mesh.Skin.Model;
            int num = model.faceIndices1.Length;
            int num2 = mesh.VertexX.Length;
            int[] array = new int[num2];
            int[] array2 = new int[num2];
            int[] array3 = new int[num2];
            int[] array4 = new int[num];
            int[] array5 = new int[num];
            int[] array6 = new int[num];
            bool[] array7 = new bool[num];
            for (int i = 0; i < num; i++)
            {
                int num3 = model.faceIndices1[i];
                int num4 = model.faceIndices2[i];
                int num5 = model.faceIndices3[i];
                if ((uint)num3 < (uint)num2 && (uint)num4 < (uint)num2 && (uint)num5 < (uint)num2)
                {
                    int num6 = mesh.VertexX[num4] - mesh.VertexX[num3];
                    int num7 = mesh.VertexY[num4] - mesh.VertexY[num3];
                    int num8 = mesh.VertexZ[num4] - mesh.VertexZ[num3];
                    int num9 = mesh.VertexX[num5] - mesh.VertexX[num3];
                    int num10 = mesh.VertexY[num5] - mesh.VertexY[num3];
                    int num11 = mesh.VertexZ[num5] - mesh.VertexZ[num3];
                    int num12 = num7 * num11 - num10 * num8;
                    int num13 = num8 * num9 - num11 * num6;
                    int num14 = num6 * num10 - num9 * num7;
                    while (num12 > 8192 || num13 > 8192 || num14 > 8192 || num12 < -8192 || num13 < -8192 || num14 < -8192)
                    {
                        num12 >>= 1;
                        num13 >>= 1;
                        num14 >>= 1;
                    }
                    int num15 = (int)Math.Sqrt((double)num12 * (double)num12 + (double)num13 * (double)num13 + (double)num14 * (double)num14);
                    if (num15 <= 0)
                    {
                        num15 = 1;
                    }
                    num12 = num12 * 256 / num15;
                    num13 = num13 * 256 / num15;
                    num14 = num14 * 256 / num15;
                    switch ((model.FaceRenderType != null && i < model.FaceRenderType.Length) ? model.FaceRenderType[i] : 0)
                    {
                    case 1:
                        array7[i] = true;
                        array4[i] = num12;
                        array5[i] = num13;
                        array6[i] = num14;
                        break;
                    case 0:
                        array[num3] += num12;
                        array2[num3] += num13;
                        array3[num3] += num14;
                        array[num4] += num12;
                        array2[num4] += num13;
                        array3[num4] += num14;
                        array[num5] += num12;
                        array2[num5] += num13;
                        array3[num5] += num14;
                        break;
                    }
                }
            }
            float[][] array8 = new float[num][];
            Span<int> span = stackalloc int[3];
            for (int j = 0; j < num; j++)
            {
                int num16 = model.faceIndices1[j];
                int num17 = model.faceIndices2[j];
                int num18 = model.faceIndices3[j];
                if ((uint)num16 >= (uint)num2 || (uint)num17 >= (uint)num2 || (uint)num18 >= (uint)num2)
                {
                    array8[j] = new float[9] { 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f };
                    continue;
                }
                if (array7[j])
                {
                    var (num19, num20, num21) = Normalise(array4[j], array5[j], array6[j]);
                    array8[j] = new float[9] { num19, num20, num21, num19, num20, num21, num19, num20, num21 };
                    continue;
                }
                span[0] = num16;
                span[1] = num17;
                span[2] = num18;
                float[] array9 = new float[9];
                for (int k = 0; k < 3; k++)
                {
                    int num22 = span[k];
                    var (num23, num24, num25) = Normalise(array[num22], array2[num22], array3[num22]);
                    array9[k * 3] = num23;
                    array9[k * 3 + 1] = num24;
                    array9[k * 3 + 2] = num25;
                }
                array8[j] = array9;
            }
            return array8;
        }

        private static (float, float, float) Normalise(int x, int y, int z)
        {
            float num = MathF.Sqrt((float)x * (float)x + (float)y * (float)y + (float)z * (float)z);
            if (num < 1f)
            {
                num = 1f;
            }
            return ((float)x / num, (float)(-y) / num, (float)(-z) / num);
        }
    }
}
