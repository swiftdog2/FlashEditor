using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     One animation from index 20 as a list row.
    /// </summary>
    /// <remarks>
    ///     The frame-set column is the reason this listing is worth having on its own: index 0 has no
    ///     name hashes, so the only statement anywhere in the cache of which frame set an animation
    ///     plays is the packed id inside its opcode 1. This surfaces it, which is what lets a frame
    ///     viewer be reached from an animation rather than from a bare group number.
    /// </remarks>
    public sealed class AnimationListing {
        /// <summary>Binds one decoded animation to where it came from.</summary>
        /// <param name="address">The group and file, and the animation id they carry.</param>
        /// <param name="record">The decoded record.</param>
        public AnimationListing(DefinitionAddress address, AnimationDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public AnimationDefinition Record { get; }

        /// <summary>The animation id.</summary>
        public int AnimationId => Record.Id;

        /// <summary>How many steps the animation plays.</summary>
        public int Frames => Record.FrameCount;

        /// <summary>How long one pass lasts, in client cycles.</summary>
        public int Cycles => Record.TotalDuration;

        /// <summary>
        ///     The index-0 frame sets this animation draws from, or nothing when it stores no frames.
        /// </summary>
        /// <remarks>
        ///     Almost every animation names one set, but nothing in the format requires it - the
        ///     packed id is per frame - so the distinct ids are listed rather than the first one.
        /// </remarks>
        public string FrameSets {
            get {
                var seen = new List<int>();
                foreach (int packed in Record.FrameIds) {
                    int group = AnimationDefinition.FrameGroupOf(packed);
                    if (!seen.Contains(group))
                        seen.Add(group);
                }
                return string.Join(",", seen);
            }
        }

        /// <summary>Which of two competing animations survives.</summary>
        public int Priority => Record.Priority;

        /// <summary>How many times the animation may loop.</summary>
        public int MaxLoops => Record.MaxLoops;

        /// <summary>How far the playhead winds back on a loop, or nothing when unstated.</summary>
        public object? FrameStep => Record.FrameStep < 0 ? null : Record.FrameStep;

        /// <summary>What happens when the animation is triggered while already playing.</summary>
        public int Retrigger => Record.RetriggerBehaviour;

        /// <summary>
        ///     The moving and stationary interrupt fields, stored value first and derived in brackets.
        /// </summary>
        /// <remarks>
        ///     Both are shown because the two disagree on most records: the client fills a -1 in from
        ///     whether opcode 3 was present, and an editor that showed only the filled-in value would
        ///     invite someone to "correct" a field the record never carried.
        /// </remarks>
        public string Interrupts {
            get {
                return Show(Record.MovingInterrupt, Record.EffectiveMovingInterrupt) + " / " +
                       Show(Record.StationaryInterrupt, Record.EffectiveStationaryInterrupt);
            }
        }

        /// <summary>How many skeleton labels this animation blends over another.</summary>
        public object? BlendLabels =>
            Record.Opcodes.Has(3) ? Record.BlendLabels.Length : null;

        /// <summary>How many frames carry a sound.</summary>
        public int SoundFrames {
            get {
                int frames = 0;
                foreach (int[] row in Record.FrameSounds)
                    if (row.Length > 0)
                        frames++;
                return frames;
            }
        }

        /// <summary>The bare flags the record carries, as the opcode numbers that set them.</summary>
        public string Flags {
            get {
                var set = new List<string>();
                if (Record.Stretches)
                    set.Add("14 stretch");
                if (Record.Tweens)
                    set.Add("15 tween");
                if (Record.TweensAcrossCachedFrames)
                    set.Add("16 tween-cached");
                if (Record.SoundsUseTheAlternateEmitter)
                    set.Add("18 alt-sound");
                return string.Join(" ", set);
            }
        }

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        /// <remarks>
        ///     Worth a column on this index above all others: over half its records are not in
        ///     ascending opcode order and five scalar opcodes repeat, so the stored order is the thing
        ///     an encoder replays rather than derives.
        /// </remarks>
        public string OpcodeOrder {
            get {
                var parts = new List<string>(Record.Opcodes.Count);
                for (int i = 0; i < Record.Opcodes.Count; i++)
                    parts.Add(Record.Opcodes[i].Opcode.ToString());
                return string.Join(",", parts);
            }
        }

        /// <summary>Renders a stored value beside the value the client derives from it.</summary>
        /// <param name="stored">What the record carried, or -1.</param>
        /// <param name="effective">What the client uses.</param>
        /// <returns>The stored value, with the derived one in brackets when they differ.</returns>
        private static string Show(int stored, int effective) {
            return stored < 0 ? "-(" + effective + ")" : stored.ToString();
        }
    }

    /// <summary>
    ///     Index 20 as a definition list: one flat row per animation.
    /// </summary>
    /// <remarks>
    ///     Read only. The codec re-encodes byte for byte, but the editable fields worth offering are
    ///     the ones this list cannot show in a cell - the frame table, the per-frame sound rows - and
    ///     an editor that let priority be typed while hiding the frames would be the wrong first
    ///     move. The panel refuses to write while <c>IsEditable</c> is false, so the two statements
    ///     cannot drift.
    /// </remarks>
    public sealed class AnimationListDescriptor : DefinitionListDescriptor<AnimationListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every animation the index declares.</summary>
        public AnimationListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<AnimationListing>("Animation", row => row.AnimationId, 90),
                DefinitionColumn.ReadOnly<AnimationListing>("Frames", row => row.Frames, 70),
                DefinitionColumn.ReadOnly<AnimationListing>("Cycles", row => row.Cycles, 70),
                DefinitionColumn.ReadOnly<AnimationListing>("Frame sets", row => row.FrameSets, 110),
                DefinitionColumn.ReadOnly<AnimationListing>("Priority", row => row.Priority, 70),
                DefinitionColumn.ReadOnly<AnimationListing>("Loops", row => row.MaxLoops, 70),
                DefinitionColumn.ReadOnly<AnimationListing>("Step", row => row.FrameStep, 70),
                DefinitionColumn.ReadOnly<AnimationListing>("Retrigger", row => row.Retrigger, 80),
                DefinitionColumn.ReadOnly<AnimationListing>("Interrupts", row => row.Interrupts, 100),
                DefinitionColumn.ReadOnly<AnimationListing>("Labels", row => row.BlendLabels, 70),
                DefinitionColumn.ReadOnly<AnimationListing>("Sound frames", row => row.SoundFrames, 100),
                DefinitionColumn.ReadOnly<AnimationListing>("Flags", row => row.Flags, 180),
                DefinitionColumn.ReadOnly<AnimationListing>("Opcodes", row => row.OpcodeOrder, 160)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.ANIMATIONS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "animation";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override AnimationListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new AnimationDefinition { Id = address.DefinitionId };
            record.Decode(payload);
            return new AnimationListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(AnimationListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }
}
