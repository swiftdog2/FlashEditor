using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Config;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     One animation an NPC's render animation set names, and what the client plays it for.
    /// </summary>
    /// <remarks>
    ///     The label is the field's meaning rather than its opcode number, because the point of the
    ///     list is to answer "which of these is the walk". Every label here is settled by what the
    ///     client does with the field, not by its position in the record - see
    ///     <see cref="RenderAnimationDefinition"/>, which cites the selector line for each.
    /// </remarks>
    public sealed class NpcAnimation {
        /// <summary>Names one animation of the set.</summary>
        /// <param name="label">What the client plays it for.</param>
        /// <param name="animationId">The index-20 animation id.</param>
        public NpcAnimation(string label, int animationId)
            : this(label, animationId, -1) {
        }

        /// <summary>Names one animation, and which skeleton it was found to animate.</summary>
        /// <remarks>
        ///     The skeleton is carried so that an entry offered by
        ///     <see cref="AnimationSkeletonIndex"/> can say <b>why</b> it was offered. That list is a
        ///     heuristic, and an entry with no visible reason for being there is indistinguishable
        ///     from one the editor is asserting the NPC plays - which is exactly the reading the
        ///     filter must not invite.
        /// </remarks>
        /// <param name="label">What the client plays it for, or empty when nothing in the cache says.</param>
        /// <param name="animationId">The index-20 animation id.</param>
        /// <param name="skeletonId">The index-1 skeleton, or -1 when it was never resolved.</param>
        public NpcAnimation(string label, int animationId, int skeletonId) {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            AnimationId = animationId;
            SkeletonId = skeletonId;
        }

        /// <summary>What the client plays this animation for, or empty when nothing states it.</summary>
        public string Label { get; }

        /// <summary>The index-20 animation id.</summary>
        public int AnimationId { get; }

        /// <summary>The index-1 skeleton this animation's first frame names, or -1.</summary>
        public int SkeletonId { get; }

        /// <summary>The label, the id and the skeleton, which is what the selector shows.</summary>
        /// <returns>The entry as one line.</returns>
        public override string ToString() {
            if (SkeletonId < 0)
                return Label + " (" + AnimationId + ")";

            //A sequence reached through the skeleton filter has no label, because the cache says
            //nothing about what it is for. Leading with the id rather than an empty pair of
            //brackets is what keeps the line readable.
            return Label.Length == 0
                ? "Animation " + AnimationId + " (skeleton " + SkeletonId + ")"
                : Label + " (" + AnimationId + ", skeleton " + SkeletonId + ")";
        }
    }

    /// <summary>
    ///     Resolves the animations an NPC definition names, through its render animation set.
    /// </summary>
    /// <remarks>
    ///     <b>An NPC record names no animation directly.</b> Its only route to one is opcode 127,
    ///     <c>NPCDefinition.renderTypeID</c>, which the client resolves to a <c>Class294</c> record
    ///     in JS5 index 2 group <see cref="ConfigGroup.RenderAnimation"/>
    ///     (<c>Particle_Sub3_Sub4_Sub2.method3039</c>, :828-844). That record holds the idle, walk,
    ///     run and turn animation ids, and this walks it into a list the viewport can cycle.
    ///     <para>
    ///     Only the fields that are set are listed. A render animation leaves most of its ids at -1,
    ///     and an entry for every field would bury the two or three the NPC actually plays under
    ///     thirty rows reading "-1" - which is the shape that makes a selector useless rather than
    ///     informative.
    ///     </para>
    /// </remarks>
    public static class NpcAnimationSet {
        /// <summary>
        ///     The animations one NPC's render animation set names, in playback-usefulness order.
        /// </summary>
        /// <remarks>
        ///     Idle first, then the walk set, then the run set, then turns - the order someone
        ///     inspecting an NPC wants rather than the record's own opcode order, which is not even
        ///     ascending in 579 of the 1,972 records.
        ///     <para>
        ///     Returns an empty list rather than throwing when the NPC names no set, when the set is
        ///     not in the cache, or when it will not decode. All three are ordinary states of an
        ///     editor pointed at a cache, and the caller puts the reason on the status line.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="npc">The NPC.</param>
        /// <param name="reason">Why the list is empty, or an empty string when it is not.</param>
        /// <returns>The animations, which may be empty.</returns>
        public static IReadOnlyList<NpcAnimation> For(RSCache cache, NPCDefinition npc, out string reason) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (npc == null)
                throw new ArgumentNullException(nameof(npc));

            if (npc.renderTypeID < 0) {
                reason = "NPC " + npc.id + " names no render animation set (opcode 127 absent).";
                return Array.Empty<NpcAnimation>();
            }

            RenderAnimationDefinition record;
            try {
                JagStream payload = cache.ReadFile(RSConstants.CONFIG,
                    ConfigGroup.RenderAnimation, npc.renderTypeID);
                record = new RenderAnimationDefinition { Id = npc.renderTypeID };
                record.Decode(payload);
            }
            catch (Exception failure) {
                reason = "Render animation " + npc.renderTypeID + " could not be read: " + failure.Message;
                return Array.Empty<NpcAnimation>();
            }

            List<NpcAnimation> animations = new List<NpcAnimation>();

            Add(animations, "Idle", record.IdleAnimationId);

            //The pool is what the client draws from when the idle id is -1, so each entry is a real
            //idle the NPC plays rather than an alternative spelling of one that is already listed.
            int[] pool = record.IdlePoolAnimationIds ?? Array.Empty<int>();
            for (int i = 0; i < pool.Length; i++)
                Add(animations, "Idle pool " + i, pool[i]);

            Add(animations, "Walk forward", record.WalkForwardAnimationId);
            Add(animations, "Walk at 90", record.WalkAt90AnimationId);
            Add(animations, "Walk at 180", record.WalkAt180AnimationId);
            Add(animations, "Walk at 270", record.WalkAt270AnimationId);

            Add(animations, "Run forward", record.RunForwardAnimationId);
            Add(animations, "Run at 90", record.RunAt90AnimationId);
            Add(animations, "Run at 180", record.RunAt180AnimationId);
            Add(animations, "Run at 270", record.RunAt270AnimationId);

            //The default set, used whenever neither the walk nor the run set applies.
            Add(animations, "Move forward", record.MoveForwardAnimationId);
            Add(animations, "Move at 90", record.MoveAt90AnimationId);
            Add(animations, "Move at 180", record.MoveAt180AnimationId);
            Add(animations, "Move at 270", record.MoveAt270AnimationId);

            Add(animations, "Turn on spot -", record.TurnOnSpotNegativeAnimationId);
            Add(animations, "Turn on spot +", record.TurnOnSpotPositiveAnimationId);
            Add(animations, "Walk turn -", record.WalkTurnNegativeAnimationId);
            Add(animations, "Walk turn +", record.WalkTurnPositiveAnimationId);
            Add(animations, "Run turn -", record.RunTurnNegativeAnimationId);
            Add(animations, "Run turn +", record.RunTurnPositiveAnimationId);
            Add(animations, "Move turn -", record.MoveTurnNegativeAnimationId);
            Add(animations, "Move turn +", record.MoveTurnPositiveAnimationId);

            reason = animations.Count == 0
                ? "Render animation " + npc.renderTypeID + " names no animation at all."
                : string.Empty;

            return animations;
        }

        /// <summary>Adds an entry unless the field is unset.</summary>
        /// <remarks>
        ///     -1 is the record's own "not set", and 65535 is how the two halves of opcode 1 spell it
        ///     on disk - the decoder already maps that back, so only -1 reaches here. Nothing else is
        ///     filtered, because the record draws no further distinction: an id the cache has no
        ///     animation for is a fact about the cache, and the viewport's own status line says so
        ///     when the animation is picked.
        /// </remarks>
        /// <param name="into">The list being built.</param>
        /// <param name="label">What the client plays it for.</param>
        /// <param name="animationId">The id, or -1.</param>
        private static void Add(List<NpcAnimation> into, string label, int animationId) {
            if (animationId >= 0)
                into.Add(new NpcAnimation(label, animationId));
        }
    }
}
