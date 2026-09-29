#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.IO;
using Xunit;

namespace FlashEditor.Tests.Definitions.Entities {
    /// <summary>
    ///     The item grid's columns: every field a new weapon needs is there, and every edit a column
    ///     accepts reaches the encoded record.
    /// </summary>
    /// <remarks>
    ///     The grid once carried Slot and EquipId columns bound to fields the codec never reads or
    ///     writes, so an edit to either was accepted, shown, and lost on save. Each edit here goes
    ///     through the column, the descriptor's encoder and a fresh decode, and is read back through
    ///     the same column.
    /// </remarks>
    public sealed class ItemListDescriptorTests {
        /// <summary>Name "Axe" and nothing else.</summary>
        private static readonly byte[] Axe = { 2, (byte) 'A', (byte) 'x', (byte) 'e', 0, 0 };

        /// <summary>An edit per editable column, each moving the field off what <see cref="Axe"/> holds.</summary>
        private static readonly Dictionary<string, object> Edits = new Dictionary<string, object> {
            ["Name"] = "Saradomin's Will",
            ["InvModel"] = 63614,
            ["ManModel1"] = 63615,
            ["ManModel2"] = 5,
            ["ManModel3"] = 6,
            ["Female1"] = 63615,
            ["Female2"] = 8,
            ["Female3"] = 9,
            ["Zoom"] = 1800,
            ["Rotate1"] = 300,
            ["Rotate2"] = 1500,
            ["Zan2d"] = 20,
            ["OffsetX"] = -3,
            ["OffsetY"] = 7,
            ["ManOffset"] = "4, -8, 12",
            ["FemaleOffset"] = "12, -44, 24",
            ["Ground"] = "||Pick up||",
            ["Inventory"] = "|Wield||Check|drop",
            ["Recolours"] = "100>200, 65535>3",
            ["Params"] = "644=1580, 686=2, 900=\"x\"",
            ["Value"] = 250000,
            ["Stack"] = true,
            ["Members"] = true
        };

        [Fact]
        public void TheGridHasEveryWeaponField_AndNoColumnForAFieldTheCodecDoesNotCarry() {
            string[] headers = new ItemListDescriptor().Columns.Select(column => column.Header).ToArray();

            Assert.Equal(new[] { "ID" }.Concat(Edits.Keys).OrderBy(h => h), headers.OrderBy(h => h));
            Assert.DoesNotContain("Slot", headers);
            Assert.DoesNotContain("EquipId", headers);
        }

        [Fact]
        public void EveryEditableColumn_IsAnEditThatSurvivesEncodeAndDecode() {
            var descriptor = new ItemListDescriptor();

            foreach (DefinitionColumn column in descriptor.Columns.Where(column => column.IsEditable)) {
                Assert.True(Edits.ContainsKey(column.Header), "No edit is listed for column " + column.Header);
                object edit = Edits[column.Header];

                ItemDefinition row = Decode(Axe);
                column.Write!(row, edit);
                byte[] encoded = descriptor.Encode(row).ToArray();

                Assert.False(encoded.AsSpan().SequenceEqual(Axe), column.Header + " changed nothing in the record.");
                Assert.Equal(Normalise(edit), Normalise(column.Read(Decode(encoded))));
            }
        }

        [Theory]
        [InlineData("InvModel", 65536)]
        [InlineData("InvModel", -1)]
        [InlineData("Zoom", 70000)]
        [InlineData("OffsetX", 32768)]
        public void ANumberTheRecordCannotStore_IsRefusedRatherThanWrapped(string header, int value) {
            DefinitionColumn column = new ItemListDescriptor().Columns.Single(c => c.Header == header);
            ItemDefinition row = Decode(Axe);
            object before = column.Read(row)!;

            Assert.ThrowsAny<FormatException>(() => column.Write!(row, value));
            Assert.Equal(before, column.Read(row));
        }

        [Fact]
        public void MalformedText_IsRefusedAndLeavesTheFieldAlone() {
            DefinitionColumn column = new ItemListDescriptor().Columns.Single(c => c.Header == "FemaleOffset");
            ItemDefinition row = Decode(Axe);

            Assert.Throws<FormatException>(() => column.Write!(row, "13, 0, 0"));
            Assert.Equal("0, 0, 0", column.Read(row));
        }

        [Fact]
        public void TryWrite_ReportsARefusedEditInsteadOfThrowing() {
            var columns = new ItemListDescriptor().Columns;
            ItemDefinition row = Decode(Axe);

            string? refusal = columns.Single(c => c.Header == "FemaleOffset").TryWrite(row, "13, 0, 0");
            string? accepted = columns.Single(c => c.Header == "Zoom").TryWrite(row, 1800);

            Assert.Contains("multiple of 4", refusal);
            Assert.Null(accepted);
            Assert.Equal(1800, row.modelZoom);
        }

        private static ItemDefinition Decode(byte[] bytes) => ItemDefinition.DecodeFromStream(new JagStream(bytes));

        private static string? Normalise(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
