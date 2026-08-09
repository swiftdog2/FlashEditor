using System.Collections.Generic;
using System;
using FlashEditor.Definitions.Particles;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Where <see cref="ParticleSystem"/> gets its index-27 emitter and effector definitions.
    /// </summary>
    /// <remarks>
    ///     The same seam as <see cref="IAnimationDataSource"/> and for the same reason: the spawn
    ///     arithmetic is checked against values derived by hand from the client's formulae, and a
    ///     fixture that says "spawn exactly one particle a millisecond and never move it" is what
    ///     makes a particle count a statement about the spawn arithmetic alone.
    /// </remarks>
    public interface IParticleDataSource
    {
        /// <summary>Reads one emitter definition.</summary>
        /// <param name="emitterId">Its file id within index 27 group 0.</param>
        /// <returns>The emitter, or null when the cache does not hold it.</returns>
        ParticleEmitterDefinition? GetEmitter(int emitterId);

        /// <summary>Reads one effector definition.</summary>
        /// <param name="effectorId">Its file id within index 27 group 1.</param>
        /// <returns>The effector, or null when the cache does not hold it.</returns>
        ParticleEffectorDefinition? GetEffector(int effectorId);
    }

    /// <summary>Reads the two index-27 groups out of a real cache, once each.</summary>
    /// <remarks>
    ///     Index 27 is two groups holding 285 files between them, so both are decoded whole on first
    ///     use rather than per lookup. That is not the same trade as the frame cache: here the whole
    ///     index is small enough that decoding it all is cheaper than the bookkeeping to avoid it, and
    ///     a model's attachments are looked up in a burst when the model loads.
    /// </remarks>
    public sealed class CacheParticleDataSource : IParticleDataSource
    {
        /// <summary>The cache to read from. Opened read-only and never written by this type.</summary>
        private readonly RSCache cache;

        /// <summary>Every emitter in group 0, or null before the first lookup.</summary>
        /// <remarks>Null is "not read yet"; an empty dictionary is "read, and there was nothing".</remarks>
        private Dictionary<int, ParticleEmitterDefinition>? emitters;

        /// <summary>Every effector in group 1, or null before the first lookup.</summary>
        private Dictionary<int, ParticleEffectorDefinition>? effectors;

        /// <summary>How many emitters were decoded, or -1 if the group has not been read.</summary>
        /// <remarks>The -1 is what lets a panel distinguish "no emitters in the cache" from "not looked".</remarks>
        public int LoadedEmitters => emitters?.Count ?? -1;

        /// <summary>How many effectors were decoded, or -1 if the group has not been read.</summary>
        public int LoadedEffectors => effectors?.Count ?? -1;

        /// <summary>Creates a source over an open cache.</summary>
        /// <param name="cache">The cache.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
        public CacheParticleDataSource(RSCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>Drops everything decoded so far.</summary>
        /// <remarks>Call after an edit to index 27, or the viewport keeps simulating the old bytes.</remarks>
        public void Clear()
        {
            emitters = null;
            effectors = null;
        }

        /// <inheritdoc/>
        public ParticleEmitterDefinition? GetEmitter(int emitterId)
        {
            emitters ??= ReadEmitters();
            return emitters.TryGetValue(emitterId, out ParticleEmitterDefinition? emitter) ? emitter : null;
        }

        /// <inheritdoc/>
        public ParticleEffectorDefinition? GetEffector(int effectorId)
        {
            effectors ??= ReadEffectors();
            return effectors.TryGetValue(effectorId, out ParticleEffectorDefinition? effector) ? effector : null;
        }

        /// <summary>Decodes every emitter in index 27 group 0.</summary>
        /// <remarks>Per-file try, so one damaged emitter costs that emitter and not the whole group.</remarks>
        /// <returns>The emitters, keyed by file id.</returns>
        private Dictionary<int, ParticleEmitterDefinition> ReadEmitters()
        {
            Dictionary<int, ParticleEmitterDefinition> decoded = new Dictionary<int, ParticleEmitterDefinition>();

            foreach (KeyValuePair<int, JagStream> file in ReadGroup(EmitterGroup))
            {
                try
                {
                    decoded[file.Key] = new ParticleEmitterDefinition { Id = file.Key }.Decode(file.Value);
                }
                catch (Exception)
                {
                    //Left out, which the system counts as a missing emitter against the attachment.
                }
            }

            return decoded;
        }

        /// <summary>Decodes every effector in index 27 group 1.</summary>
        /// <returns>The effectors, keyed by file id.</returns>
        private Dictionary<int, ParticleEffectorDefinition> ReadEffectors()
        {
            Dictionary<int, ParticleEffectorDefinition> decoded = new Dictionary<int, ParticleEffectorDefinition>();

            foreach (KeyValuePair<int, JagStream> file in ReadGroup(EffectorGroup))
            {
                try
                {
                    decoded[file.Key] = new ParticleEffectorDefinition { Id = file.Key }.Decode(file.Value);
                }
                catch (Exception)
                {
                    //Left out, which the system counts as a missing effector against the attachment.
                }
            }

            return decoded;
        }

        /// <summary>Reads one index-27 group, or nothing when it is not there.</summary>
        /// <remarks>
        ///     An empty dictionary rather than a throw. A cache with no index 27 is a cache with no
        ///     particle effects, which the viewer should show as a model without them rather than as
        ///     an error.
        /// </remarks>
        /// <param name="group">The group id.</param>
        /// <returns>Its files, keyed by file id.</returns>
        private IReadOnlyDictionary<int, JagStream> ReadGroup(int group)
        {
            try
            {
                return cache.ReadGroup(RSConstants.CONFIG_PARTICLES, group);
            }
            catch (Exception)
            {
                return new Dictionary<int, JagStream>();
            }
        }

        /// <summary>Index-27 group holding the emitter definitions.</summary>
        private const int EmitterGroup = 0;

        /// <summary>Index-27 group holding the effector definitions.</summary>
        private const int EffectorGroup = 1;
    }

    /// <summary>A source holding emitters and effectors handed to it directly.</summary>
    /// <remarks>
    ///     What the particle tests build their fixtures from, and what a future editor preview would
    ///     use to simulate an edit that has not been saved yet.
    /// </remarks>
    public sealed class InMemoryParticleDataSource : IParticleDataSource
    {
        /// <summary>Emitters by id.</summary>
        private readonly Dictionary<int, ParticleEmitterDefinition> emitters =
            new Dictionary<int, ParticleEmitterDefinition>();

        /// <summary>Effectors by id.</summary>
        private readonly Dictionary<int, ParticleEffectorDefinition> effectors =
            new Dictionary<int, ParticleEffectorDefinition>();

        /// <summary>Adds or replaces an emitter.</summary>
        /// <param name="emitterId">The id a model attachment will name.</param>
        /// <param name="emitter">The definition.</param>
        /// <returns>This source, so a fixture reads as one expression.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="emitter"/> is null.</exception>
        public InMemoryParticleDataSource AddEmitter(int emitterId, ParticleEmitterDefinition emitter)
        {
            emitters[emitterId] = emitter ?? throw new ArgumentNullException(nameof(emitter));
            return this;
        }

        /// <summary>Adds or replaces an effector.</summary>
        /// <param name="effectorId">The id a model attachment or an emitter will name.</param>
        /// <param name="effector">The definition.</param>
        /// <returns>This source, so a fixture reads as one expression.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="effector"/> is null.</exception>
        public InMemoryParticleDataSource AddEffector(int effectorId, ParticleEffectorDefinition effector)
        {
            effectors[effectorId] = effector ?? throw new ArgumentNullException(nameof(effector));
            return this;
        }

        /// <inheritdoc/>
        public ParticleEmitterDefinition? GetEmitter(int emitterId)
        {
            return emitters.TryGetValue(emitterId, out ParticleEmitterDefinition? emitter) ? emitter : null;
        }

        /// <inheritdoc/>
        public ParticleEffectorDefinition? GetEffector(int effectorId)
        {
            return effectors.TryGetValue(effectorId, out ParticleEffectorDefinition? effector) ? effector : null;
        }
    }
}
