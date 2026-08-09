using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.ClientScripts;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every CS2 client script the real revision-639 cache declares and requires each one
    ///     to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 12 is addressed from both ends - the last two bytes say how long the switch section
    ///     is, which fixes where the footer starts, which is where the instruction stream stops - so
    ///     the harness's usual over-read probe does not apply here. Padding a record moves the very
    ///     trailer the decoder reads, so appending sentinel bytes produces a different script rather
    ///     than a detectable overshoot. The exactness those sweeps normally assert is enforced inside
    ///     <see cref="ClientScriptDefinition.Decode"/> instead: the switch blocks must end exactly on
    ///     the trailer, the instruction stream exactly on the footer, and the footer's own
    ///     instruction count must equal the number decoded. A record failing any of the three throws,
    ///     which is why "every script decodes" is a real statement about the operand-width rule here
    ///     and not the weak claim it is on an opcode-stream index.
    ///     <para>
    ///     The sweep enumerates what the reference table declares, as every other index does. Two
    ///     groups of the repack sit in its idx file and in no table; they are reported by
    ///     <see cref="TheUndeclaredGroups_AreReadAndReportedRatherThanSwept"/> rather than folded
    ///     into the population, because the client resolves every script through the table and can
    ///     never load them.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheClientScriptTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Opcodes listed in the histogram the test prints for the future disassembler.</summary>
        private const int ReportedOpcodes = 40;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheClientScriptTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups index 12's reference table declares, one script each.</summary>
        /// <remarks>
        ///     Read from the table rather than written down, so the sweeps assert a relationship -
        ///     every declared script was read and re-encoded - that holds in any 639 cache.
        /// </remarks>
        private int ScriptsInCache => _fixture.DeclaredGroups(RSConstants.CLIENT_SCRIPTS_INDEX);

        /// <summary>
        ///     The client-script index bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every group, not the 250-group sample: the whole index decompresses to under three
        ///     megabytes, and "every script in the cache re-encodes to its stored bytes" is not a
        ///     claim a sample can make. <c>NotOpcodeTerminated</c> is mandatory rather than tidy -
        ///     the opcode-boundary trace it switches off decodes one prefix per byte, and the
        ///     largest script here is over a hundred kilobytes, so leaving it on would turn a single
        ///     failure into a run that never finishes.
        /// </remarks>
        /// <returns>A sweep over every script the cache declares.</returns>
        private DefinitionSweep<ClientScriptDefinition> Sweep()
        {
            return new DefinitionSweep<ClientScriptDefinition>(_fixture, _output,
                RSConstants.CLIENT_SCRIPTS_INDEX,
                new DefinitionCodec<ClientScriptDefinition>("client script",
                    (id, stream) => new ClientScriptDefinition { Id = id }.Decode(stream),
                    script => script.Encode(),
                    script => script.Instructions.Select(instruction => instruction.Opcode)
                        .Distinct().OrderBy(opcode => opcode)))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     Every declared script decodes, which on this format means every offset it states
        ///     agrees with every other.
        /// </summary>
        [RealCacheFact]
        public void EveryClientScript_DecodesWithItsOwnOffsetsAgreeing()
        {
            DefinitionSweepResult swept = Sweep().AssertEveryRecordDecodes();

            Assert.True(ScriptsInCache > 0, "index 12's reference table declares no groups at all");
            Assert.Equal(ScriptsInCache, swept.Records);
            Assert.Equal(ScriptsInCache, swept.Groups);
            Assert.Equal(ScriptsInCache, swept.Passed);
        }

        /// <summary>Every declared script re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The primary regression detector for this index. Every field the encoder writes is
        ///     derived from the decoded content - the instruction count, the switch-section length,
        ///     the footer's position - so this sweep is what proves that derivation is right rather
        ///     than assumed, and it is the only thing standing between an edit to one script and a
        ///     rewrite of every archive packed in the same reference table.
        /// </remarks>
        [RealCacheFact]
        public void EveryClientScript_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(ScriptsInCache, swept.Records);
            Assert.Equal(ScriptsInCache, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>
        ///     Whatever the encoder writes, its own decoder reads back and writes out unchanged.
        /// </summary>
        /// <remarks>
        ///     Independent of byte identity against the cache, and written out here rather than
        ///     taken from the harness because the harness re-decodes a padded copy - which this
        ///     format cannot survive, since the padding moves the trailer. This is the property the
        ///     save path depends on once a script has actually been edited.
        /// </remarks>
        [RealCacheFact]
        public void EveryClientScript_EncodeIsAFixedPointOfDecode()
        {
            var failures = new List<string>();
            int stable = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, script) =>
            {
                byte[] first = script.Encode().ToArray();

                byte[] second;
                try
                {
                    second = new ClientScriptDefinition { Id = record.Id }
                        .Decode(new JagStream(first)).Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"client script {record.Id}: re-decoding the encoded stream threw " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (first.AsSpan().SequenceEqual(second))
                {
                    stable++;
                    return;
                }

                failures.Add($"client script {record.Id}: encoder output re-encoded to {second.Length} " +
                             $"bytes from {first.Length}");
            });

            _output.WriteLine($"{stable} of {swept.Records} client scripts survived an " +
                              "encode-decode-encode cycle");

            Assert.Empty(failures);
            Assert.Equal(ScriptsInCache, stable);
        }

        /// <summary>
        ///     Index 12 is one script per group and one file per group, so a script id is a group id.
        /// </summary>
        /// <remarks>
        ///     The addressing <see cref="CacheAddressing"/> records for this index, asserted against
        ///     the table rather than assumed. Both client readers reach the same single-file
        ///     accessor, so a group holding anything else could not be loaded at all.
        /// </remarks>
        [RealCacheFact]
        public void EveryGroup_HoldsExactlyOneFileWhoseIdIsZero()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.CLIENT_SCRIPTS_INDEX);
            var wrong = new List<string>();

            foreach (KeyValuePair<int, RSArchiveEntry> entry in table.GetArchiveEntries())
            {
                int[] fileIds = entry.Value.GetValidFileIds();
                if (fileIds.Length != 1 || fileIds[0] != 0)
                    wrong.Add($"group {entry.Key} declares [{string.Join(",", fileIds)}]");
            }

            Assert.True(ScriptsInCache > 0, "index 12's reference table declares no groups at all");
            Assert.Empty(wrong);
            Assert.Equal(ScriptsInCache, _fixture.DeclaredFiles(RSConstants.CLIENT_SCRIPTS_INDEX));
            Assert.Equal(CacheIdShape.GroupPerId,
                CacheAddressing.For(RSConstants.CLIENT_SCRIPTS_INDEX).Shape);
        }

        /// <summary>
        ///     What index 12 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     The opcode histogram is the point of this test. It is what sizes the disassembler that
        ///     a usable script tab needs, and it is measured rather than taken from a document -
        ///     roughly 580 distinct opcodes across the three dispatchers in <c>Class247</c>, which is
        ///     why the codec ships without one.
        ///     <para>
        ///     Three of the figures decide how much of the codec the sweeps can defend at all, and
        ///     all three are zero: no script carries a leading name, none stores its empty switch
        ///     section without a count byte, and no string operand carries a byte the cp1252 table
        ///     cannot round trip. Those branches rest entirely on
        ///     <c>ClientScriptDefinitionCodecTests</c>. If a repack ever introduces one, this
        ///     assertion is what says so, and the codec already handles it.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheClientScriptIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            var opcodes = new SortedDictionary<int, int>();
            long instructions = 0;
            long integerOperands = 0;
            long byteOperands = 0;
            long textOperands = 0;
            long emptyTextOperands = 0;
            long unmappableTextBytes = 0;
            long highTextBytes = 0;
            int namedScripts = 0;
            int scriptsOmittingTheSwitchCountByte = 0;
            int scriptsWithASwitchBlock = 0;
            long switchBlocks = 0;
            long switchCases = 0;
            int parametersExceedingLocals = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, script) =>
            {
                if (script.Name != null)
                    namedScripts++;
                if (script.OmitsSwitchBlockCount)
                    scriptsOmittingTheSwitchCountByte++;
                if (script.SwitchBlocks.Count > 0)
                {
                    scriptsWithASwitchBlock++;
                    switchBlocks += script.SwitchBlocks.Count;
                    foreach (ClientScriptSwitchBlock block in script.SwitchBlocks)
                        switchCases += block.Cases.Count;
                }

                //The client fills the first n slots of an array it sized from the other field, so a
                //script wanting more parameters than locals could not be called at all.
                if (script.IntegerParameterCount > script.IntegerLocalCount ||
                    script.StringParameterCount > script.StringLocalCount)
                {
                    parametersExceedingLocals++;
                }

                foreach (ClientScriptInstruction instruction in script.Instructions)
                {
                    instructions++;
                    opcodes.TryGetValue(instruction.Opcode, out int seen);
                    opcodes[instruction.Opcode] = seen + 1;

                    switch (instruction.OperandKind)
                    {
                        case ClientScriptOperandKind.Text:
                            textOperands++;
                            byte[] stored = instruction.TextOperandBytes;
                            if (stored.Length == 0)
                                emptyTextOperands++;
                            foreach (byte value in stored)
                            {
                                if (value < 0x80)
                                    continue;
                                highTextBytes++;
                                if (!RoundTripsThroughCp1252(value))
                                    unmappableTextBytes++;
                            }
                            break;

                        case ClientScriptOperandKind.Byte:
                            byteOperands++;
                            break;

                        default:
                            integerOperands++;
                            break;
                    }
                }
            });

            Assert.NotEmpty(opcodes);
            ReportHistogram(opcodes, instructions);

            //Relationships first: these hold in any 639 cache and are what defends one this project
            //has never seen.
            Assert.True(ScriptsInCache > 0, "index 12's reference table declares no groups at all");
            Assert.Equal(ScriptsInCache, swept.Records);
            Assert.Equal(instructions, integerOperands + byteOperands + textOperands);
            Assert.Equal(instructions, opcodes.Values.Sum(count => (long) count));
            Assert.True(switchBlocks >= scriptsWithASwitchBlock,
                "a script counted as having a switch block must hold at least one");
            Assert.Equal(0, parametersExceedingLocals);

            //The three branches no shipped record exercises, so a future cache that does is loud.
            Assert.Equal(0, namedScripts);
            Assert.Equal(0, scriptsOmittingTheSwitchCountByte);
            Assert.Equal(0L, unmappableTextBytes);

            //And the populations, which belong to a cache rather than to build 639.
            RealCacheProfile profile = _fixture.Profile;
            profile.AssertCensus(_output, "clientscript.instructions", instructions);
            profile.AssertCensus(_output, "clientscript.operand.integer", integerOperands);
            profile.AssertCensus(_output, "clientscript.operand.byte", byteOperands);
            profile.AssertCensus(_output, "clientscript.operand.text", textOperands);
            profile.AssertCensus(_output, "clientscript.emptyTextOperands", emptyTextOperands);
            profile.AssertCensus(_output, "clientscript.highTextBytes", highTextBytes);
            profile.AssertCensus(_output, "clientscript.scriptsWithASwitchBlock", scriptsWithASwitchBlock);
            profile.AssertCensus(_output, "clientscript.switchBlocks", switchBlocks);
            profile.AssertCensus(_output, "clientscript.switchCases", switchCases);
            profile.AssertCensus(_output, "clientscript.distinctOpcodes", opcodes.Count);
            profile.AssertCensus(_output, "clientscript.maxOpcode", opcodes.Keys.Last());
        }

        /// <summary>
        ///     Every branch and every switch arm in the index lands on a real instruction.
        /// </summary>
        /// <remarks>
        ///     The assertion that settles the branch arithmetic, and it has no <c>or</c> in it: a
        ///     target is in range or the sweep fails. <c>Class247.java:7779</c> advances the counter
        ///     with <c>OPCODE = is[++current]</c> before dispatching and each branch arm then adds the
        ///     delta, so the next instruction is at <c>position + 1 + delta</c>.
        ///     <para>
        ///     <b>The +1 is load bearing and this sweep is what proves it.</b> Measured over the
        ///     vanilla capture, all 42,884 branches in the index are in range under that reading and
        ///     exactly one is not under <c>position + delta</c>: script 686's unconditional jump at
        ///     position 8 has a delta of -9 in a 13-instruction script, which is a loop back to
        ///     instruction 0 correctly and instruction -1 otherwise. A single witness rather than an
        ///     aggregate, which is the standard this cache demands - the 11,962 switch arms do not
        ///     discriminate between the two readings at all, so they could never have settled it.
        ///     </para>
        ///     <para>
        ///     Both figures are counted rather than written down, and the relationship asserted is
        ///     that every branch resolved. A cache holding different scripts still has to pass.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryJumpTarget_LandsOnAnInstructionOfItsOwnScript()
        {
            long branches = 0;
            long switchArms = 0;
            long unresolvable = 0;
            long survivingWithoutThePlusOne = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, script) =>
            {
                int count = script.Instructions.Count;
                unresolvable += ClientScriptDisassembly.Of(script).UnresolvableTargets;

                for (int position = 0; position < count; position++)
                {
                    ClientScriptInstruction instruction = script.Instructions[position];

                    if (ClientScriptOpcodes.IsBranch(instruction.Opcode))
                    {
                        branches++;
                        int plain = position + instruction.IntegerOperand;
                        if (plain >= 0 && plain < count)
                            survivingWithoutThePlusOne++;
                        continue;
                    }

                    if (instruction.Opcode != ClientScriptOpcodes.SwitchOpcode)
                        continue;

                    int block = instruction.IntegerOperand;
                    if (block >= 0 && block < script.SwitchBlocks.Count)
                        switchArms += script.SwitchBlocks[block].Cases.Count;
                }
            });

            _output.WriteLine($"{branches} branches and {switchArms} switch arms across {swept.Records} " +
                              $"scripts, {unresolvable} landing outside their script");
            _output.WriteLine($"{branches - survivingWithoutThePlusOne} of those branches falsify the " +
                              "reading that omits the +1");

            Assert.Equal(ScriptsInCache, swept.Records);
            Assert.True(branches > 0, "no script in the index branches at all, so nothing was tested");
            Assert.True(switchArms > 0, "no switch block in the index holds an arm, so nothing was tested");
            Assert.Equal(0, unresolvable);

            //The discriminating witness. Without it this sweep would pass under either reading and
            //would be evidence for neither.
            Assert.True(survivingWithoutThePlusOne < branches,
                "every branch is in range under 'position + delta' too, so this sweep no longer " +
                "distinguishes the interpreter's arithmetic from an off-by-one");
        }

        /// <summary>
        ///     Every opcode the interpreter handles in line is named, and the disassembler's coverage
        ///     is reported as a share of instructions.
        /// </summary>
        /// <remarks>
        ///     Two claims, and only the first is an assertion. <b>Every opcode below 100 that this
        ///     cache uses carries a mnemonic</b>, because the in-line chain at
        ///     <c>Class247.java:7781-7988</c> is short enough to have been read whole - so a cache
        ///     turning up a sub-100 opcode the client has no arm for is a real discovery and fails
        ///     here rather than appearing as a blank cell.
        ///     <para>
        ///     The coverage percentage is printed rather than asserted against a floor. A floor would
        ///     be read as a target, and the honest statement is a measurement: naming one more opcode
        ///     in the long tail moves it by hundredths, while the figure that matters - instructions
        ///     rather than distinct opcodes - is already dominated by a set that is complete.
        ///     </para>
        ///     <para>
        ///     Also asserted: nothing in the index uses an opcode at or above 10,000, which
        ///     <c>Class247.java:7997</c> breaks out of the loop for. That is a claim about the data
        ///     rather than about this project, and it is what says the two dispatchers plus the chain
        ///     account for every instruction here.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheDisassembler_NamesEveryInLineOpcodeAndReportsWhatThatCovers()
        {
            long instructions = 0;
            long named = 0;
            long inLine = 0;
            var unnamedBelowOneHundred = new SortedSet<int>();
            var namedOpcodes = new SortedSet<int>();
            var allOpcodes = new SortedSet<int>();
            var undispatched = new SortedSet<int>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, script) =>
            {
                foreach (ClientScriptInstruction instruction in script.Instructions)
                {
                    int opcode = instruction.Opcode;
                    instructions++;
                    allOpcodes.Add(opcode);

                    if (opcode < 100)
                        inLine++;
                    if (opcode >= 10000)
                        undispatched.Add(opcode);

                    if (ClientScriptOpcodes.MnemonicOf(opcode) == null)
                    {
                        if (opcode < 100)
                            unnamedBelowOneHundred.Add(opcode);
                        continue;
                    }

                    named++;
                    namedOpcodes.Add(opcode);
                }
            });

            _output.WriteLine($"{named} of {instructions} instructions named " +
                              $"({100.0 * named / instructions:F2}%), over {namedOpcodes.Count} of the " +
                              $"{allOpcodes.Count} distinct opcodes in use");
            _output.WriteLine($"the in-line chain below 100 is {inLine} instructions " +
                              $"({100.0 * inLine / instructions:F2}%) over " +
                              $"{allOpcodes.Count(opcode => opcode < 100)} opcodes");

            Assert.Equal(ScriptsInCache, swept.Records);
            Assert.True(instructions > 0, "the index decoded no instructions at all");
            Assert.Empty(unnamedBelowOneHundred);
            Assert.Empty(undispatched);

            //Every named instruction is one this table has a row for, so the two counts cannot drift.
            Assert.True(named <= instructions);
            Assert.All(namedOpcodes, opcode => Assert.Contains(opcode, ClientScriptOpcodes.NamedOpcodes));
        }

        /// <summary>
        ///     Groups the idx file holds and the reference table does not are read and reported, not
        ///     swept.
        /// </summary>
        /// <remarks>
        ///     The vanilla capture has none on any index; the repack has two here. They hold real,
        ///     well-formed scripts, so this asserts that they decode and re-encode rather than
        ///     merely noting they exist - but they stay out of the population figures, because
        ///     <c>JS5Archive.method2758</c> gates every client read on the reference table and
        ///     nothing in the game can ever reach them.
        /// </remarks>
        [RealCacheFact]
        public void TheUndeclaredGroups_AreReadAndReportedRatherThanSwept()
        {
            RSCache cache = _fixture.OpenCache();
            IReadOnlyList<int> orphans = cache.EnumerateOrphanGroups(RSConstants.CLIENT_SCRIPTS_INDEX);

            IReadOnlyDictionary<int, int[]> recorded = _fixture.Profile.OrphanGroups;
            if (recorded != null)
            {
                recorded.TryGetValue(RSConstants.CLIENT_SCRIPTS_INDEX, out int[] expected);
                Assert.Equal(expected ?? Array.Empty<int>(), orphans.ToArray());
            }

            foreach (int groupId in orphans)
            {
                byte[] stored = _fixture.RawContainer(RSConstants.CLIENT_SCRIPTS_INDEX, groupId);
                Assert.NotNull(stored);

                RSContainer container =
                    _fixture.TryDecodeContainer(RSConstants.CLIENT_SCRIPTS_INDEX, groupId, stored);
                Assert.NotNull(container);

                //One file per group across the whole index, so the orphans are unpacked the same
                //way the declared groups are.
                RSArchive archive = RSArchive.Decode(container.GetStream(), new[] { 0 });
                byte[] bytes = archive.GetFile(0).ToArray();

                var script = new ClientScriptDefinition { Id = groupId }.Decode(new JagStream(bytes));

                _output.WriteLine($"undeclared group {groupId}: {bytes.Length} bytes, " +
                                  $"{script.Instructions.Count} instructions, " +
                                  $"{script.SwitchBlocks.Count} switch blocks, " +
                                  $"{script.IntegerParameterCount} integer and " +
                                  $"{script.StringParameterCount} string parameters, " +
                                  $"name {(script.Name == null ? "absent" : "'" + script.Name + "'")}");

                Assert.Equal(bytes, script.Encode().ToArray());
            }

            _output.WriteLine($"{orphans.Count} index-12 groups sit in the idx file and in no " +
                              "reference table, so the sweep above never sees them");
        }

        /// <summary>
        ///     The bytes <c>ClientScriptDefinitionCodecTests</c> asserts against are still what the
        ///     cache holds.
        /// </summary>
        /// <remarks>
        ///     Without this the offline tests pin the codec to a literal nobody can check.
        /// </remarks>
        [RealCacheFact]
        public void TheCapturedFixture_IsStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();

            byte[] stored = cache.ReadFileBytes(RSConstants.CLIENT_SCRIPTS_INDEX,
                ClientScriptDefinitionCodecTests.CapturedScriptId, 0);

            Assert.Equal(ClientScriptDefinitionCodecTests.CapturedScriptBytes(), stored);
        }

        /// <summary>
        ///     Whether a byte above 0x7F survives the client's modified cp1252 round trip.
        /// </summary>
        /// <remarks>
        ///     0xA0 and above pass through unchanged. In the 0x80-0x9F band five slots are
        ///     unassigned, decode to <c>'?'</c> and re-encode as 0x3F; the rest map to real
        ///     characters and come back. Checked by round-tripping the byte through the production
        ///     readers rather than by restating the table, so the two cannot drift apart.
        /// </remarks>
        /// <param name="value">The stored byte.</param>
        /// <returns>Whether encoding the decoded text reproduces the byte.</returns>
        private static bool RoundTripsThroughCp1252(byte value)
        {
            var stored = new JagStream(new byte[] { value, 0x00 });
            string text = stored.ReadJagexString();

            var written = new JagStream();
            written.WriteJagexString(text);
            byte[] bytes = written.Flip().ToArray();

            return bytes.Length == 2 && bytes[0] == value;
        }

        /// <summary>
        ///     Prints the opcode histogram, which is what sizes the disassembler this codec omits.
        /// </summary>
        /// <param name="opcodes">Occurrences per opcode.</param>
        /// <param name="instructions">Instructions across the whole index.</param>
        private void ReportHistogram(SortedDictionary<int, int> opcodes, long instructions)
        {
            _output.WriteLine($"{instructions} instructions across {opcodes.Count} distinct opcodes, " +
                              $"highest {opcodes.Keys.Last()}");

            //Bucketed by dispatcher, because that is the shape of the work a disassembler faces:
            //Class247's in-line chain below 100, method3148 for 100..4999 and method3156 above.
            Report("in-line chain, opcode < 100", opcodes, opcode => opcode < 100);
            Report("method3148, 100..4999", opcodes, opcode => opcode >= 100 && opcode < 5000);
            Report("method3156, 5000..9999", opcodes, opcode => opcode >= 5000 && opcode < 10000);
            Report("undispatched, 10000 and above", opcodes, opcode => opcode >= 10000);

            string busiest = string.Join(", ", opcodes
                .OrderByDescending(entry => entry.Value)
                .Take(ReportedOpcodes)
                .Select(entry => $"{entry.Key}={entry.Value}"));
            _output.WriteLine($"busiest {ReportedOpcodes} opcodes: {busiest}");
        }

        /// <summary>Prints one dispatcher's share of the histogram.</summary>
        /// <param name="label">What the range is.</param>
        /// <param name="opcodes">Occurrences per opcode.</param>
        /// <param name="inRange">Whether an opcode belongs to this range.</param>
        private void Report(string label, SortedDictionary<int, int> opcodes, Func<int, bool> inRange)
        {
            var range = opcodes.Where(entry => inRange(entry.Key)).ToArray();
            _output.WriteLine($"  {label}: {range.Length} distinct, " +
                              $"{range.Sum(entry => (long) entry.Value)} instructions");
        }
    }
}
