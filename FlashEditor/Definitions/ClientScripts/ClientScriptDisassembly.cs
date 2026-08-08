using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     A linear disassembly of one CS2 script: every instruction with its mnemonic where one is
    ///     proven, its byte offset, and its resolved branch target where it has one.
    /// </summary>
    /// <remarks>
    ///     <b>This is a listing, not a control flow graph.</b> Jump targets are resolved and the
    ///     positions that are jumped to are marked, which is what makes a stream of 7,000
    ///     instructions navigable, but nothing here reconstructs basic blocks, loops or
    ///     if/else structure. That is deliberate: a half-correct block decomposition reads as an
    ///     authoritative one, and the two facts a listing needs - where a branch goes, and whether
    ///     anything branches here - are provable on their own from the interpreter's arithmetic
    ///     while a block decomposition is not.
    ///     <para>
    ///     <b>The target arithmetic.</b> <c>Class247.java:7779</c> advances the counter with
    ///     <c>OPCODE = is[++current]</c> before dispatching, and every branch arm then does
    ///     <c>current += is_265_[current]</c>, so the next instruction executed is at
    ///     <c>position + 1 + delta</c>. Both the position and the delta are signed and a negative
    ///     delta is ordinary - it is how a loop is written. The <c>+ 1</c> is not cosmetic: over the
    ///     vanilla b639 capture all 42,884 branches in the index land on a real instruction under
    ///     this reading and exactly one does not under <c>position + delta</c>, which is script 686's
    ///     jump at position 8 with a delta of -9 in a 13-instruction script.
    ///     </para>
    ///     <para>
    ///     A switch arm's delta is measured from the <b>switch instruction</b>, not from the block,
    ///     because <c>Class247.java:7980</c> applies it to the same <c>current</c>. A block reached
    ///     from two different opcode-51 instructions therefore has two sets of targets, which is why
    ///     they are resolved per site rather than stored on the block.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptDisassembly {
        private readonly List<ClientScriptDisassemblyLine> lines;

        /// <summary>One line per instruction, in execution order.</summary>
        public IReadOnlyList<ClientScriptDisassemblyLine> Lines => lines;

        /// <summary>How many of the script's instructions carry a proven mnemonic.</summary>
        public int NamedInstructions { get; }

        /// <summary>How many instructions the script holds.</summary>
        public int InstructionCount => lines.Count;

        /// <summary>
        ///     Branch or switch targets that do not land on an instruction of this script.
        /// </summary>
        /// <remarks>
        ///     Zero across both supported caches. Carried rather than thrown so a hand-edited or
        ///     future script that is genuinely malformed still lists, with the defect visible instead
        ///     of the tab failing to open.
        /// </remarks>
        public int UnresolvableTargets { get; }

        private ClientScriptDisassembly(List<ClientScriptDisassemblyLine> lines, int named, int unresolvable) {
            this.lines = lines;
            NamedInstructions = named;
            UnresolvableTargets = unresolvable;
        }

        /// <summary>Disassembles one decoded script.</summary>
        /// <param name="script">The decoded script.</param>
        /// <returns>The listing.</returns>
        /// <exception cref="ArgumentNullException">No script was supplied.</exception>
        public static ClientScriptDisassembly Of(ClientScriptDefinition script) {
            if (script == null)
                throw new ArgumentNullException(nameof(script));

            int count = script.Instructions.Count;
            var lines = new List<ClientScriptDisassemblyLine>(count);
            var jumpedTo = new HashSet<int>();
            int named = 0;
            int unresolvable = 0;

            //The name field precedes the stream and always costs at least its terminator, so a
            //nameless script's first instruction sits at offset 1.
            int offset = (script.NameBytes?.Length ?? 0) + 1;

            for (int position = 0; position < count; position++) {
                ClientScriptInstruction instruction = script.Instructions[position];
                ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(instruction.Opcode);

                if (info.IsNamed)
                    named++;

                int? target = null;
                if (ClientScriptOpcodes.IsBranch(instruction.Opcode)) {
                    int candidate = position + 1 + instruction.IntegerOperand;
                    if (candidate >= 0 && candidate < count) {
                        target = candidate;
                        jumpedTo.Add(candidate);
                    }
                    else {
                        unresolvable++;
                    }
                }

                int? switchBlock = null;
                if (instruction.Opcode == ClientScriptOpcodes.SwitchOpcode) {
                    int block = instruction.IntegerOperand;
                    if (block >= 0 && block < script.SwitchBlocks.Count) {
                        switchBlock = block;
                        foreach (ClientScriptSwitchCase arm in script.SwitchBlocks[block].Cases) {
                            int candidate = position + 1 + arm.JumpOffset;
                            if (candidate >= 0 && candidate < count)
                                jumpedTo.Add(candidate);
                            else
                                unresolvable++;
                        }
                    }
                    else {
                        unresolvable++;
                    }
                }

                lines.Add(new ClientScriptDisassemblyLine(position, offset, instruction, info, target, switchBlock));
                offset += instruction.StoredLength;
            }

            //Second pass: a line cannot know it is a label until every branch has been resolved.
            foreach (ClientScriptDisassemblyLine line in lines)
                line.MarkLabel(jumpedTo.Contains(line.Position));

            return new ClientScriptDisassembly(lines, named, unresolvable);
        }

        /// <summary>
        ///     Where each arm of a switch block goes, resolved against the instruction that selects it.
        /// </summary>
        /// <param name="script">The decoded script.</param>
        /// <param name="switchPosition">The position of the opcode-51 instruction.</param>
        /// <param name="jumpOffset">The arm's stored delta.</param>
        /// <returns>The target instruction, or null when it does not land on one.</returns>
        /// <exception cref="ArgumentNullException">No script was supplied.</exception>
        public static int? ResolveSwitchTarget(ClientScriptDefinition script, int switchPosition, int jumpOffset) {
            if (script == null)
                throw new ArgumentNullException(nameof(script));

            int target = switchPosition + 1 + jumpOffset;
            return target >= 0 && target < script.Instructions.Count ? target : (int?) null;
        }
    }

    /// <summary>One disassembled instruction.</summary>
    public sealed class ClientScriptDisassemblyLine {
        /// <summary>Where the instruction sits in the stream, which is what a jump is relative to.</summary>
        public int Position { get; }

        /// <summary>Where the instruction starts in the decompressed file.</summary>
        public int Offset { get; }

        /// <summary>The decoded instruction.</summary>
        public ClientScriptInstruction Instruction { get; }

        /// <summary>What is known about the opcode, which is never nothing.</summary>
        public ClientScriptOpcodeInfo Info { get; }

        /// <summary>Where this instruction branches to, or null when it does not branch.</summary>
        public int? BranchTarget { get; }

        /// <summary>Which switch block this instruction selects, or null when it is not a switch.</summary>
        public int? SwitchBlock { get; }

        /// <summary>Whether some branch or switch arm in this script targets this position.</summary>
        public bool IsLabel { get; private set; }

        internal ClientScriptDisassemblyLine(int position, int offset, ClientScriptInstruction instruction,
            ClientScriptOpcodeInfo info, int? branchTarget, int? switchBlock) {
            Position = position;
            Offset = offset;
            Instruction = instruction;
            Info = info;
            BranchTarget = branchTarget;
            SwitchBlock = switchBlock;
        }

        /// <summary>Records that something in the script jumps here.</summary>
        /// <param name="isLabel">Whether this position is a branch target.</param>
        internal void MarkLabel(bool isLabel) {
            IsLabel = isLabel;
        }

        /// <summary>
        ///     The operand as text, quoted when it is a string so an empty one is visible as one.
        /// </summary>
        /// <returns>The operand.</returns>
        public string OperandText() {
            return Instruction.OperandKind == ClientScriptOperandKind.Text
                ? "\"" + Instruction.TextOperand + "\""
                : Instruction.IntegerOperand.ToString();
        }
    }
}
