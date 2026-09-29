#nullable enable
using System;
using System.Collections.Generic;
using FlashEditor.Definitions.Entities;
using Xunit;

namespace FlashEditor.Tests.Definitions.Entities {
    /// <summary>
    ///     The text the item grid shows for its list-valued fields, and what an edit of it parses to.
    /// </summary>
    /// <remarks>
    ///     Worked examples use item 4718's stored values. Every malformed edit must be refused with
    ///     a reason, because a cell that silently kept part of what was typed is an edit dropped.
    /// </remarks>
    public sealed class ItemFieldTextTests {
        [Fact]
        public void Options_ShowFiveSlots_AndAnEmptySlotIsNoOption() {
            Assert.Equal("|Wield|||drop", ItemFieldText.FormatOptions(new[] { null, "Wield", null, null, "drop" }!));
            Assert.Equal(new[] { null, "Wield", null, "Rub", "drop" },
                ItemFieldText.ParseOptions(" | Wield | | Rub |drop"));
        }

        [Theory]
        [InlineData("Wield|drop")]
        [InlineData("a|b|c|d|e|f")]
        public void Options_WithoutExactlyFiveSlots_AreRefused(string text) {
            var refusal = Assert.Throws<FormatException>(() => ItemFieldText.ParseOptions(text));
            Assert.Contains("5", refusal.Message);
        }

        [Fact]
        public void Recolours_ShowEachPairAsFromToTo_Unsigned() {
            Assert.Equal("6798>5231, 65535>0",
                ItemFieldText.FormatRecolours(new short[] { 6798, -1 }, new short[] { 5231, 0 }));

            (short[] From, short[] To)? parsed = ItemFieldText.ParseRecolours("6798 > 5231,65535>0");
            Assert.NotNull(parsed);
            Assert.Equal(new short[] { 6798, -1 }, parsed!.Value.From);
            Assert.Equal(new short[] { 5231, 0 }, parsed.Value.To);

            Assert.Equal(string.Empty, ItemFieldText.FormatRecolours(null!, null!));
            Assert.Null(ItemFieldText.ParseRecolours("  "));
        }

        [Theory]
        [InlineData("6798")]
        [InlineData("6798>")]
        [InlineData("65536>1")]
        [InlineData("-1>1")]
        [InlineData("a>b")]
        public void Recolours_Malformed_AreRefused(string text) {
            Assert.Throws<FormatException>(() => ItemFieldText.ParseRecolours(text));
        }

        [Fact]
        public void Params_ShowKeyEqualsValue_WithStringsQuoted() {
            var parameters = new SortedDictionary<int, object> {
                [644] = 1580, [686] = 2, [900] = "a \"b\", c\\d"
            };

            string text = ItemFieldText.FormatParams(parameters);

            Assert.Equal("644=1580, 686=2, 900=\"a \\\"b\\\", c\\\\d\"", text);
            Assert.Equal(parameters, ItemFieldText.ParseParams(text));
            Assert.Equal(new SortedDictionary<int, object> { [749] = -5 }, ItemFieldText.ParseParams(" 749 = -5 "));
            Assert.Null(ItemFieldText.ParseParams(""));
        }

        [Theory]
        [InlineData("644")]
        [InlineData("644=")]
        [InlineData("644=x")]
        [InlineData("644=1, 644=2")]
        [InlineData("16777216=1")]
        [InlineData("644=\"open")]
        [InlineData("644=1 686=2")]
        public void Params_Malformed_AreRefused(string text) {
            Assert.Throws<FormatException>(() => ItemFieldText.ParseParams(text));
        }

        [Fact]
        public void Offsets_ShowTheDecodedUnits_AndOnlyAcceptWhatTheOpcodeCanStore() {
            Assert.Equal("12, -44, 24", ItemFieldText.FormatOffset(12, -44, 24));
            Assert.Equal((12, -44, 24), ItemFieldText.ParseOffset("12,-44, 24"));
            Assert.Equal((-512, 508, 0), ItemFieldText.ParseOffset("-512, 508, 0"));

            //Stored as a signed byte shifted left by two, so 13 and 512 cannot be written.
            Assert.Contains("multiple of 4", Assert.Throws<FormatException>(() => ItemFieldText.ParseOffset("13, 0, 0")).Message);
            Assert.Throws<FormatException>(() => ItemFieldText.ParseOffset("512, 0, 0"));
            Assert.Throws<FormatException>(() => ItemFieldText.ParseOffset("4, 0"));
        }
    }
}
