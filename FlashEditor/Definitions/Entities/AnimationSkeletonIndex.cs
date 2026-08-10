using System;
using System.Collections.Generic;
using System.Threading;
using FlashEditor.Cache;
using FlashEditor.Definitions.Animation;
using FlashEditor.IO;
using FlashEditor.Rendering;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Which skeleton each index-20 sequence animates, so a sequence can be offered against a
    ///     model that it will actually deform.
    /// </summary>
    /// <remarks>
    ///     <b>The cache contains no link from an NPC to its attack animation, and this is the nearest
    ///     honest substitute.</b> An NPC record names exactly one animation-valued thing - opcode
    ///     127, the render animation set (<c>Class141.java:420-421</c>) - and that set holds only
    ///     idle, walk, run and turn (<c>Class294.method3476</c>). Graardor's smash is chosen by the
    ///     server: packet 6 mask <c>0x10</c> carries four sequence ids to
    ///     <c>Particle_Sub3_Sub4_Sub2.anInt6413</c> (<c>Class21_Sub2.java:279-293</c>,
    ///     <c>Class282.java:16-42</c>), and the only writer of that field in the whole client is the
    ///     packet handler. No opcode, no varbit transform and no CS2 script associates the two.
    ///     <para>
    ///     So the editor cannot list "this NPC's attacks". What it can do is narrow 3,526 sequences
    ///     to the ones built for the same skeleton, which is what decides whether an animation
    ///     deforms a model into a pose or into a knot.
    ///     </para>
    ///     <para>
    ///     <b>This is a heuristic, and the client does not agree with it.</b> Frames bind to a model
    ///     by bone-label index, not by a stored skeleton id: a model carries no skeleton reference at
    ///     all, and the client applies whatever frame it is handed without checking anything. A
    ///     mismatched skeleton does not fail, it produces garbage. Matching skeleton ids is therefore
    ///     a strong filter on plausibility and never a guarantee - and the UI has to say so, because
    ///     a filtered list reads as a list of correct answers.
    ///     </para>
    ///     <para>
    ///     <b>Built lazily and once.</b> Resolving a sequence's skeleton means reading the first
    ///     frame of its frame group, and there are 3,526 groups holding 359,931 frames between them.
    ///     That is a background job with progress, not something to do while a dropdown opens, and
    ///     the result is held for the life of the loaded cache.
    ///     </para>
    /// </remarks>
    public sealed class AnimationSkeletonIndex {
        private readonly Dictionary<int, int> skeletonBySequence = new Dictionary<int, int>();

        private AnimationSkeletonIndex() {
        }

        /// <summary>How many sequences were examined.</summary>
        public int SequenceCount => skeletonBySequence.Count;

        /// <summary>
        ///     The skeleton a sequence animates, or -1 when it could not be determined.
        /// </summary>
        /// <param name="sequenceId">The index-20 sequence id.</param>
        /// <returns>The index-1 skeleton id, or -1.</returns>
        public int SkeletonOf(int sequenceId) =>
            skeletonBySequence.TryGetValue(sequenceId, out int skeleton) ? skeleton : -1;

        /// <summary>Every sequence built for one skeleton, in ascending id order.</summary>
        /// <param name="skeletonId">The index-1 skeleton id.</param>
        /// <returns>The sequence ids.</returns>
        public IReadOnlyList<int> SequencesFor(int skeletonId) =>
            SequencesFor(new[] { skeletonId });

        /// <summary>
        ///     Every sequence built for any of a set of skeletons, in ascending id order.
        /// </summary>
        /// <remarks>
        ///     <b>A set rather than one skeleton, because an NPC's own animations do not all share
        ///     one.</b> The first version of this took a single id, derived from the NPC's idle
        ///     animation, and a sweep over every NPC in the vanilla capture found render sets that
        ///     name animations on a different skeleton from their own idle - NPC 3284 names
        ///     animation 8326 on skeleton 1750 while its idle is on another. Filtering to the idle's
        ///     skeleton therefore hid animations the cache itself says that NPC plays, which is the
        ///     one thing a filter must never do.
        /// </remarks>
        /// <param name="skeletonIds">The index-1 skeleton ids to accept.</param>
        /// <returns>The sequence ids.</returns>
        public IReadOnlyList<int> SequencesFor(IEnumerable<int> skeletonIds) {
            var matches = new List<int>();
            if (skeletonIds == null)
                return matches;

            var wanted = new HashSet<int>();
            foreach (int skeleton in skeletonIds)
                if (skeleton >= 0)
                    wanted.Add(skeleton);

            if (wanted.Count == 0)
                return matches;

            foreach (KeyValuePair<int, int> entry in skeletonBySequence)
                if (wanted.Contains(entry.Value))
                    matches.Add(entry.Key);

            matches.Sort();
            return matches;
        }

        /// <summary>
        ///     The distinct skeletons a set of sequences animates.
        /// </summary>
        /// <remarks>
        ///     What a caller builds an NPC's filter from: hand it the animations the render set
        ///     names and it returns every skeleton those touch, which is the set the NPC
        ///     demonstrably animates rather than a guess from one of them.
        /// </remarks>
        /// <param name="sequenceIds">The sequences to look up.</param>
        /// <returns>The skeleton ids, without duplicates and without the unresolved.</returns>
        public IReadOnlyCollection<int> SkeletonsOf(IEnumerable<int> sequenceIds) {
            var skeletons = new HashSet<int>();
            if (sequenceIds == null)
                return skeletons;

            foreach (int sequence in sequenceIds) {
                int skeleton = SkeletonOf(sequence);
                if (skeleton >= 0)
                    skeletons.Add(skeleton);
            }

            return skeletons;
        }

        /// <summary>
        ///     Reads every sequence and records the skeleton its first frame names.
        /// </summary>
        /// <remarks>
        ///     The first frame rather than all of them. A sequence's frames come from one frame group
        ///     in practice and a group has one skeleton, so reading more would cost 359,931 frame
        ///     decodes to answer a question the first frame already answers. A sequence whose frames
        ///     genuinely spanned skeletons would be mis-summarised here, which is why
        ///     <see cref="SkeletonOf"/> is documented as the skeleton of the first frame rather than
        ///     of the sequence.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="frames">Where frames are read from, so its cache is shared.</param>
        /// <param name="report">Called with a percentage as the sweep runs, or null.</param>
        /// <param name="cancel">Stops the sweep between groups.</param>
        /// <returns>The index.</returns>
        /// <exception cref="OperationCanceledException">The token was signalled.</exception>
        public static AnimationSkeletonIndex Build(RSCache cache, IAnimationDataSource frames,
            Action<int>? report = null, CancellationToken cancel = default) {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            var index = new AnimationSkeletonIndex();

            /* The id split is asked for rather than written here. Index 20 pages 128 records to a
               group, which CacheAddressing already states with the client citation behind it
               (Class301.java:207-208 and Class163.java:143); a second copy of "<< 7" in this file
               would be a place for the two to drift apart. */
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ANIMATIONS_INDEX);

            var groups = new List<int>();
            try {
                /* Table-driven, like every other sweep here. An idx-driven reading would pick up
                   the repack's undeclared groups, which the client gates out and cannot reach. */
                foreach (int group in cache.EnumerateGroups(RSConstants.ANIMATIONS_INDEX))
                    groups.Add(group);
            }
            catch (Exception) {
                return index;
            }

            int done = 0;
            int lastPercent = -1;

            foreach (int group in groups) {
                /* Checked here rather than left to the caller. Without a token the only point this
                   sweep hands control back is the progress callback, so a caller wanting to stop it
                   had to throw from inside that - which works only while the callback is invoked
                   outside every try in this loop, and silently stops working for any caller that
                   passes no callback at all. */
                cancel.ThrowIfCancellationRequested();

                /* One group read rather than one read per file. RSCache.ReadFile releases the group
                   as soon as it has handed back the file it was asked for, so walking a 128-file
                   config group file by file re-inflates it 128 times. */
                IReadOnlyDictionary<int, JagStream> files;
                try {
                    files = cache.ReadGroup(RSConstants.ANIMATIONS_INDEX, group);
                }
                catch (Exception) {
                    continue;
                }

                foreach (KeyValuePair<int, JagStream> file in files) {
                    if (file.Value == null)
                        continue;

                    int sequenceId = addressing.DefinitionId(group, file.Key);

                    try {
                        var sequence = new AnimationDefinition();
                        sequence.Decode(file.Value);

                        if (sequence.FrameIds.Length == 0)
                            continue;

                        FrameDefinition? frame = frames.GetFrame(sequence.FrameIds[0]);
                        if (frame != null)
                            index.skeletonBySequence[sequenceId] = frame.SkeletonId;
                    }
                    catch (Exception) {
                        //A sequence that will not decode has no skeleton to record. It stays out of
                        //the index and therefore out of every filtered list, which is the right
                        //answer for a record nothing can play.
                    }
                }

                done++;

                //Reported on percent boundaries. A progress call per group marshals to the UI thread
                //3,526 times to move a bar by fractions of a percent.
                int percent = groups.Count == 0 ? 100 : done * 100 / groups.Count;
                if (percent != lastPercent) {
                    lastPercent = percent;
                    report?.Invoke(percent);
                }
            }

            return index;
        }
    }
}
