using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Particles {
    /// <summary>
    ///     What the Particles tab needs from a row whichever of index 27's two families it holds.
    /// </summary>
    /// <remarks>
    ///     Emitters and effectors share an index and nothing else: one describes how particles are
    ///     born and the other a force applied to them once they exist. A single grid would need a
    ///     union of headings that is wrong for both, so the tab selects the family and this is the
    ///     smallest surface the shared detail pane can be written against.
    /// </remarks>
    public interface IParticleListing : IDetailRow {
        /// <summary>Where the record lives in the cache.</summary>
        DefinitionAddress Address { get; }
    }

    /// <summary>One particle emitter from group 0 of index 27 as a list row.</summary>
    public sealed class ParticleEmitterListing : IParticleListing {
        /// <summary>Binds one decoded emitter to where it came from.</summary>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public ParticleEmitterListing(DefinitionAddress address, ParticleEmitterDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public ParticleEmitterDefinition Record { get; }

        /// <summary>The emitter id, which is its file id in group 0.</summary>
        public int EmitterId => Record.Id;

        /// <summary>The material each particle is drawn with, or nothing when it has none.</summary>
        /// <remarks>Null rather than -1 so "untextured quad" reads as an empty cell.</remarks>
        public object? MaterialId => Record.MaterialId == ParticleEmitterDefinition.NoMaterial ? null : Record.MaterialId;

        /// <summary>The spawn-speed range.</summary>
        public string Speed => Range(Record.SpeedMin, Record.SpeedMax);

        /// <summary>The spawn rate, in 1/64 particles per elapsed unit.</summary>
        public string SpawnRate => Range(Record.SpawnRateMin, Record.SpawnRateMax);

        /// <summary>The particle lifetime range.</summary>
        public string Lifetime => Range(Record.LifetimeMin, Record.LifetimeMax);

        /// <summary>
        ///     The spawn-size range, flagged when the file stored it as a single value.
        /// </summary>
        /// <remarks>
        ///     Opcodes 5 and 31 are aliases for the same two fields, so the decoded numbers cannot
        ///     say which the file used and both occur here. The marker is the only place that shows.
        /// </remarks>
        public string Size =>
            Range(Record.SizeMinStored, Record.SizeMaxStored) +
            (Record.StoresSizeAsASingleValue ? " (op5)" : string.Empty);

        /// <summary>The spawn-colour range, packed ARGB.</summary>
        public string Colours => Argb(Record.SpawnColourStart) + " to " + Argb(Record.SpawnColourEnd);

        /// <summary>How many effectors this emitter names, by either route.</summary>
        public int Effectors =>
            (Record.SceneEffectorIds?.Length ?? 0) + (Record.GlobalEffectorIds?.Length ?? 0);

        /// <summary>The bare flags the record carries, as the opcode numbers that set them.</summary>
        /// <remarks>
        ///     Named by opcode as well as by meaning because two of them - 11 and 26 - have no
        ///     meaning to state: nothing in the 637 client handles either.
        /// </remarks>
        public string Flags {
            get {
                var set = new List<string>();
                if (Record.HasUnhandledFlag11)
                    set.Add("11");
                if (Record.RandomisesColourChannelsIndependently)
                    set.Add("24 rnd-channels");
                if (Record.UnusedFlag26)
                    set.Add("26");
                if (Record.KeepsMaterialOnSoftwareRenderer)
                    set.Add("30 keep-material");
                if (Record.BreaksTheDrawBatch)
                    set.Add("32 no-batch");
                if (Record.DiesOnCollision)
                    set.Add("33 die-on-hit");
                if (Record.SurvivesBelowTheGround)
                    set.Add("34 below-ground");
                return string.Join(" ", set);
            }
        }

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        public string OpcodeOrder => DetailText.Order(Record.Opcodes);

        /// <inheritdoc/>
        public string Summary =>
            "Emitter " + EmitterId + " - " + Record.Opcodes.Count + " opcode(s) - order " + OpcodeOrder;

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields => new[] {
            new DetailField("Yaw bounds, stored (opcode 1)", Record.YawStartStored + " to " + Record.YawEndStored),
            new DetailField("Pitch bounds, stored (opcode 1)", Record.PitchStartStored + " to " + Record.PitchEndStored),
            new DetailField("Unread byte (opcode 2)", Record.Opcodes.Has(2) ? Record.UnusedByte2.ToString() : "not stored"),
            new DetailField("Spawn speed (opcode 3)", Speed),
            new DetailField("Drag (opcode 4)", "mode " + Record.DragMode + ", strength " + Record.DragStrength),
            new DetailField("Spawn size, stored (opcodes 5/31)", Size),
            new DetailField("Spawn colour (opcode 6)", Colours),
            new DetailField("Lifetime (opcode 7)", Lifetime),
            new DetailField("Spawn rate (opcode 8)", SpawnRate),
            new DetailField("Scene effector ids (opcode 9)", DetailText.Ids(Record.SceneEffectorIds)),
            new DetailField("Global effector ids (opcode 10)", DetailText.Ids(Record.GlobalEffectorIds)),
            new DetailField("Ceiling plane (opcode 12)", DetailText.OrAbsent(Record.CeilingPlane, -2)),
            new DetailField("Floor plane (opcode 13)", DetailText.OrAbsent(Record.FloorPlane, -2)),
            new DetailField("Prime steps (opcode 14)", Record.PrimeSteps.ToString()),
            new DetailField("Material (opcode 15)", DetailText.OrAbsent(Record.MaterialId)),
            new DetailField("Duty cycle (opcode 16)",
                "flag " + Record.CycleFlagStored + " (emits " + (Record.EmitsBeforeThreshold ? "before" : "after") +
                "), threshold " + Record.CycleThreshold + ", period " + Record.CyclePeriod +
                ", repeats " + Record.CycleRepeatsStored + " (" + (Record.CycleRepeats ? "yes" : "no") + ")"),
            new DetailField("Low detail emitter (opcode 17)", DetailText.OrAbsent(Record.LowDetailEmitterId)),
            new DetailField("Fade colour (opcode 18)", Record.FadeColour == 0 ? "no ramp" : Argb(Record.FadeColour)),
            new DetailField("Minimum detail level (opcode 19)", Record.MinimumDetailLevel.ToString()),
            new DetailField("Fade percentages (opcodes 20/21)",
                "colour " + Record.FadeColourPercent + "%, alpha " + Record.FadeAlphaPercent + "%"),
            new DetailField("Speed ramp (opcodes 22/23)",
                DetailText.OrAbsent(Record.EndSpeed) + " over " + Record.SpeedRampPercent + "%"),
            new DetailField("Attached effector keys (opcode 25)", DetailText.Ids(Record.AttachedEffectorKeys)),
            new DetailField("Size ramp (opcodes 27/28)",
                DetailText.OrAbsent(Record.EndSizeStored) + " over " + Record.SizeRampPercent + "%"),
            new DetailField("Unread short (opcode 29)", Record.Opcodes.Has(29) ? Record.UnusedShort29.ToString() : "not stored"),
            new DetailField("Flags", Flags.Length == 0 ? "none" : Flags),
            new DetailField("Stored opcode order", OpcodeOrder)
        };

        private static string Range(int min, int max) {
            return min == max ? min.ToString() : min + " to " + max;
        }

        internal static string Argb(int packed) {
            return "0x" + packed.ToString("X8");
        }
    }

    /// <summary>One particle effector from group 1 of index 27 as a list row.</summary>
    public sealed class ParticleEffectorListing : IParticleListing {
        /// <summary>Binds one decoded effector to where it came from.</summary>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public ParticleEffectorListing(DefinitionAddress address, ParticleEffectorDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public ParticleEffectorDefinition Record { get; }

        /// <summary>The effector id, which is its file id in group 1.</summary>
        public int EffectorId => Record.Id;

        /// <summary>
        ///     How the effector is reached, flagged when it registers globally.
        /// </summary>
        /// <remarks>
        ///     Mode 2 is the one an emitter's opcode 10 can resolve, because that is the value which
        ///     puts the effector into the client's 16-slot global registry. Any other value is
        ///     reachable only by an emitter searching the scene for it.
        /// </remarks>
        public string Mode =>
            Record.Mode == ParticleEffectorDefinition.GlobalMode
                ? Record.Mode + " (global)"
                : Record.Mode.ToString();

        /// <summary>
        ///     Opcode 4's second field, which divides the falloff law rather than scaling the force.
        /// </summary>
        /// <remarks>
        ///     Shown as stored. The client replaces a 0 with a 1 at load to avoid dividing by zero
        ///     (Class66.java:257-259), which is a repair rather than a stored value, so correcting it
        ///     here would show a number the file does not hold.
        /// </remarks>
        public int Strength => Record.Strength;

        /// <summary>The force vector the effector applies, unless it is radial.</summary>
        public string Direction => Record.DirectionX + ", " + Record.DirectionY + ", " + Record.DirectionZ;

        /// <summary>Which distance law weakens the force.</summary>
        public int Falloff => Record.FalloffMode;

        /// <summary>The cone half-angle as stored.</summary>
        public int ConeAngle => Record.ConeAngleStored;

        /// <summary>The bare flags the record carries, as the opcode numbers that set them.</summary>
        public string Flags {
            get {
                var set = new List<string>();
                if (Record.HasUnhandledFlag5)
                    set.Add("5");
                if (Record.HasUnhandledFlag7)
                    set.Add("7");
                if (Record.MovesPositionRatherThanVelocity)
                    set.Add("8 moves-position");
                if (Record.IsRadial)
                    set.Add("9 radial");
                if (Record.IsInverted)
                    set.Add("10 inverted");
                return string.Join(" ", set);
            }
        }

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        public string OpcodeOrder => DetailText.Order(Record.Opcodes);

        /// <inheritdoc/>
        public string Summary =>
            "Effector " + EffectorId + " - mode " + Mode + " - order " + OpcodeOrder;

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields => new[] {
            new DetailField("Cone angle, stored (opcode 1)", ConeAngle.ToString()),
            new DetailField("Unread byte (opcode 2)", Record.Opcodes.Has(2) ? Record.UnusedByte2.ToString() : "not stored"),
            new DetailField("Force vector (opcode 3)", Direction),
            new DetailField("Falloff mode (opcode 4)", Falloff.ToString()),
            new DetailField("Falloff divisor (opcode 4)", Strength.ToString()),
            new DetailField("Mode (opcode 6)", Mode),
            new DetailField("Flags", Flags.Length == 0 ? "none" : Flags),
            new DetailField("Stored opcode order", OpcodeOrder)
        };
    }

    /// <summary>
    ///     Group 0 of index 27 as a definition list: one flat row per particle emitter.
    /// </summary>
    /// <remarks>
    ///     Scoped to a single group, because index 27 holds two unrelated families in two groups the
    ///     way index 2 and index 28 do. The base <c>Enumerate</c> walks the whole index, which here
    ///     would feed effector payloads to the emitter decoder.
    ///     <para>
    ///     Read only. The record round-trips, but almost every field of it is one member of a
    ///     multi-field opcode - the four angle bounds share opcode 1, the duty cycle shares opcode 16
    ///     - and the size bounds are stored through one of two aliased opcodes whose choice a cell
    ///     cannot express. The detail pane below the list shows every value the record carries.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEmitterListDescriptor : DefinitionListDescriptor<ParticleEmitterListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every emitter the index declares.</summary>
        public ParticleEmitterListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Emitter", row => row.EmitterId, 90),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Material", row => row.MaterialId, 90),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Speed", row => row.Speed, 110),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Spawn rate", row => row.SpawnRate, 110),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Lifetime", row => row.Lifetime, 120),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Size", row => row.Size, 140),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Colours", row => row.Colours, 200),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Effectors", row => row.Effectors, 90),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Flags", row => row.Flags, 220),
                DefinitionColumn.ReadOnly<ParticleEmitterListing>("Opcodes", row => row.OpcodeOrder, 200)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.CONFIG_PARTICLES;

        /// <inheritdoc/>
        public override string RowNoun => "emitter";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            return ParticleEnumeration.Group(cache, IndexId, ParticleEmitterDefinition.GroupId, Address);
        }

        /// <inheritdoc/>
        public override ParticleEmitterListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new ParticleEmitterDefinition { Id = address.FileId };
            record.Decode(payload);
            return new ParticleEmitterListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ParticleEmitterListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }

    /// <summary>
    ///     Group 1 of index 27 as a definition list: one flat row per particle effector.
    /// </summary>
    /// <remarks>
    ///     Group scoped and read only for the same reasons as
    ///     <see cref="ParticleEmitterListDescriptor"/>; the direction is three fields of one opcode.
    /// </remarks>
    public sealed class ParticleEffectorListDescriptor : DefinitionListDescriptor<ParticleEffectorListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every effector the index declares.</summary>
        public ParticleEffectorListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Effector", row => row.EffectorId, 90),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Mode", row => row.Mode, 110),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Divisor", row => row.Strength, 90),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Force", row => row.Direction, 150),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Falloff", row => row.Falloff, 80),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Cone angle", row => row.ConeAngle, 100),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Flags", row => row.Flags, 220),
                DefinitionColumn.ReadOnly<ParticleEffectorListing>("Opcodes", row => row.OpcodeOrder, 160)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.CONFIG_PARTICLES;

        /// <inheritdoc/>
        public override string RowNoun => "effector";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            return ParticleEnumeration.Group(cache, IndexId, ParticleEffectorDefinition.GroupId, Address);
        }

        /// <inheritdoc/>
        public override ParticleEffectorListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new ParticleEffectorDefinition { Id = address.FileId };
            record.Decode(payload);
            return new ParticleEffectorListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ParticleEffectorListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }

    /// <summary>Shared group-scoped enumeration for the two particle descriptors.</summary>
    internal static class ParticleEnumeration {
        /// <summary>
        ///     Every file one group of index 27 declares.
        /// </summary>
        /// <remarks>
        ///     From the reference table rather than a counted walk. The file count of this index
        ///     differs between the two supported caches, so nothing here may assume a range.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="indexId">The index id.</param>
        /// <param name="groupId">The group within it.</param>
        /// <param name="address">Builds the address, so the descriptor's own id rules apply.</param>
        /// <returns>The addresses to load.</returns>
        internal static IEnumerable<DefinitionAddress> Group(RSCache cache, int indexId, int groupId,
            Func<int, int, DefinitionAddress> address) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach (int file in cache.GetFileIds(indexId, groupId))
                yield return address(groupId, file);
        }
    }
}
