using System.Collections.Generic;
using FlashEditor.Definitions.Models.Interchange;
using Xunit;

namespace FlashEditor.Tests.Definitions.Models {
    /// <summary>Reading the diffuse colour of each material out of an MTL.</summary>
    public sealed class MtlParserTests {
        /// <summary>The shape Blender writes, cut down to the lines that matter plus some that do not.</summary>
        private const string Blender =
            "# Blender MTL File: 'axe.blend'\n" +
            "newmtl gold\r\n" +
            "Ns 250.000000\n" +
            "Ka 1.000000 1.000000 1.000000\n" +
            "Kd 0.860000 0.680000 0.310000\n" +
            "d 1.000000\n" +
            "\n" +
            "newmtl  navy \n" +
            "\tKd 0.09 0.13 0.24   # trailing comment\n" +
            "newmtl untinted\n" +
            "illum 1\n";

        [Fact]
        public void Parse_ReadsEachMaterialsKd_AsRgbRoundedToTheNearestByte() {
            IReadOnlyDictionary<string, int> kd = MtlParser.Parse(Blender);

            Assert.Equal((219 << 16) | (173 << 8) | 79, kd["gold"]);
            Assert.Equal((23 << 16) | (33 << 8) | 61, kd["navy"]);
        }

        [Fact]
        public void Parse_LeavesOutAMaterialThatStatesNoKd() {
            Assert.False(MtlParser.Parse(Blender).ContainsKey("untinted"));
        }

        [Fact]
        public void Parse_ClampsKdOutsideZeroToOne() {
            Assert.Equal(0xFF0000, MtlParser.Parse("newmtl hot\nKd 1.5 -0.2 0\n")["hot"]);
        }

        [Fact]
        public void Parse_RefusesAKdWithNoMaterialToBelongTo() {
            var failure = Assert.Throws<ModelImportException>(() => MtlParser.Parse("Kd 1 1 1\n"));
            Assert.Contains("newmtl", failure.Message);
        }

        [Fact]
        public void Parse_RefusesAKdThatIsNotThreeNumbers() {
            var failure = Assert.Throws<ModelImportException>(() => MtlParser.Parse("newmtl a\nKd 1 x 1\n"));
            Assert.Contains("Line 2", failure.Message);
        }
    }
}
