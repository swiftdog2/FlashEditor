using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.ClientScripts;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>Scaffolding: dumps the index-12 opcode histogram to the scratchpad. Removed before commit.</summary>
    [Collection("RealCache")]
    public sealed class TempHistogramDump : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public TempHistogramDump(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Writes opcode counts and per-opcode operand statistics out as CSV.</summary>
        [RealCacheFact]
        public void Dump()
        {
            var opcodes = new SortedDictionary<int, long>();
            var operandMin = new Dictionary<int, int>();
            var operandMax = new Dictionary<int, int>();
            long instructions = 0;

            new DefinitionSweep<ClientScriptDefinition>(_fixture, _output,
                    RSConstants.CLIENT_SCRIPTS_INDEX,
                    new DefinitionCodec<ClientScriptDefinition>("client script",
                        (id, stream) => new ClientScriptDefinition { Id = id }.Decode(stream),
                        script => script.Encode(),
                        script => Array.Empty<int>()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated()
                .ForEachDecoded((record, script) =>
                {
                    foreach (ClientScriptInstruction instruction in script.Instructions)
                    {
                        instructions++;
                        opcodes.TryGetValue(instruction.Opcode, out long seen);
                        opcodes[instruction.Opcode] = seen + 1;

                        if (instruction.OperandKind == ClientScriptOperandKind.Text)
                            continue;

                        int value = instruction.IntegerOperand;
                        if (!operandMin.TryGetValue(instruction.Opcode, out int low) || value < low)
                            operandMin[instruction.Opcode] = value;
                        if (!operandMax.TryGetValue(instruction.Opcode, out int high) || value > high)
                            operandMax[instruction.Opcode] = value;
                    }
                });

            var branchOpcodes = new HashSet<int> { 6, 7, 8, 9, 10, 31, 32, 86, 87 };
            long branches = 0;
            long withPlusOne = 0;
            long withoutPlusOne = 0;
            long switchArms = 0;
            long switchWithPlusOne = 0;
            long switchWithoutPlusOne = 0;

            new DefinitionSweep<ClientScriptDefinition>(_fixture, _output,
                    RSConstants.CLIENT_SCRIPTS_INDEX,
                    new DefinitionCodec<ClientScriptDefinition>("client script",
                        (id, stream) => new ClientScriptDefinition { Id = id }.Decode(stream),
                        script => script.Encode(),
                        script => Array.Empty<int>()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated()
                .ForEachDecoded((record, script) =>
                {
                    int count = script.Instructions.Count;
                    for (int position = 0; position < count; position++)
                    {
                        ClientScriptInstruction instruction = script.Instructions[position];
                        if (!branchOpcodes.Contains(instruction.Opcode))
                            continue;

                        branches++;
                        int delta = instruction.IntegerOperand;
                        bool plusOne = position + 1 + delta >= 0 && position + 1 + delta < count;
                        bool plain = position + delta >= 0 && position + delta < count;
                        if (plusOne)
                            withPlusOne++;
                        if (plain)
                            withoutPlusOne++;
                        if (plusOne != plain)
                            _output.WriteLine($"discriminating branch: script {record.Id} position {position} " +
                                              $"opcode {instruction.Opcode} delta {delta} of {count} instructions, " +
                                              $"+1 target {position + 1 + delta}, plain target {position + delta}");
                    }

                    for (int position = 0; position < count; position++)
                    {
                        ClientScriptInstruction instruction = script.Instructions[position];
                        if (instruction.Opcode != 51)
                            continue;

                        int block = instruction.IntegerOperand;
                        if (block < 0 || block >= script.SwitchBlocks.Count)
                            continue;

                        foreach (ClientScriptSwitchCase arm in script.SwitchBlocks[block].Cases)
                        {
                            switchArms++;
                            if (position + 1 + arm.JumpOffset >= 0 && position + 1 + arm.JumpOffset <= count)
                                switchWithPlusOne++;
                            if (position + arm.JumpOffset >= 0 && position + arm.JumpOffset <= count)
                                switchWithoutPlusOne++;
                        }
                    }
                });

            _output.WriteLine($"branches {branches}: in range with +1 {withPlusOne}, without {withoutPlusOne}");
            _output.WriteLine($"switch arms {switchArms}: in range with +1 {switchWithPlusOne}, " +
                              $"without {switchWithoutPlusOne}");

            string path = Environment.GetEnvironmentVariable("FLASHEDITOR_DUMP_PATH") ??
                          Path.Combine(Path.GetTempPath(), "cs2-histogram.csv");
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("opcode,count,operandMin,operandMax");
                foreach (KeyValuePair<int, long> entry in opcodes)
                {
                    operandMin.TryGetValue(entry.Key, out int low);
                    operandMax.TryGetValue(entry.Key, out int high);
                    writer.WriteLine($"{entry.Key},{entry.Value},{low},{high}");
                }
            }

            _output.WriteLine($"{instructions} instructions, {opcodes.Count} distinct, written to {path}");
            Assert.True(instructions > 0);
        }
    }
}
