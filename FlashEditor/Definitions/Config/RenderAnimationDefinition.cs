using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     One indexed write into a render animation's twelve-slot table, as the file stored it.
    /// </summary>
    /// <remarks>
    ///     Opcodes 27, 55 and 56 each carry a slot index and then a fixed number of values, and each
    ///     may appear more than once in a record to fill more than one slot. Keeping the occurrences
    ///     as a list rather than materialising the twelve-slot array is what makes that
    ///     re-encodable: the array remembers the values but not which opcodes wrote them, in what
    ///     order, or whether two occurrences targeted the same slot. <b>None of the three occurs in
    ///     either cache</b>, so no sweep defends this and only a hand-built record can.
    /// </remarks>
    public readonly struct RenderAnimationSlot {
        /// <summary>The slot the occurrence writes, 0..11 in the client's arrays.</summary>
        public int Slot { get; }

        /// <summary>The values it wrote, one per field the opcode carries.</summary>
        public int[] Values { get; }

        /// <summary>Records one indexed write.</summary>
        /// <param name="slot">The slot index.</param>
        /// <param name="values">The values, never null.</param>
        public RenderAnimationSlot(int slot, int[] values) {
            Slot = slot;
            Values = values ?? Array.Empty<int>();
        }
    }

    /// <summary>
    ///     A render animation set: which animation a player or NPC plays for every combination of
    ///     movement and facing.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.RenderAnimation"/>. Decoded by
    ///     <c>Class294.method3475</c> (:176-194) dispatching to <c>method3476</c> (:196-418); the
    ///     provider is <c>Class257</c>, which names the group at Class257.java:82.
    ///     <para>
    ///     <b>Settled by usage.</b> Every mobile resolves one of these through
    ///     <c>Particle_Sub3_Sub4_Sub2.method3039</c> (:828-844).
    ///     <c>Class284_Sub1_Sub2.method3370</c> (:27-140) is the selector, and it is what names the
    ///     fields: it masks the difference between a mobile's facing and its heading into 0..16383,
    ///     splits that into four quadrants at 2048, 6144, 10240 and 14336, and picks one animation
    ///     per quadrant out of three parallel sets - the walk set on opcodes 2 to 5, the run set on 6
    ///     to 9, and a third set on opcode 1's second short plus opcodes 40 to 42 that is used
    ///     whenever neither of the others applies. Opcodes 38, 39, 46 to 51 add turn variants,
    ///     selected on the sign of the turn delta.
    ///     </para>
    ///     <para>
    ///     <b>Hazards.</b> 579 of the 1,972 records are in non-ascending opcode order across 58
    ///     distinct orders, and two records repeat an opcode: file 1205 stores opcode 6 twice and
    ///     file 1799 stores 38 and 39 twice each, interleaved. Seventeen opcodes - 27, 28, 29, 30,
    ///     31, 32, 33, 34, 35, 36, 37, 43, 44, 45, 53, 55 and 56 - occur in no file of either cache,
    ///     so a passing sweep says nothing at all about them.
    ///     </para>
    /// </remarks>
    public sealed class RenderAnimationDefinition : ConfigDefinition {
        /// <summary>How many entries opcode 28's equipment-slot table holds.</summary>
        /// <remarks>Twelve, fixed by <c>Class294.java:229</c>; the count is not stored.</remarks>
        public const int EquipmentSlots = 12;

        /// <summary>Opcode 1, first short. The idle animation, or -1 to pick from <see cref="IdlePoolAnimationIds"/>.</summary>
        /// <remarks>
        ///     <c>anInt2396</c>. <c>Class294.method3478</c> (:420-444) returns it when it is set and
        ///     otherwise draws from the opcode 52 pool weighted by its bytes; CS2 opcode 4600
        ///     (Class247.java:3802-3822) reads the same pair back. Stored 65535 means -1, and 72
        ///     records store that sentinel.
        /// </remarks>
        public int IdleAnimationId { get; set; } = -1;

        /// <summary>Opcode 1, second short. The animation for moving forward when no set applies.</summary>
        /// <remarks>
        ///     <c>anInt2399</c>, the fallback of every arm of the selector. Stored 65535 means -1, and
        ///     310 records store that sentinel.
        /// </remarks>
        public int MoveForwardAnimationId { get; set; } = -1;

        /// <summary>Opcode 2. Walking with the facing within a quadrant of the heading.</summary>
        /// <remarks><c>anInt2368</c>. Selected at Class284_Sub1_Sub2.java:110-121.</remarks>
        public int WalkForwardAnimationId { get; set; } = -1;

        /// <summary>Opcode 3. Walking with the facing 180 degrees from the heading.</summary>
        /// <remarks>
        ///     <c>anInt2394</c>, the arm for a masked angle in 6144..10240. Named by quadrant rather
        ///     than as "backward" or "left" because the client never states which way its yaw runs.
        /// </remarks>
        public int WalkAt180AnimationId { get; set; } = -1;

        /// <summary>Opcode 4. Walking with the facing 270 degrees from the heading.</summary>
        /// <remarks><c>anInt2377</c>, the arm for a masked angle in 10240..14336.</remarks>
        public int WalkAt270AnimationId { get; set; } = -1;

        /// <summary>Opcode 5. Walking with the facing 90 degrees from the heading.</summary>
        /// <remarks><c>anInt2403</c>, the arm for a masked angle in 2048..6144.</remarks>
        public int WalkAt90AnimationId { get; set; } = -1;

        /// <summary>Opcode 6. Running with the facing within a quadrant of the heading.</summary>
        /// <remarks><c>anInt2389</c>. File 1205 stores this opcode twice.</remarks>
        public int RunForwardAnimationId { get; set; } = -1;

        /// <summary>Opcode 7. Running with the facing 180 degrees from the heading.</summary>
        /// <remarks><c>anInt2361</c>.</remarks>
        public int RunAt180AnimationId { get; set; } = -1;

        /// <summary>Opcode 8. Running with the facing 270 degrees from the heading.</summary>
        /// <remarks><c>anInt2357</c>.</remarks>
        public int RunAt270AnimationId { get; set; } = -1;

        /// <summary>Opcode 9. Running with the facing 90 degrees from the heading.</summary>
        /// <remarks><c>anInt2402</c>.</remarks>
        public int RunAt90AnimationId { get; set; } = -1;

        /// <summary>Opcode 26, first byte, as stored.</summary>
        /// <remarks>
        ///     <c>anInt2362</c>, which the client stores as <c>value * 4</c> and passes to
        ///     <c>Particle_Sub3_Sub4_Sub2.method3040</c> alongside <see cref="Unknown26B"/>
        ///     (Player.java:953-1009). The raw byte is kept rather than the scaled value: the scale
        ///     is injective over a byte so either would round-trip, and the byte cannot be wrong.
        /// </remarks>
        public int Unknown26A { get; set; }

        /// <summary>Opcode 26, second byte, as stored.</summary>
        /// <remarks><c>anInt2382</c>, scaled the same way.</remarks>
        public int Unknown26B { get; set; }

        /// <summary>Opcode 27. Per-model-slot translations and rotations, in stored order.</summary>
        /// <remarks>
        ///     <c>anIntArrayArray2366</c>, six signed shorts per slot. <c>Class141.java:1182-1208</c>
        ///     applies entries 0 to 2 as a translation and entries 3 to 5, each shifted left three,
        ///     as a rotation of that slot's model. Occurs in no file of either cache.
        /// </remarks>
        public List<RenderAnimationSlot> ModelSlotTransforms { get; } = new List<RenderAnimationSlot>();

        /// <summary>Opcode 28. Which equipment slot each model slot draws from, -1 for none.</summary>
        /// <remarks>
        ///     <c>anIntArray2379</c>, twelve unsigned bytes with 255 meaning -1.
        ///     <c>Node_Sub3.java:29-41</c> uses it to reorder the worn-item list before the models are
        ///     built. Occurs in no file of either cache, so the 255 alias is untested by the data;
        ///     -1 has exactly one encoding, which is what makes writing 255 back safe.
        /// </remarks>
        public int[]? EquipmentSlotOrder { get; set; }

        /// <summary>Opcode 29. How fast the body yaw catches up with the heading.</summary>
        /// <remarks>
        ///     <c>anInt2398</c>, the acceleration argument of <c>Class325.method3699</c>
        ///     (Particle_Sub3_Sub4_Sub2.java:472-475); zero disables the turn entirely.
        /// </remarks>
        public int Unknown29 { get; set; }

        /// <summary>Opcode 30. The yaw that turn aims at.</summary>
        /// <remarks><c>anInt2383</c>, the target argument of the same call.</remarks>
        public int Unknown30 { get; set; }

        /// <summary>Opcode 31. Acceleration of a second turn, on another part.</summary>
        /// <remarks><c>anInt2390</c> (Particle_Sub3_Sub4_Sub2.java:488-503).</remarks>
        public int Unknown31 { get; set; }

        /// <summary>Opcode 32. Target of that second turn.</summary>
        /// <remarks><c>anInt2392</c>.</remarks>
        public int Unknown32 { get; set; }

        /// <summary>Opcode 33. Amplitude of that second turn, signed and applied both ways.</summary>
        /// <remarks><c>anInt2393</c>, passed as itself and as its negation on alternate ticks.</remarks>
        public int Unknown33 { get; set; }

        /// <summary>Opcode 34. Acceleration of a third turn.</summary>
        /// <remarks><c>anInt2375</c> (Particle_Sub3_Sub4_Sub2.java:496-507).</remarks>
        public int Unknown34 { get; set; }

        /// <summary>Opcode 35. Target of that third turn.</summary>
        /// <remarks><c>anInt2380</c>.</remarks>
        public int Unknown35 { get; set; }

        /// <summary>Opcode 36. Amplitude of that third turn, signed.</summary>
        /// <remarks><c>anInt2363</c>.</remarks>
        public int Unknown36 { get; set; }

        /// <summary>Opcode 37. Step of a per-tick offset applied while the mobile turns.</summary>
        /// <remarks><c>anInt2401</c>, added to and subtracted from a running counter at Class333.java:183-202.</remarks>
        public int Unknown37 { get; set; } = -1;

        /// <summary>Opcode 38. Turning on the spot, the negative direction.</summary>
        /// <remarks>
        ///     <c>anInt2376</c>, chosen when the turn delta is negative
        ///     (Class284_Sub1_Sub2.java:43-51). Whether that is left or right is not settled by the
        ///     client.
        /// </remarks>
        public int TurnOnSpotNegativeAnimationId { get; set; } = -1;

        /// <summary>Opcode 39. Turning on the spot, the positive direction.</summary>
        /// <remarks><c>anInt2388</c>. File 1799 stores this opcode twice.</remarks>
        public int TurnOnSpotPositiveAnimationId { get; set; } = -1;

        /// <summary>Opcode 40. Default set, facing 180 degrees from the heading.</summary>
        /// <remarks><c>anInt2365</c> (Class284_Sub1_Sub2.java:125-131).</remarks>
        public int MoveAt180AnimationId { get; set; } = -1;

        /// <summary>Opcode 41. Default set, facing 270 degrees from the heading.</summary>
        /// <remarks><c>anInt2359</c>.</remarks>
        public int MoveAt270AnimationId { get; set; } = -1;

        /// <summary>Opcode 42. Default set, facing 90 degrees from the heading.</summary>
        /// <remarks><c>anInt2372</c>.</remarks>
        public int MoveAt90AnimationId { get; set; } = -1;

        /// <summary>Opcode 43. The animation a hit splat's own render uses.</summary>
        /// <remarks><c>anInt2381</c>, read at IntegerNode.java:72,77. Occurs in no file of either cache.</remarks>
        public int Unknown43 { get; set; } = -1;

        /// <summary>Opcode 44. A second animation on that path.</summary>
        /// <remarks><c>anInt2374</c> (IntegerNode.java:152-157). Occurs in no file of either cache.</remarks>
        public int Unknown44 { get; set; } = -1;

        /// <summary>Opcode 45. An animation returned in place of the mobile's own.</summary>
        /// <remarks>
        ///     <c>anInt2385</c>, returned outright when set (Particle_Sub3_Sub4_Sub2.java:1013-1014).
        ///     Occurs in no file of either cache.
        /// </remarks>
        public int Unknown45 { get; set; } = -1;

        /// <summary>Opcode 46. Walking while turning the negative way.</summary>
        /// <remarks><c>anInt2405</c> (Class284_Sub1_Sub2.java:58-62).</remarks>
        public int WalkTurnNegativeAnimationId { get; set; } = -1;

        /// <summary>Opcode 47. Walking while turning the positive way.</summary>
        /// <remarks><c>anInt2404</c>.</remarks>
        public int WalkTurnPositiveAnimationId { get; set; } = -1;

        /// <summary>Opcode 48. Running while turning the negative way.</summary>
        /// <remarks><c>anInt2384</c> (Class284_Sub1_Sub2.java:75-80).</remarks>
        public int RunTurnNegativeAnimationId { get; set; } = -1;

        /// <summary>Opcode 49. Running while turning the positive way.</summary>
        /// <remarks><c>anInt2370</c>.</remarks>
        public int RunTurnPositiveAnimationId { get; set; } = -1;

        /// <summary>Opcode 50. Default set while turning the negative way.</summary>
        /// <remarks><c>anInt2378</c> (Class284_Sub1_Sub2.java:65-73).</remarks>
        public int MoveTurnNegativeAnimationId { get; set; } = -1;

        /// <summary>Opcode 51. Default set while turning the positive way.</summary>
        /// <remarks><c>anInt2369</c>.</remarks>
        public int MoveTurnPositiveAnimationId { get; set; } = -1;

        /// <summary>Opcode 52. The idle animations drawn from when <see cref="IdleAnimationId"/> is -1.</summary>
        /// <remarks>
        ///     <c>anIntArray2395</c>. <c>Class294.method3478</c> picks one at random weighted by
        ///     <see cref="IdlePoolWeights"/>; CS2 opcode 4600 returns the heaviest instead. Measured
        ///     over both caches: 54 records carry a pool, of one, two, three or five entries.
        /// </remarks>
        public int[]? IdlePoolAnimationIds { get; set; }

        /// <summary>Opcode 52. The weight of each pooled animation, one byte each.</summary>
        /// <remarks>
        ///     <c>anIntArray2386</c>. The client also accumulates their sum into <c>anInt2367</c>,
        ///     which is derived and is deliberately not stored here - recomputing it costs nothing
        ///     and keeping it would let an edit leave the two disagreeing.
        /// </remarks>
        public int[]? IdlePoolWeights { get; set; }

        /// <summary>The total weight of the idle pool, as the client accumulates it while reading.</summary>
        public int IdlePoolWeightTotal {
            get {
                int total = 0;
                foreach (int weight in IdlePoolWeights ?? Array.Empty<int>())
                    total += weight;
                return total;
            }
        }

        /// <summary>Opcode 53. Present clears a flag the client sets by default.</summary>
        /// <remarks>
        ///     <c>aBoolean2400</c>, gating a branch of the appearance build
        ///     (Particle_Sub3_Sub4_Sub2_Sub1.java:79, Player.java:576). Occurs in no file of either
        ///     cache, so what it gates is recorded and not named.
        /// </remarks>
        public bool Unknown53 { get; set; } = true;

        /// <summary>Opcode 54, first byte, as stored.</summary>
        /// <remarks>
        ///     <c>anInt2360</c>, which the client stores as <c>value &lt;&lt; 6</c> and passes to
        ///     <c>method3040</c> with <see cref="Unknown54B"/>. Kept raw for the reason
        ///     <see cref="Unknown26A"/> is.
        /// </remarks>
        public int Unknown54A { get; set; }

        /// <summary>Opcode 54, second byte, as stored.</summary>
        /// <remarks><c>anInt2391</c>, shifted the same way.</remarks>
        public int Unknown54B { get; set; }

        /// <summary>Opcode 55. Per-slot values, in stored order.</summary>
        /// <remarks>
        ///     <c>anIntArray2373</c>, one unsigned short per slot, read back at
        ///     Particle_Sub3_Sub4_Sub2.java:948-949. Occurs in no file of either cache.
        /// </remarks>
        public List<RenderAnimationSlot> Unknown55Slots { get; } = new List<RenderAnimationSlot>();

        /// <summary>Opcode 56. Per-slot triples, in stored order.</summary>
        /// <remarks>
        ///     <c>anIntArrayArray2364</c>, three signed shorts per slot, read at
        ///     Node_Sub10_Sub22.java:239-241. Occurs in no file of either cache.
        /// </remarks>
        public List<RenderAnimationSlot> Unknown56Slots { get; } = new List<RenderAnimationSlot>();

        /// <summary>Decodes one render animation definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public RenderAnimationDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1:
                    IdleAnimationId = ShortOrMinusOne(stream);
                    MoveForwardAnimationId = ShortOrMinusOne(stream);
                    break;

                case 2: WalkForwardAnimationId = stream.ReadUnsignedShort(); break;
                case 3: WalkAt180AnimationId = stream.ReadUnsignedShort(); break;
                case 4: WalkAt270AnimationId = stream.ReadUnsignedShort(); break;
                case 5: WalkAt90AnimationId = stream.ReadUnsignedShort(); break;
                case 6: RunForwardAnimationId = stream.ReadUnsignedShort(); break;
                case 7: RunAt180AnimationId = stream.ReadUnsignedShort(); break;
                case 8: RunAt270AnimationId = stream.ReadUnsignedShort(); break;
                case 9: RunAt90AnimationId = stream.ReadUnsignedShort(); break;

                case 26:
                    Unknown26A = stream.ReadUnsignedByte();
                    Unknown26B = stream.ReadUnsignedByte();
                    break;

                case 27: ModelSlotTransforms.Add(ReadSlot(stream, 6, signed: true)); break;

                case 28: {
                    int[] order = new int[EquipmentSlots];
                    for (int i = 0; i < order.Length; i++) {
                        int slot = stream.ReadUnsignedByte();
                        order[i] = slot == 0xFF ? -1 : slot;
                    }
                    EquipmentSlotOrder = order;
                    break;
                }

                case 29: Unknown29 = stream.ReadUnsignedByte(); break;
                case 30: Unknown30 = stream.ReadUnsignedShort(); break;
                case 31: Unknown31 = stream.ReadUnsignedByte(); break;
                case 32: Unknown32 = stream.ReadUnsignedShort(); break;
                case 33: Unknown33 = stream.ReadShort(); break;
                case 34: Unknown34 = stream.ReadUnsignedByte(); break;
                case 35: Unknown35 = stream.ReadUnsignedShort(); break;
                case 36: Unknown36 = stream.ReadShort(); break;
                case 37: Unknown37 = stream.ReadUnsignedByte(); break;
                case 38: TurnOnSpotNegativeAnimationId = stream.ReadUnsignedShort(); break;
                case 39: TurnOnSpotPositiveAnimationId = stream.ReadUnsignedShort(); break;
                case 40: MoveAt180AnimationId = stream.ReadUnsignedShort(); break;
                case 41: MoveAt270AnimationId = stream.ReadUnsignedShort(); break;
                case 42: MoveAt90AnimationId = stream.ReadUnsignedShort(); break;
                case 43: Unknown43 = stream.ReadUnsignedShort(); break;
                case 44: Unknown44 = stream.ReadUnsignedShort(); break;
                case 45: Unknown45 = stream.ReadUnsignedShort(); break;
                case 46: WalkTurnNegativeAnimationId = stream.ReadUnsignedShort(); break;
                case 47: WalkTurnPositiveAnimationId = stream.ReadUnsignedShort(); break;
                case 48: RunTurnNegativeAnimationId = stream.ReadUnsignedShort(); break;
                case 49: RunTurnPositiveAnimationId = stream.ReadUnsignedShort(); break;
                case 50: MoveTurnNegativeAnimationId = stream.ReadUnsignedShort(); break;
                case 51: MoveTurnPositiveAnimationId = stream.ReadUnsignedShort(); break;

                case 52: {
                    int count = stream.ReadUnsignedByte();
                    int[] ids = new int[count];
                    int[] weights = new int[count];
                    for (int i = 0; i < count; i++) {
                        ids[i] = stream.ReadUnsignedShort();
                        weights[i] = stream.ReadUnsignedByte();
                    }
                    IdlePoolAnimationIds = ids;
                    IdlePoolWeights = weights;
                    break;
                }

                case 53: Unknown53 = false; break;

                case 54:
                    Unknown54A = stream.ReadUnsignedByte();
                    Unknown54B = stream.ReadUnsignedByte();
                    break;

                case 55: Unknown55Slots.Add(ReadSlot(stream, 1, signed: false)); break;
                case 56: Unknown56Slots.Add(ReadSlot(stream, 3, signed: true)); break;

                default:
                    //The client's dispatcher is a chain of equality tests with no final else, so an
                    //opcode it does not name consumes nothing and desynchronises the rest of the
                    //record. Refusing is strictly better.
                    throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1:
                    WriteShortOrMinusOne(stream, IdleAnimationId);
                    WriteShortOrMinusOne(stream, MoveForwardAnimationId);
                    break;

                case 2: stream.WriteShort(WalkForwardAnimationId); break;
                case 3: stream.WriteShort(WalkAt180AnimationId); break;
                case 4: stream.WriteShort(WalkAt270AnimationId); break;
                case 5: stream.WriteShort(WalkAt90AnimationId); break;
                case 6: stream.WriteShort(RunForwardAnimationId); break;
                case 7: stream.WriteShort(RunAt180AnimationId); break;
                case 8: stream.WriteShort(RunAt270AnimationId); break;
                case 9: stream.WriteShort(RunAt90AnimationId); break;

                case 26:
                    stream.WriteByte(Unknown26A);
                    stream.WriteByte(Unknown26B);
                    break;

                case 27: WriteSlot(stream, ModelSlotTransforms, 6, 27); break;

                case 28: {
                    int[] order = EquipmentSlotOrder ?? Array.Empty<int>();
                    if (order.Length != EquipmentSlots)
                        throw new InvalidDataException("Render animation " + Id + " has " +
                            order.Length + " equipment slots; opcode 28 stores exactly " +
                            EquipmentSlots + " and carries no count.");
                    foreach (int slot in order)
                        stream.WriteByte(slot == -1 ? 0xFF : slot);
                    break;
                }

                case 29: stream.WriteByte(Unknown29); break;
                case 30: stream.WriteShort(Unknown30); break;
                case 31: stream.WriteByte(Unknown31); break;
                case 32: stream.WriteShort(Unknown32); break;
                case 33: stream.WriteShort(Unknown33); break;
                case 34: stream.WriteByte(Unknown34); break;
                case 35: stream.WriteShort(Unknown35); break;
                case 36: stream.WriteShort(Unknown36); break;
                case 37: stream.WriteByte(Unknown37); break;
                case 38: stream.WriteShort(TurnOnSpotNegativeAnimationId); break;
                case 39: stream.WriteShort(TurnOnSpotPositiveAnimationId); break;
                case 40: stream.WriteShort(MoveAt180AnimationId); break;
                case 41: stream.WriteShort(MoveAt270AnimationId); break;
                case 42: stream.WriteShort(MoveAt90AnimationId); break;
                case 43: stream.WriteShort(Unknown43); break;
                case 44: stream.WriteShort(Unknown44); break;
                case 45: stream.WriteShort(Unknown45); break;
                case 46: stream.WriteShort(WalkTurnNegativeAnimationId); break;
                case 47: stream.WriteShort(WalkTurnPositiveAnimationId); break;
                case 48: stream.WriteShort(RunTurnNegativeAnimationId); break;
                case 49: stream.WriteShort(RunTurnPositiveAnimationId); break;
                case 50: stream.WriteShort(MoveTurnNegativeAnimationId); break;
                case 51: stream.WriteShort(MoveTurnPositiveAnimationId); break;

                case 52: {
                    int[] ids = IdlePoolAnimationIds ?? Array.Empty<int>();
                    int[] weights = IdlePoolWeights ?? Array.Empty<int>();
                    if (ids.Length != weights.Length)
                        throw new InvalidDataException("Render animation " + Id + " has " +
                            ids.Length + " pooled animations and " + weights.Length +
                            " weights; opcode 52 stores one count for both.");
                    stream.WriteByte(ids.Length);
                    for (int i = 0; i < ids.Length; i++) {
                        stream.WriteShort(ids[i]);
                        stream.WriteByte(weights[i]);
                    }
                    break;
                }

                case 53: break;

                case 54:
                    stream.WriteByte(Unknown54A);
                    stream.WriteByte(Unknown54B);
                    break;

                case 55: WriteSlot(stream, Unknown55Slots, 1, 55); break;
                case 56: WriteSlot(stream, Unknown56Slots, 3, 56); break;

                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && (IdleAnimationId != -1 || MoveForwardAnimationId != -1)) yield return 1;
            if (!Has(2) && WalkForwardAnimationId != -1) yield return 2;
            if (!Has(3) && WalkAt180AnimationId != -1) yield return 3;
            if (!Has(4) && WalkAt270AnimationId != -1) yield return 4;
            if (!Has(5) && WalkAt90AnimationId != -1) yield return 5;
            if (!Has(6) && RunForwardAnimationId != -1) yield return 6;
            if (!Has(7) && RunAt180AnimationId != -1) yield return 7;
            if (!Has(8) && RunAt270AnimationId != -1) yield return 8;
            if (!Has(9) && RunAt90AnimationId != -1) yield return 9;
            if (!Has(26) && (Unknown26A != 0 || Unknown26B != 0)) yield return 26;
            if (!Has(27) && ModelSlotTransforms.Count > 0) yield return 27;
            if (!Has(28) && EquipmentSlotOrder != null) yield return 28;
            if (!Has(29) && Unknown29 != 0) yield return 29;
            if (!Has(30) && Unknown30 != 0) yield return 30;
            if (!Has(31) && Unknown31 != 0) yield return 31;
            if (!Has(32) && Unknown32 != 0) yield return 32;
            if (!Has(33) && Unknown33 != 0) yield return 33;
            if (!Has(34) && Unknown34 != 0) yield return 34;
            if (!Has(35) && Unknown35 != 0) yield return 35;
            if (!Has(36) && Unknown36 != 0) yield return 36;
            if (!Has(37) && Unknown37 != -1) yield return 37;
            if (!Has(38) && TurnOnSpotNegativeAnimationId != -1) yield return 38;
            if (!Has(39) && TurnOnSpotPositiveAnimationId != -1) yield return 39;
            if (!Has(40) && MoveAt180AnimationId != -1) yield return 40;
            if (!Has(41) && MoveAt270AnimationId != -1) yield return 41;
            if (!Has(42) && MoveAt90AnimationId != -1) yield return 42;
            if (!Has(43) && Unknown43 != -1) yield return 43;
            if (!Has(44) && Unknown44 != -1) yield return 44;
            if (!Has(45) && Unknown45 != -1) yield return 45;
            if (!Has(46) && WalkTurnNegativeAnimationId != -1) yield return 46;
            if (!Has(47) && WalkTurnPositiveAnimationId != -1) yield return 47;
            if (!Has(48) && RunTurnNegativeAnimationId != -1) yield return 48;
            if (!Has(49) && RunTurnPositiveAnimationId != -1) yield return 49;
            if (!Has(50) && MoveTurnNegativeAnimationId != -1) yield return 50;
            if (!Has(51) && MoveTurnPositiveAnimationId != -1) yield return 51;
            if (!Has(52) && IdlePoolAnimationIds != null) yield return 52;
            if (!Has(53) && !Unknown53) yield return 53;
            if (!Has(54) && (Unknown54A != 0 || Unknown54B != 0)) yield return 54;
            if (!Has(55) && Unknown55Slots.Count > 0) yield return 55;
            if (!Has(56) && Unknown56Slots.Count > 0) yield return 56;
        }

        /// <summary>Reads one indexed slot write: a slot byte, then a fixed number of shorts.</summary>
        /// <param name="stream">The definition file, positioned at the slot index.</param>
        /// <param name="fields">How many shorts follow.</param>
        /// <param name="signed">Whether those shorts are signed.</param>
        /// <returns>The occurrence.</returns>
        private static RenderAnimationSlot ReadSlot(JagStream stream, int fields, bool signed) {
            int slot = stream.ReadUnsignedByte();
            int[] values = new int[fields];
            for (int i = 0; i < fields; i++)
                values[i] = signed ? stream.ReadShort() : stream.ReadUnsignedShort();
            return new RenderAnimationSlot(slot, values);
        }

        /// <summary>
        ///     Writes the last recorded occurrence of an indexed slot opcode.
        /// </summary>
        /// <remarks>
        ///     The last one, because that is the occurrence the opcode stream re-encodes from a
        ///     field; every earlier one replays the bytes it was read from, and taking the first
        ///     here would put the wrong slot into the last position.
        /// </remarks>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="slots">The occurrences recorded for this opcode.</param>
        /// <param name="fields">How many shorts each carries.</param>
        /// <param name="opcode">The opcode, for the failure message.</param>
        private void WriteSlot(JagStream stream, List<RenderAnimationSlot> slots, int fields, int opcode) {
            if (slots.Count == 0)
                throw new InvalidDataException("Render animation " + Id + " carries opcode " +
                    opcode + " with no slot recorded for it.");

            RenderAnimationSlot slot = slots[slots.Count - 1];
            if (slot.Values.Length != fields)
                throw new InvalidDataException("Render animation " + Id + " opcode " + opcode +
                    " holds " + slot.Values.Length + " values for slot " + slot.Slot +
                    "; the opcode stores exactly " + fields + ".");

            stream.WriteByte(slot.Slot);
            foreach (int value in slot.Values)
                stream.WriteShort(value);
        }

        /// <summary>Reads an animation id, mapping the stored 65535 to -1 as the client does.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>The id, or -1.</returns>
        private static int ShortOrMinusOne(JagStream stream) {
            int value = stream.ReadUnsignedShort();
            return value == 0xFFFF ? -1 : value;
        }

        /// <summary>Writes back the 65535 the client reads as -1.</summary>
        /// <remarks>
        ///     -1 has exactly one encoding in this field, so the alias is safe in one direction only:
        ///     writing a truncated -1 would emit 0xFFFF by accident rather than by rule. 72 records
        ///     store the sentinel in the first short and 310 in the second, so both halves are
        ///     exercised by the cache.
        /// </remarks>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="value">The id, or -1.</param>
        private static void WriteShortOrMinusOne(JagStream stream, int value) {
            stream.WriteShort(value == -1 ? 0xFFFF : value);
        }
    }
}
