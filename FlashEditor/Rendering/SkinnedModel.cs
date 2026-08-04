using System.Collections.Generic;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public sealed class SkinnedModel
    {
        private static readonly int[] NoMembers = Array.Empty<int>();

        public ModelDefinition Model { get; }

        public int[][] VertexLabelGroups { get; }

        public int[][] FaceLabelGroups { get; }

        public bool IsSkinned => VertexLabelGroups.Length != 0 || FaceLabelGroups.Length != 0;

        public SkinnedModel(ModelDefinition model)
        {
            Model = model ?? throw new ArgumentNullException("model");
            VertexLabelGroups = ResolveVertexGroups(model);
            FaceLabelGroups = Group(model.FaceSkin, model.faceIndices1.Length);
        }

        public int[] VerticesFor(int label)
        {
            return ((uint)label < (uint)VertexLabelGroups.Length) ? VertexLabelGroups[label] : NoMembers;
        }

        public int[] FacesFor(int label)
        {
            return ((uint)label < (uint)FaceLabelGroups.Length) ? FaceLabelGroups[label] : NoMembers;
        }

        public PosedMesh CreatePose()
        {
            return new PosedMesh(this);
        }

        private static int[][] ResolveVertexGroups(ModelDefinition model)
        {
            if (model.VertexGroups != null)
            {
                return model.VertexGroups;
            }
            return Group(model.VertSkins, model.VertX.Length);
        }

        private static int[][] Group(IReadOnlyList<int>? labels, int count)
        {
            if (labels == null || count <= 0)
            {
                return Array.Empty<int[]>();
            }
            int num = Math.Min(count, labels.Count);
            int num2 = -1;
            for (int i = 0; i < num; i++)
            {
                if (labels[i] > num2)
                {
                    num2 = labels[i];
                }
            }
            if (num2 < 0)
            {
                return Array.Empty<int[]>();
            }
            int[] array = new int[num2 + 1];
            for (int j = 0; j < num; j++)
            {
                if (labels[j] >= 0)
                {
                    array[labels[j]]++;
                }
            }
            int[][] array2 = new int[num2 + 1][];
            for (int k = 0; k <= num2; k++)
            {
                array2[k] = ((array[k] == 0) ? NoMembers : new int[array[k]]);
                array[k] = 0;
            }
            for (int l = 0; l < num; l++)
            {
                int num3 = labels[l];
                if (num3 >= 0)
                {
                    array2[num3][array[num3]++] = l;
                }
            }
            return array2;
        }
    }
}
