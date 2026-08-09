using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Animation;
using FlashEditor.Rendering;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Whether an entity built from several model files still holds together once it is posed.
    /// </summary>
    /// <remarks>
    ///     The one claim about the 3D viewport that is a number rather than a picture. Nothing on this
    ///     machine can capture the GL surface, so "the jaw came off the head" is an observation a human
    ///     makes and nothing in this suite can repeat - except this way round: the parts of an entity
    ///     meet along seams where two model files carry a vertex at the <i>same rest coordinate</i>,
    ///     and whatever the pose does, those two copies have to end up in the same place. A gap between
    ///     them is exactly the detached jaw, measured.
    ///     <para>
    ///     The client never poses the parts separately. <c>Class141.java:801</c> merges them with
    ///     <c>new Model(models, models.length)</c> whenever there is more than one, and
    ///     <c>Node_Sub3.java:172</c> does the same on the equipped-model path, so
    ///     <c>Renderable_Sub2.java:997</c> builds its vertex-label groups over the whole body and
    ///     <c>:2803-2827</c> sums a pivot centroid over the whole body. Posing each part against its
    ///     own centroid instead gives every part a different pivot, and a part carrying none of the
    ///     pivot bone's labels falls back to the bare offset - the model origin, which on a character
    ///     sits on the floor between the feet.
    ///     </para>
    ///     <para>
    ///     Two figures are reported rather than one. The <b>worst</b> gap is the visible defect; the
    ///     <b>total</b> over every shared coordinate is what says whether a change fixed the body or
    ///     only the one seam that happened to be measured.
    ///     </para>
    /// </remarks>
    public class SeamCoherenceTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>NPC 1, "Man" - eight model files, and the case the defect was seen on.</summary>
        /// <remarks>
        ///     Declared in both caches with the same eight model ids, which is what makes the figures
        ///     below properties of build 639 rather than of one cache on this machine. The test asserts
        ///     that rather than trusting it.
        /// </remarks>
        private const int NpcId = 1;

        /// <summary>
        ///     The frame the defect was diagnosed on, named directly rather than through an animation.
        /// </summary>
        /// <remarks>
        ///     A packed index-0 id: group 232, file 122. Pinned rather than reached by playing an
        ///     animation record, because an animation's frame list is an index-20 record that a repack
        ///     may have edited, and the claim here is about one frame's transforms and not about which
        ///     animation happens to run them.
        /// </remarks>
        private const int SeamFrameId = 15204474;

        /// <summary>
        ///     How far apart two copies of one rest coordinate may end up, in model units.
        /// </summary>
        /// <remarks>
        ///     Zero, and it has to be. Merging welds coincident vertices into one
        ///     (<c>Model.method2598</c>, <c>Model.java:1824-1848</c>), so both parts read their posed
        ///     position out of the same composite vertex and land on the same point by construction
        ///     rather than by arriving near it.
        ///     <para>
        ///     This was 16 for one commit, on the reasoning that keeping the first contributor's label
        ///     on a disputed seam would leave a small residue. It does not - the weld removes the
        ///     dispute rather than resolving it - and 16 was measured to swallow a real regression: with
        ///     the weld disabled and only the pivots merged, this sweep reports a worst gap of 11 and a
        ///     total of 11, which a tolerance of 16 passes silently. A tolerance that admits a failure
        ///     does not test for it.
        ///     </para>
        /// </remarks>
        private const int MaximumSeamGap = 0;

        private readonly RealCacheFixture fixture;
        private readonly ITestOutputHelper output;

        public SeamCoherenceTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        /// <summary>
        ///     Every rest coordinate two of an NPC's model files share stays one point after posing.
        /// </summary>
        /// <remarks>
        ///     Asserted without an <c>or</c>, and with the denominator asserted separately: a run that
        ///     found no shared coordinates at all would satisfy "no gap exceeded the limit" while
        ///     testing nothing, so the count of seams examined is itself required to be non-zero.
        /// </remarks>
        [RealCacheFact]
        public void PosingAMultiPartNpc_KeepsEveryCoincidentVertexTogether()
        {
            RSCache cache = fixture.OpenCache();
            IReadOnlyList<ModelDefinition> parts = NpcModels(cache, NpcId);

            Assert.True(parts.Count > 1,
                "NPC " + NpcId + " resolved " + parts.Count + " model files, so nothing about seams can be tested.");

            SkeletalAnimator animator = new SkeletalAnimator(new CacheAnimationDataSource(cache));
            animator.SetModels(parts);
            animator.Play(SingleFrameAnimation(SeamFrameId));

            Assert.Null(animator.LastError);
            Assert.True(animator.HasPose, "Frame " + SeamFrameId + " did not pose.");

            SeamReport report = MeasureSeams(parts, animator.Poses);

            output.WriteLine("NPC " + NpcId + " frame " + SeamFrameId + ": "
                + report.SeamCount + " rest coordinates shared by two or more of "
                + parts.Count + " parts, worst gap " + report.WorstGap
                + " model units, total " + report.TotalGap + ".");

            Assert.True(report.SeamCount > 0,
                "No rest coordinate is shared by two of NPC " + NpcId + "'s "
                + parts.Count + " model files, so this test measures nothing.");

            Assert.True(report.WorstGap <= MaximumSeamGap,
                "Posed parts came apart at a seam. Worst gap " + report.WorstGap
                + " model units over " + report.SeamCount + " shared rest coordinates, total "
                + report.TotalGap + ". " + report.WorstDescription);
        }

        /// <summary>What the seam sweep found, so the assertion can report all of it at once.</summary>
        /// <param name="SeamCount">Rest coordinates carried by two or more parts.</param>
        /// <param name="WorstGap">The largest distance any one of them was pulled apart by.</param>
        /// <param name="TotalGap">Those distances summed, so a partial fix is visible.</param>
        /// <param name="WorstDescription">Which parts and vertices produced <paramref name="WorstGap"/>.</param>
        private readonly record struct SeamReport(
            int SeamCount, int WorstGap, long TotalGap, string WorstDescription);

        /// <summary>
        ///     Measures how far apart the copies of each shared rest coordinate ended up.
        /// </summary>
        /// <remarks>
        ///     Only coordinates carried by <i>different</i> parts count: two coincident vertices within
        ///     one model file are welded by neither this project nor the client, because the client
        ///     merges only when it has more than one model (<c>Class141.java:801</c>).
        ///     <para>
        ///     Measured against the <b>rest</b> coordinate rather than against the same vertex in an
        ///     earlier frame. A part that is displaced by the same wrong amount on every frame is
        ///     stationary relative to itself and would satisfy a frame-to-frame comparison while
        ///     sitting visibly off the body, which is what the boots do.
        ///     </para>
        /// </remarks>
        /// <param name="parts">The model files, in the order they were posed.</param>
        /// <param name="poses">One pose per part, in the same order.</param>
        /// <returns>The report.</returns>
        private static SeamReport MeasureSeams(
            IReadOnlyList<ModelDefinition> parts, IReadOnlyList<PosedMesh> poses)
        {
            Dictionary<(int, int, int), List<(int Part, int Vertex)>> byRestCoordinate =
                new Dictionary<(int, int, int), List<(int Part, int Vertex)>>();

            for (int part = 0; part < parts.Count; part++)
            {
                ModelDefinition model = parts[part];

                for (int vertex = 0; vertex < model.VertX.Length; vertex++)
                {
                    (int, int, int) key = (model.VertX[vertex], model.VertY[vertex], model.VertZ[vertex]);

                    if (!byRestCoordinate.TryGetValue(key, out List<(int Part, int Vertex)> carriers))
                    {
                        carriers = new List<(int Part, int Vertex)>();
                        byRestCoordinate[key] = carriers;
                    }

                    carriers.Add((part, vertex));
                }
            }

            int seams = 0;
            int worst = 0;
            long total = 0;
            string worstDescription = "No seam was pulled apart.";

            foreach (KeyValuePair<(int, int, int), List<(int Part, int Vertex)>> entry in byRestCoordinate)
            {
                List<(int Part, int Vertex)> carriers = entry.Value;

                if (carriers.Select(carrier => carrier.Part).Distinct().Count() < 2)
                {
                    continue;
                }

                seams++;
                int gap = 0;
                (int Part, int Vertex) gapLeft = carriers[0];
                (int Part, int Vertex) gapRight = carriers[0];

                for (int a = 0; a < carriers.Count; a++)
                {
                    for (int b = a + 1; b < carriers.Count; b++)
                    {
                        if (carriers[a].Part == carriers[b].Part)
                        {
                            continue;
                        }

                        int distance = SeamGap(poses, carriers[a], carriers[b]);

                        if (distance > gap)
                        {
                            gap = distance;
                            gapLeft = carriers[a];
                            gapRight = carriers[b];
                        }
                    }
                }

                total += gap;

                if (gap > worst)
                {
                    worst = gap;
                    worstDescription = "Worst at rest coordinate " + entry.Key
                        + ": part " + gapLeft.Part + " vertex " + gapLeft.Vertex
                        + " against part " + gapRight.Part + " vertex " + gapRight.Vertex + ".";
                }
            }

            return new SeamReport(seams, worst, total, worstDescription);
        }

        /// <summary>How far apart two posed copies of one rest coordinate ended up.</summary>
        /// <remarks>
        ///     Straight-line distance, rounded to the nearest model unit. A per-axis maximum would also
        ///     be a defensible measure and is not the one the diagnosis used, so this stays Euclidean
        ///     to keep the figures comparable: the same sweep reports 695 worst over 46 shared
        ///     coordinates by this measure and 454 by a per-axis maximum, and quoting one against the
        ///     other would read as a regression that never happened.
        /// </remarks>
        /// <param name="poses">The poses, indexed by part.</param>
        /// <param name="left">First carrier of the shared rest coordinate.</param>
        /// <param name="right">Second carrier.</param>
        /// <returns>The gap in model units.</returns>
        private static int SeamGap(IReadOnlyList<PosedMesh> poses,
            (int Part, int Vertex) left, (int Part, int Vertex) right)
        {
            PosedMesh a = poses[left.Part];
            PosedMesh b = poses[right.Part];

            long dx = a.VertexX[left.Vertex] - b.VertexX[right.Vertex];
            long dy = a.VertexY[left.Vertex] - b.VertexY[right.Vertex];
            long dz = a.VertexZ[left.Vertex] - b.VertexZ[right.Vertex];

            return (int)Math.Round(Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        /// <summary>Reads an NPC's model files, in the order the definition names them.</summary>
        /// <remarks>
        ///     The order is the contract the whole viewport is indexed by, so it is preserved rather
        ///     than sorted. A model id of -1 is the definition's way of saying "no part here" and is
        ///     dropped, which is what the editor does when it uploads them.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="npcId">The index-18 id.</param>
        /// <returns>The decoded models.</returns>
        private static IReadOnlyList<ModelDefinition> NpcModels(RSCache cache, int npcId)
        {
            CacheAddressing addressing = CacheAddressing.For(RSConstants.NPC_DEFINITIONS_INDEX);
            NPCDefinition npc = new NPCDefinition(cache.ReadFile(RSConstants.NPC_DEFINITIONS_INDEX,
                addressing.GroupOf(npcId), addressing.FileOf(npcId)));

            List<ModelDefinition> models = new List<ModelDefinition>();

            foreach (int modelId in npc.modelIds ?? Array.Empty<int>())
            {
                if (modelId >= 0)
                {
                    models.Add(cache.GetModelDefinition(modelId, 0));
                }
            }

            return models;
        }

        /// <summary>An animation record holding one named frame and nothing else.</summary>
        /// <remarks>
        ///     Built rather than read, so the test names the frame it means. Reading a real index-20
        ///     record would pin the assertion to whichever animation currently lists that frame, which
        ///     is a fact about a record a repack can edit rather than about the pose.
        /// </remarks>
        /// <param name="packedFrameId">The index-0 frame, packed group and file.</param>
        /// <returns>The record.</returns>
        private static AnimationDefinition SingleFrameAnimation(int packedFrameId)
        {
            return new AnimationDefinition
            {
                Id = -1,
                FrameIds = new[] { packedFrameId },
                FrameDurations = new[] { 1 },
            };
        }
    }
}
