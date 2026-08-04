using System.Collections.Generic;
using System;
using FlashEditor.Definitions.Particles;
using FlashEditor.cache;

namespace FlashEditor.Rendering
{
    public interface IParticleDataSource
    {
        ParticleEmitterDefinition? GetEmitter(int emitterId);

        ParticleEffectorDefinition? GetEffector(int effectorId);
    }

    public sealed class CacheParticleDataSource : IParticleDataSource
    {
        private readonly RSCache cache;

        private Dictionary<int, ParticleEmitterDefinition>? emitters;

        private Dictionary<int, ParticleEffectorDefinition>? effectors;

        public int LoadedEmitters => emitters?.Count ?? (-1);

        public int LoadedEffectors => effectors?.Count ?? (-1);

        public CacheParticleDataSource(RSCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException("cache");
        }

        public void Clear()
        {
            emitters = null;
            effectors = null;
        }

        public ParticleEmitterDefinition? GetEmitter(int emitterId)
        {
            if (emitters == null)
            {
                emitters = ReadEmitters();
            }
            ParticleEmitterDefinition? value;
            return emitters.TryGetValue(emitterId, out value) ? value : null;
        }

        public ParticleEffectorDefinition? GetEffector(int effectorId)
        {
            if (effectors == null)
            {
                effectors = ReadEffectors();
            }
            ParticleEffectorDefinition? value;
            return effectors.TryGetValue(effectorId, out value) ? value : null;
        }

        private Dictionary<int, ParticleEmitterDefinition> ReadEmitters()
        {
            Dictionary<int, ParticleEmitterDefinition> dictionary = new Dictionary<int, ParticleEmitterDefinition>();
            foreach (KeyValuePair<int, JagStream> item in ReadGroup(0))
            {
                try
                {
                    dictionary[item.Key] = new ParticleEmitterDefinition
                    {
                        Id = item.Key
                    }.Decode(item.Value);
                }
                catch (Exception)
                {
                }
            }
            return dictionary;
        }

        private Dictionary<int, ParticleEffectorDefinition> ReadEffectors()
        {
            Dictionary<int, ParticleEffectorDefinition> dictionary = new Dictionary<int, ParticleEffectorDefinition>();
            foreach (KeyValuePair<int, JagStream> item in ReadGroup(1))
            {
                try
                {
                    dictionary[item.Key] = new ParticleEffectorDefinition
                    {
                        Id = item.Key
                    }.Decode(item.Value);
                }
                catch (Exception)
                {
                }
            }
            return dictionary;
        }

        private IReadOnlyDictionary<int, JagStream> ReadGroup(int group)
        {
            try
            {
                return cache.ReadGroup(27, group);
            }
            catch (Exception)
            {
                return new Dictionary<int, JagStream>();
            }
        }
    }

    public sealed class InMemoryParticleDataSource : IParticleDataSource
    {
        private readonly Dictionary<int, ParticleEmitterDefinition> emitters = new Dictionary<int, ParticleEmitterDefinition>();

        private readonly Dictionary<int, ParticleEffectorDefinition> effectors = new Dictionary<int, ParticleEffectorDefinition>();

        public InMemoryParticleDataSource AddEmitter(int emitterId, ParticleEmitterDefinition emitter)
        {
            emitters[emitterId] = emitter ?? throw new ArgumentNullException("emitter");
            return this;
        }

        public InMemoryParticleDataSource AddEffector(int effectorId, ParticleEffectorDefinition effector)
        {
            effectors[effectorId] = effector ?? throw new ArgumentNullException("effector");
            return this;
        }

        public ParticleEmitterDefinition? GetEmitter(int emitterId)
        {
            ParticleEmitterDefinition? value;
            return emitters.TryGetValue(emitterId, out value) ? value : null;
        }

        public ParticleEffectorDefinition? GetEffector(int effectorId)
        {
            ParticleEffectorDefinition? value;
            return effectors.TryGetValue(effectorId, out value) ? value : null;
        }
    }
}
