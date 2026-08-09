using FlashEditor;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Models.Interchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using FlashEditor.IO;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the OBJ export and import, which is the one part of the model path that is testable
    ///     without a renderer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Nothing here draws anything, and nothing needs to. An export is a transform from a
    ///     <see cref="ModelFile"/> to text and an import is the transform back, so the whole of it
    ///     is arithmetic over integers: which delta each vertex stores, which strip opcode reaches
    ///     each face from the one before, and which arrays are carried over untouched. That last
    ///     one is the reason this file exists at all - a naive round trip through OBJ silently
    ///     discards every per-face and per-vertex array the format carries, and it discards them in
    ///     a way that looks fine until the model is next drawn.
    ///     </para>
    ///     <para>
    ///     The strongest single check is elsewhere, in <c>RealCacheModelObjTests</c>: export a real
    ///     model, re-import it unedited, and require the stored bytes back. What that cannot see is
    ///     a refusal that never triggers, a rebuild that is never exercised because unchanged
    ///     geometry keeps its bytes, or a field added to <see cref="ModelFile"/> and forgotten in
    ///     the preservation copy. Those are here.
    ///     </para>
    /// </remarks>
    public class ModelObjInterchangeTests
    {
        /// <summary>A model id well below the new-protocol range, so the sentinel decides.</summary>
        private const int SentinelModelId = 100;

        /// <summary>A model id inside the range the client treats as new-protocol.</summary>
        private const int NewProtocolModelId = ModelCodec.FirstNewProtocolModelId;

        // ===================================================================
        //  The axis and scale conventions
        // ===================================================================

        /// <summary>
        ///     The exporter negates Y and Z, and nothing else.
        /// </summary>
        /// <remarks>
        ///     A model's Y grows downwards, so a straight copy stands every model on its head. The
        ///     fix has to be a half turn about X rather than a mirror: negating Y alone flips
        ///     handedness, which reverses face winding, and a modeller would then show every
        ///     triangle inside out. Asserting the emitted text is the only way to pin this - a round
        ///     trip cancels the error out and passes either way.
        /// </remarks>
        [Fact]
        public void Export_NegatesYAndZ_AndLeavesFaceWindingAlone()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            ObjDocument document = ModelObjExporter.Export(file);

            string[] vertices = Lines(document.ObjText, "v ");
            Assert.Equal("v 0 0 0", vertices[0]);
            Assert.Equal("v 10 -20 -30", vertices[1]);
            Assert.Equal("v 40 -50 -60", vertices[2]);

            //Face 0 is vertices 0, 1, 2 in the model, so 1, 2, 3 in OBJ's one-based indexing and
            //in that order.
            Assert.Equal("f 1 2 3", Lines(document.ObjText, "f ")[0]);
        }

        /// <summary>
        ///     The exported coordinates are the stored ones, and the header says what the client
        ///     would do with them.
        /// </summary>
        /// <remarks>
        ///     A format-12 model is drawn four times larger than it is stored, because its callers
        ///     shift the coordinates left by two afterwards (Model.java:1682-1700). Exporting the
        ///     shifted numbers would make the import lossy for any edit that did not land on a
        ///     four-unit grid, so the stored numbers go out and the shift is stated instead.
        /// </remarks>
        [Fact]
        public void Export_WritesStoredCoordinates_AndStatesTheShiftTheClientApplies()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            Assert.Equal(2, file.VertexShift);

            ObjDocument document = ModelObjExporter.Export(file);

            Assert.Contains("v 10 -20 -30", document.ObjText);
            Assert.Contains("draws this model 4 times larger", document.ObjText);
            Assert.Contains(document.Summary, line => line.Contains("4 times larger"));
        }

        // ===================================================================
        //  Round trips that do not need a cache
        // ===================================================================

        /// <summary>
        ///     An unedited export re-imports to the very same model, on every one of the three
        ///     layouts.
        /// </summary>
        /// <remarks>
        ///     The identity claim is stronger than "the geometry matches": the import must hand back
        ///     the object it was given, because rebuilding the blocks would renormalise the strip
        ///     opcodes and the smart widths and change bytes nobody edited.
        /// </remarks>
        /// <param name="encoding">The layout under test.</param>
        /// <param name="modelId">A model id that selects it.</param>
        [Theory]
        [InlineData(ModelEncoding.Legacy, SentinelModelId)]
        [InlineData(ModelEncoding.Newer, SentinelModelId)]
        [InlineData(ModelEncoding.NewProtocol, NewProtocolModelId)]
        public void AnUneditedRoundTrip_ChangesNothingAtAll(ModelEncoding encoding, int modelId)
        {
            ModelFile file = Cube(encoding, modelId);
            byte[] before = ModelCodec.Encode(file).ToArray();

            ObjDocument document = ModelObjExporter.Export(file);
            ModelImportResult result = ModelObjImporter.Import(file, document.ObjText);

            Assert.True(result.Succeeded, result.Message);
            Assert.False(result.GeometryChanged);
            Assert.Same(file, result.Model);
            Assert.Equal(before, ModelCodec.Encode(result.Model).ToArray());
        }

        /// <summary>
        ///     A rebuilt model survives the real codec: encode it, decode it, and the geometry that
        ///     went in comes back.
        /// </summary>
        /// <remarks>
        ///     This is what the unedited round trip cannot check, because that path keeps the
        ///     original bytes and never runs the encoder. Here the deltas, the vertex masks, the
        ///     strip opcodes and the four declared block lengths are all freshly computed, and the
        ///     production decoder is the thing that reads them back.
        /// </remarks>
        /// <param name="encoding">The layout under test.</param>
        /// <param name="modelId">A model id that selects it.</param>
        [Theory]
        [InlineData(ModelEncoding.Legacy, SentinelModelId)]
        [InlineData(ModelEncoding.Newer, SentinelModelId)]
        [InlineData(ModelEncoding.NewProtocol, NewProtocolModelId)]
        public void AnEditedRoundTrip_SurvivesTheCodec(ModelEncoding encoding, int modelId)
        {
            ModelFile file = Cube(encoding, modelId);
            string edited = Translate(ModelObjExporter.Export(file).ObjText, 3, -5, 7);

            ModelImportResult result = ModelObjImporter.Import(file, edited);
            Assert.True(result.Succeeded, result.Message);
            Assert.True(result.GeometryChanged);

            ModelFile again = ModelCodec.Decode(ModelCodec.Encode(result.Model).ToArray(), modelId);
            ModelGeometry expected = ModelGeometry.FromFile(file);
            ModelGeometry actual = ModelGeometry.FromFile(again);

            for (int i = 0; i < expected.VertexCount; i++)
            {
                //The OBJ was moved in OBJ space, where Y and Z are negated.
                Assert.Equal(expected.X[i] + 3, actual.X[i]);
                Assert.Equal(expected.Y[i] + 5, actual.Y[i]);
                Assert.Equal(expected.Z[i] - 7, actual.Z[i]);
            }

            Assert.Equal(expected.FaceA, actual.FaceA);
            Assert.Equal(expected.FaceB, actual.FaceB);
            Assert.Equal(expected.FaceC, actual.FaceC);
        }

        /// <summary>
        ///     Every strip opcode the format has is chosen when it applies, and the decoder agrees
        ///     about what each one means.
        /// </summary>
        /// <remarks>
        ///     The three rolling opcodes differ only in which two of the previous face's vertices
        ///     they keep - 2 keeps <c>(a, c)</c>, 3 keeps <c>(c, b)</c>, 4 keeps <c>(b, a)</c> - so
        ///     an encoder that confused any pair still produces a well-formed file that decodes to
        ///     different triangles. The check is that the production decoder recovers the faces that
        ///     went in.
        /// </remarks>
        [Fact]
        public void TheStripEncoder_UsesEveryOpcode_AndTheDecoderAgrees()
        {
            //Chosen so that each face after the first matches exactly one rolling opcode.
            var faces = new[]
            {
                (0, 1, 2),  //nothing to roll from, so a restart
                (0, 2, 3),  //keeps (a, c) - opcode 2
                (3, 2, 4),  //keeps (c, b) - opcode 3
                (2, 3, 5),  //keeps (b, a) - opcode 4
                (6, 7, 8)   //shares nothing, so another restart
            };

            ModelGeometry geometry = Geometry(9, faces);
            ModelFile rebuilt = ModelGeometryEncoder.Rebuild(Skeleton(ModelEncoding.Newer,
                SentinelModelId, 9, faces.Length), geometry);

            Assert.Equal(new byte[] { 1, 2, 3, 4, 1 }, rebuilt.FaceOpcodes);

            ModelFile again = ModelCodec.Decode(ModelCodec.Encode(rebuilt).ToArray(), SentinelModelId);
            ModelGeometry decoded = ModelGeometry.FromFile(again);
            Assert.Equal(geometry.FaceA, decoded.FaceA);
            Assert.Equal(geometry.FaceB, decoded.FaceB);
            Assert.Equal(geometry.FaceC, decoded.FaceC);
        }

        /// <summary>
        ///     An axis that does not move between two vertices stores neither a mask bit nor a byte.
        /// </summary>
        /// <remarks>
        ///     The decoder only adds a delta when the bit is set, so a zero delta is representable
        ///     both ways. Clearing the bit is the smaller of the two and is what the rebuild picks;
        ///     the other spelling is exactly why an unchanged mesh keeps its original bytes instead
        ///     of coming through the rebuild at all.
        /// </remarks>
        [Fact]
        public void ARebuild_ClearsTheMaskBitForAnAxisThatDidNotMove()
        {
            var geometry = new ModelGeometry(
                new[] { 0, 5, 5 }, new[] { 0, 0, 7 }, new[] { 0, 0, 0 },
                new[] { 0 }, new[] { 1 }, new[] { 2 });

            ModelFile rebuilt = ModelGeometryEncoder.Rebuild(
                Skeleton(ModelEncoding.Newer, SentinelModelId, 3, 1), geometry);

            Assert.Equal(new byte[] { 0, 0x1, 0x2 }, rebuilt.VertexFlags);
            Assert.Single(rebuilt.VertexDeltasX);
            Assert.Single(rebuilt.VertexDeltasY);
            Assert.Empty(rebuilt.VertexDeltasZ);
            Assert.Equal(1, rebuilt.VertexXLength);
            Assert.Equal(1, rebuilt.VertexYLength);
            Assert.Equal(0, rebuilt.VertexZLength);
        }

        /// <summary>
        ///     A rebuild drops the unread remainder of the blocks it rewrites, and says so in the
        ///     declared lengths.
        /// </summary>
        /// <remarks>
        ///     The footer's declared block sizes are not derived from the content, and models do
        ///     ship declaring more than the client consumes. Carrying that slack forward past a
        ///     rebuilt block would leave the length field describing bytes that are no longer there.
        /// </remarks>
        [Fact]
        public void ARebuild_DropsTheSlackOfTheBlocksItRewrites()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            file.SlackVertexX = new byte[] { 9, 9, 9 };
            file.VertexXLength += 3;
            file.SlackFaceIndex = new byte[] { 8 };
            file.FaceIndexLength += 1;

            ModelGeometry geometry = ModelGeometry.FromFile(file);
            ModelFile rebuilt = ModelGeometryEncoder.Rebuild(file, geometry);

            Assert.Empty(rebuilt.SlackVertexX);
            Assert.Empty(rebuilt.SlackFaceIndex);
            Assert.Equal(file.VertexXLength - 3, rebuilt.VertexXLength);
            Assert.Equal(file.FaceIndexLength - 1, rebuilt.FaceIndexLength);

            //The slack was the only thing removed, so the model still reads back.
            ModelFile again = ModelCodec.Decode(ModelCodec.Encode(rebuilt).ToArray(), SentinelModelId);
            Assert.True(geometry.Matches(ModelGeometry.FromFile(again)));
        }

        // ===================================================================
        //  Preservation
        // ===================================================================

        /// <summary>
        ///     A rebuild replaces the four geometry blocks and hands back every other field of
        ///     <see cref="ModelFile"/> as the same object.
        /// </summary>
        /// <remarks>
        ///     Driven off reflection rather than a written-out list, because the failure this is
        ///     defending against is a field being <em>added</em> to the model and forgotten in the
        ///     preservation copy. A hand-written list of fields to check would be updated by exactly
        ///     the person who already remembered, and never by the person who did not. Reference
        ///     equality is the assertion because "preserved" should mean the same array, not a value
        ///     that happens to match.
        /// </remarks>
        [Fact]
        public void AnImport_PreservesEveryFieldExceptTheGeometryBlocks()
        {
            ModelFile file = Fully(SentinelModelId);
            string edited = Translate(ModelObjExporter.Export(file).ObjText, 1, 0, 0);

            ModelImportResult result = ModelObjImporter.Import(file, edited);
            Assert.True(result.Succeeded, result.Message);

            var missed = new List<string>();
            foreach (PropertyInfo property in Settable())
            {
                if (GeometryFields.Contains(property.Name))
                    continue;

                object before = property.GetValue(file);
                object after = property.GetValue(result.Model);

                bool same = before == null ? after == null : ReferenceEquals(before, after) ||
                            (property.PropertyType.IsValueType && Equals(before, after));
                if (!same)
                    missed.Add($"{property.Name} was not preserved");
            }

            Assert.True(missed.Count == 0, string.Join(Environment.NewLine, missed));
        }

        /// <summary>
        ///     The report names every array it kept, including the ones this model does not have.
        /// </summary>
        /// <remarks>
        ///     The viewport cannot be captured on this machine, so what the import did has to be
        ///     legible as text. An absent array is listed rather than skipped: "this model has no
        ///     per-face alphas" and "its alphas were kept" are different facts, and a report that
        ///     shows only what exists gives no way to tell them apart.
        /// </remarks>
        [Fact]
        public void AnImport_ReportsWhatItKeptAndWhatItIgnored()
        {
            ModelFile file = Fully(SentinelModelId);
            string edited = Translate(ModelObjExporter.Export(file).ObjText, 1, 0, 0);

            ModelImportResult result = ModelObjImporter.Import(file, edited);
            Assert.True(result.Succeeded, result.Message);

            foreach (string field in new[]
                     {
                         "face colours", "face render types", "face priorities", "face alphas",
                         "face skin groups", "vertex skin groups", "face texture ids",
                         "texture coordinate indices", "textured triangles", "particle emitters",
                         "particle effectors", "billboard bonds"
                     })
            {
                Assert.Contains(result.Entries, entry =>
                    entry.Field == field && entry.Disposition == ModelImportDisposition.Preserved);
            }

            Assert.Contains(result.Entries, entry =>
                entry.Disposition == ModelImportDisposition.Replaced &&
                entry.Field == "vertex coordinates");
            Assert.Contains(result.Entries, entry =>
                entry.Disposition == ModelImportDisposition.Ignored &&
                entry.Field == "OBJ materials");

            //An array this model does not carry is still accounted for.
            Assert.Contains(result.Entries, entry =>
                entry.Field == "particle effectors" && entry.Detail.Contains("absent"));
        }

        // ===================================================================
        //  Refusals
        // ===================================================================

        /// <summary>
        ///     A face count that moved is refused, in either direction.
        /// </summary>
        /// <remarks>
        ///     There is no model whose per-face arrays survive it. Colours are not optional, so even
        ///     the plainest model would need one invented for a new face, and nothing says which of
        ///     the old faces a new one inherits from.
        /// </remarks>
        /// <param name="removeInstead">Whether to drop a face rather than add one.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AFaceCountThatMoved_IsRefused(bool removeInstead)
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            string text = ModelObjExporter.Export(file).ObjText;
            text = removeInstead
                ? string.Join("\n", text.Split('\n').Where(line => !line.StartsWith("f 1 2 3")))
                : text + "\nf 1 2 3\n";

            ModelImportResult result = ModelObjImporter.Import(file, text);

            Assert.False(result.Succeeded);
            Assert.Null(result.Model);
            Assert.Contains("faces", result.Message);
            Assert.Contains(result.Entries, entry =>
                entry.Disposition == ModelImportDisposition.Refused);
        }

        /// <summary>
        ///     A vertex count that moved is refused when something else addresses a vertex, and
        ///     allowed when nothing does.
        /// </summary>
        /// <remarks>
        ///     Three arrays index a vertex: the per-vertex skin groups, the three reference vertices
        ///     of every textured triangle, and a particle effector's anchor. An emitter anchors to a
        ///     face instead and so does not block anything, which is the distinction the model
        ///     format makes and the one an import has to make with it.
        /// </remarks>
        [Fact]
        public void AVertexCountThatMoved_IsRefusedOnlyWhenSomethingIndexesAVertex()
        {
            ModelFile plain = Cube(ModelEncoding.Newer, SentinelModelId);
            ModelImportResult allowed = ModelObjImporter.Import(plain, WithSpareVertex(plain));
            Assert.True(allowed.Succeeded, allowed.Message);
            Assert.Equal(plain.VertexCount + 1, allowed.Model.VertexCount);

            ModelFile skinned = Cube(ModelEncoding.Newer, SentinelModelId);
            skinned.VertexSkinFlag = 1;
            skinned.VertexSkins = Enumerable.Range(0, skinned.VertexCount)
                .Select(i => new StoredSmart(0, JagStream.SmartWidth.OneByte)).ToArray();

            ModelImportResult refused = ModelObjImporter.Import(skinned, WithSpareVertex(skinned));
            Assert.False(refused.Succeeded);
            Assert.Contains("skin groups", refused.Message);
        }

        /// <summary>A face naming a vertex the mesh does not hold is refused.</summary>
        /// <remarks>
        ///     The client indexes its coordinate arrays with these directly, so an out-of-range face
        ///     reads whatever follows the array rather than failing.
        /// </remarks>
        [Fact]
        public void AFaceNamingAVertexThatDoesNotExist_IsRefused()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            ModelImportResult result = ModelObjImporter.Import(file,
                ModelObjExporter.Export(file).ObjText.Replace("f 1 2 3", "f 1 2 9"));

            Assert.False(result.Succeeded);
            Assert.Contains("only 3 had been declared", result.Message);
        }

        /// <summary>
        ///     Two vertices further apart than a signed smart can express are refused rather than
        ///     truncated.
        /// </summary>
        [Fact]
        public void ADeltaTooLargeForASignedSmart_IsRefused()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            ModelImportResult result = ModelObjImporter.Import(file,
                ModelObjExporter.Export(file).ObjText.Replace("v 10 -20 -30", "v 900000 -20 -30"));

            Assert.False(result.Succeeded);
            Assert.Contains("signed smart carries", result.Message);
        }

        /// <summary>
        ///     A new-protocol model whose strip opcodes carry the trailing block's per-face flag is
        ///     refused rather than having those bits dropped.
        /// </summary>
        /// <remarks>
        ///     Only the low three bits are the opcode there (Model.java:1071). No model in either
        ///     cache sets the rest, and <see cref="ModelCodec"/> refuses the flag that would give
        ///     them meaning, so a rebuild that quietly zeroed them would be inventing a file nobody
        ///     can check.
        /// </remarks>
        [Fact]
        public void ANewProtocolOpcodeCarryingFlagBits_IsRefused()
        {
            ModelFile file = Cube(ModelEncoding.NewProtocol, NewProtocolModelId);
            file.FaceOpcodes = (byte[]) file.FaceOpcodes.Clone();
            file.FaceOpcodes[0] |= 0x8;

            ModelImportResult result = ModelObjImporter.Import(file,
                Translate(ModelObjExporter.Export(file).ObjText, 1, 0, 0));

            Assert.False(result.Succeeded);
            Assert.Contains("per-face flag for the trailing block", result.Message);
        }

        // ===================================================================
        //  The OBJ parser
        // ===================================================================

        /// <summary>Reads the face spellings the format allows, and the relative index form.</summary>
        /// <remarks>
        ///     A negative index counts back from the end of the list <em>as it stands at that
        ///     line</em>, not from the end of the file, which is what makes it resolvable in one
        ///     pass. Blender writes them when asked to.
        /// </remarks>
        [Fact]
        public void TheParser_ReadsEveryFaceSpellingAndRelativeIndices()
        {
            ObjMesh mesh = ObjParser.Parse(string.Join("\n",
                "# a comment",
                "v 0 0 0",
                "v 1 0 0",
                "v 1 1 0",
                "vt 0 0",
                "vt 1 0",
                "vt 1 1",
                "vn 0 1 0",
                "f 1 2 3",
                "f 1/1 2/2 3/3",
                "f 1//1 2//1 3//1",
                "f 1/1/1 2/2/1 3/3/1",
                "f -3 -2 -1"));

            Assert.Equal(3, mesh.Positions.Count);
            Assert.Equal(3, mesh.TexCoords.Count);
            Assert.Equal(1, mesh.NormalCount);
            Assert.Equal(5, mesh.Faces.Count);

            foreach (ObjFace face in mesh.Faces)
            {
                Assert.Equal(0, face.A);
                Assert.Equal(1, face.B);
                Assert.Equal(2, face.C);
            }

            Assert.Equal(-1, mesh.Faces[0].TexA);
            Assert.Equal(0, mesh.Faces[1].TexA);
            Assert.Equal(-1, mesh.Faces[2].TexA);
            Assert.Equal(2, mesh.Faces[3].TexC);
        }

        /// <summary>A polygon becomes a fan, and the count of them travels back to the user.</summary>
        /// <remarks>
        ///     Fanning changes the face count, which is the one thing an import refuses, so a user
        ///     staring at "the face count changed" needs to be told that their quads are why.
        /// </remarks>
        [Fact]
        public void TheParser_FansAPolygonAndSaysItDid()
        {
            ObjMesh mesh = ObjParser.Parse(string.Join("\n",
                "v 0 0 0", "v 1 0 0", "v 1 1 0", "v 0 1 0", "f 1 2 3 4"));

            Assert.Equal(2, mesh.Faces.Count);
            Assert.Equal(1, mesh.TriangulatedPolygons);
            Assert.Equal(0, mesh.Faces[0].A);
            Assert.Equal(1, mesh.Faces[0].B);
            Assert.Equal(2, mesh.Faces[0].C);
            Assert.Equal(0, mesh.Faces[1].A);
            Assert.Equal(2, mesh.Faces[1].B);
            Assert.Equal(3, mesh.Faces[1].C);
        }

        /// <summary>A malformed line this understands is a failure, not something to read past.</summary>
        /// <param name="text">The file.</param>
        /// <param name="expected">Part of the message it must produce.</param>
        [Theory]
        [InlineData("v 0 0\nf 1 1 1", "needs three coordinates")]
        [InlineData("v 0 0 0\nv x 0 0\nf 1 1 1", "where a number belongs")]
        [InlineData("v 0 0 0\nf 1 2", "at least three corners")]
        [InlineData("v 0 0 0\nf 0 0 0", "indices start at 1")]
        [InlineData("v 0 0 0\nf 1 1 7", "only 1 had been declared")]
        public void TheParser_RefusesALineItUnderstandsAndCannotRead(string text, string expected)
        {
            ModelImportException failure =
                Assert.Throws<ModelImportException>(() => ObjParser.Parse(text));
            Assert.Contains(expected, failure.Message);
        }

        /// <summary>
        ///     A coordinate that is not whole is snapped, and how far it moved is reported.
        /// </summary>
        /// <remarks>
        ///     A modeller has no reason to keep vertices on an integer grid, so refusing would make
        ///     the import unusable. Silently rounding without saying so is the other failure, since
        ///     the user then has no way to know their edit was approximated.
        /// </remarks>
        [Fact]
        public void ANonIntegerCoordinate_IsSnappedAndReported()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            ModelImportResult result = ModelObjImporter.Import(file,
                ModelObjExporter.Export(file).ObjText.Replace("v 10 -20 -30", "v 10.6 -20 -30"));

            Assert.True(result.Succeeded, result.Message);
            Assert.True(result.GeometryChanged);
            Assert.Equal(11, ModelGeometry.FromFile(result.Model).X[1]);
            Assert.Contains(result.Entries, entry => entry.Field == "coordinate rounding");
        }

        // ===================================================================
        //  Materials
        // ===================================================================

        /// <summary>
        ///     Faces are grouped by appearance, and a legacy model's packed mask is unpacked first.
        /// </summary>
        /// <remarks>
        ///     On the legacy encoding a textured face's colour word <em>is</em> its texture id, and
        ///     the drawn colour is replaced by the neutral 127 (Model.java:1497-1505). Reading the
        ///     raw word there would produce a material coloured by a texture number.
        /// </remarks>
        [Fact]
        public void Export_GroupsFacesByAppearance_AndUnpacksTheLegacyMask()
        {
            ModelFile file = Cube(ModelEncoding.Legacy, SentinelModelId);
            file.LegacyFaceMaskFlag = 1;
            file.FaceTypeBytes = new byte[] { 0x2 };
            file.FaceColours = new ushort[] { 41 };

            ObjDocument document = ModelObjExporter.Export(file);

            Assert.Contains("usemtl tex41_hsl127", document.ObjText);
            Assert.Contains("newmtl tex41_hsl127", document.MaterialText);
            Assert.Contains("# index 9 texture 41", document.MaterialText);
        }

        /// <summary>
        ///     A face's stored alpha is transparency, so the material's dissolve is its complement.
        /// </summary>
        /// <remarks>
        ///     The client packs the byte as <c>255 - alpha</c> into the ARGB word
        ///     (Renderable_Sub2.java:542), so a stored 0 is fully opaque. Writing it straight
        ///     through would make every opaque face invisible in a modeller.
        /// </remarks>
        [Fact]
        public void Export_TreatsAStoredAlphaAsTransparencyRatherThanOpacity()
        {
            ModelFile file = Cube(ModelEncoding.Newer, SentinelModelId);
            file.AlphaFlag = 1;
            //Whole lines, because "Kd 0.5 ..." also contains "d 0".
            file.FaceAlphas = new byte[] { 0 };
            Assert.Contains("\nd 1\n", ModelObjExporter.Export(file).MaterialText);

            file.FaceAlphas = new byte[] { 255 };
            Assert.Contains("\nd 0\n", ModelObjExporter.Export(file).MaterialText);
        }

        // ===================================================================
        //  Fixtures
        // ===================================================================

        /// <summary>The four blocks a rebuild is allowed to replace, and the fields describing them.</summary>
        private static readonly HashSet<string> GeometryFields = new HashSet<string>
        {
            nameof(ModelFile.VertexCount),
            nameof(ModelFile.VertexFlags),
            nameof(ModelFile.VertexDeltasX),
            nameof(ModelFile.VertexDeltasY),
            nameof(ModelFile.VertexDeltasZ),
            nameof(ModelFile.VertexXLength),
            nameof(ModelFile.VertexYLength),
            nameof(ModelFile.VertexZLength),
            nameof(ModelFile.SlackVertexX),
            nameof(ModelFile.SlackVertexY),
            nameof(ModelFile.SlackVertexZ),
            nameof(ModelFile.FaceOpcodes),
            nameof(ModelFile.FaceIndexDeltas),
            nameof(ModelFile.FaceIndexLength),
            nameof(ModelFile.SlackFaceIndex)
        };

        /// <summary>Every property of a model that holds state, rather than deriving it.</summary>
        private static IEnumerable<PropertyInfo> Settable()
        {
            return typeof(ModelFile)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetSetMethod(true) != null);
        }

        /// <summary>A three-vertex, one-face model in the requested layout.</summary>
        /// <remarks>
        ///     Built through the production encoder rather than from literal bytes, so the fixture
        ///     cannot drift away from the codec. The coordinates are deliberately not on any grid,
        ///     so a lost sign or a stray shift shows up as a wrong number rather than as a zero.
        /// </remarks>
        /// <param name="encoding">The layout to build.</param>
        /// <param name="modelId">The model id, which selects the new-protocol layout.</param>
        /// <returns>A model that encodes and decodes.</returns>
        private static ModelFile Cube(ModelEncoding encoding, int modelId)
        {
            var geometry = new ModelGeometry(
                new[] { 0, 10, 40 }, new[] { 0, 20, 50 }, new[] { 0, 30, 60 },
                new[] { 0 }, new[] { 1 }, new[] { 2 });

            return ModelGeometryEncoder.Rebuild(Skeleton(encoding, modelId, 3, 1), geometry);
        }

        /// <summary>A model carrying one of every optional array, for the preservation checks.</summary>
        private static ModelFile Fully(int modelId)
        {
            ModelFile file = Cube(ModelEncoding.Newer, modelId);
            file.Flags = 0x1 | 0x2 | 0x4;
            file.FaceTypeBytes = new byte[] { 0 };
            file.PriorityFlag = 255;
            file.FacePriorities = new byte[] { 3 };
            file.AlphaFlag = 1;
            file.FaceAlphas = new byte[] { 20 };
            file.FaceSkinFlag = 1;
            file.FaceSkins = new[] { new StoredSmart(4, JagStream.SmartWidth.OneByte) };
            file.Emitters = new[] { new ModelParticleEmitter(7, 0) };
            file.Effectors = Array.Empty<ModelParticleEffector>();
            file.Bonds = new[]
            {
                new ModelBond(9, 0, new StoredSmart(1, JagStream.SmartWidth.OneByte), 2)
            };
            return file;
        }

        /// <summary>
        ///     A model with every non-geometry field filled in and nothing in the geometry blocks,
        ///     ready for <see cref="ModelGeometryEncoder.Rebuild"/> to fill them.
        /// </summary>
        private static ModelFile Skeleton(ModelEncoding encoding, int modelId, int vertexCount,
            int faceCount)
        {
            return new ModelFile
            {
                Encoding = encoding,
                ModelId = modelId,
                VertexCount = vertexCount,
                FaceCount = faceCount,
                TexturedFaceCount = 0,
                FormatType = encoding == ModelEncoding.NewProtocol ? 15 : 12,
                Header = encoding == ModelEncoding.NewProtocol ? new byte[] { 1, 0, 15 } : null,
                Sentinel = encoding == ModelEncoding.Newer ? new byte[] { 0xFF, 0xFF } : null,
                TextureTypes = encoding == ModelEncoding.Legacy ? null : Array.Empty<byte>(),
                VertexFlags = new byte[vertexCount],
                FaceOpcodes = new byte[faceCount],
                FaceColours = new ushort[faceCount],
                VertexDeltasX = Array.Empty<StoredSmart>(),
                VertexDeltasY = Array.Empty<StoredSmart>(),
                VertexDeltasZ = Array.Empty<StoredSmart>(),
                FaceIndexDeltas = Array.Empty<StoredSmart>(),
                TextureCoords = Array.Empty<StoredSmart>(),
                SlackVertexX = Array.Empty<byte>(),
                SlackVertexY = Array.Empty<byte>(),
                SlackVertexZ = Array.Empty<byte>(),
                SlackFaceIndex = Array.Empty<byte>(),
                SlackTextureCoord = Array.Empty<byte>(),
                SlackTextureScale = Array.Empty<byte>(),
                SlackVertexSkin = encoding == ModelEncoding.NewProtocol ? Array.Empty<byte>() : null,
                SlackFaceSkin = encoding == ModelEncoding.NewProtocol ? Array.Empty<byte>() : null,
                Gap = Array.Empty<byte>(),
                TextureVertexA = Array.Empty<ushort>(),
                TextureVertexB = Array.Empty<ushort>(),
                TextureVertexC = Array.Empty<ushort>(),
                TextureScaleP = Array.Empty<int>(),
                TextureScaleQ = Array.Empty<int>(),
                TextureScaleR = Array.Empty<int>(),
                TextureFieldA = Array.Empty<byte>(),
                TextureFieldB = Array.Empty<byte>(),
                TextureFieldC = Array.Empty<byte>(),
                TextureType2FieldA = Array.Empty<byte>(),
                TextureType2FieldB = Array.Empty<byte>()
            };
        }

        /// <summary>Geometry over <paramref name="vertexCount"/> vertices with the given faces.</summary>
        /// <remarks>Coordinates are a diagonal ramp, so no two vertices coincide.</remarks>
        private static ModelGeometry Geometry(int vertexCount, (int A, int B, int C)[] faces)
        {
            var x = new int[vertexCount];
            var y = new int[vertexCount];
            var z = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                x[i] = i;
                y[i] = i * 2;
                z[i] = i * 3;
            }

            return new ModelGeometry(x, y, z,
                faces.Select(face => face.A).ToArray(),
                faces.Select(face => face.B).ToArray(),
                faces.Select(face => face.C).ToArray());
        }

        /// <summary>Moves every vertex of an OBJ, in the OBJ's own axes.</summary>
        private static string Translate(string objText, int dx, int dy, int dz)
        {
            var moved = new List<string>();
            foreach (string line in objText.Split('\n'))
            {
                if (!line.StartsWith("v ", StringComparison.Ordinal))
                {
                    moved.Add(line);
                    continue;
                }

                string[] parts = line.Split(' ');
                moved.Add("v " + (int.Parse(parts[1]) + dx) + " " +
                          (int.Parse(parts[2]) + dy) + " " + (int.Parse(parts[3]) + dz));
            }

            return string.Join("\n", moved);
        }

        /// <summary>The same OBJ with one more vertex, which no face refers to.</summary>
        private static string WithSpareVertex(ModelFile file)
        {
            return ModelObjExporter.Export(file).ObjText + "\nv 1 2 3\n";
        }

        /// <summary>Every line of an OBJ starting with the given keyword.</summary>
        private static string[] Lines(string objText, string prefix)
        {
            return objText.Split('\n')
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();
        }
    }
}
