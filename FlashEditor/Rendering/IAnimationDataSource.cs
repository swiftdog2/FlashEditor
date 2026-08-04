using System.Collections.Generic;
using System;
using FlashEditor.Definitions.Animation;
using FlashEditor.cache;

namespace FlashEditor.Rendering
{
    public interface IAnimationDataSource
    {
        FrameDefinition? GetFrame(int packedFrameId);

        SkeletonDefinition? GetSkeleton(int skeletonId);
    }

    public sealed class CacheAnimationDataSource : IAnimationDataSource
    {
        private readonly RSCache cache;

        private readonly Dictionary<int, Dictionary<int, FrameDefinition>> frameSets = new Dictionary<int, Dictionary<int, FrameDefinition>>();

        private readonly Dictionary<int, SkeletonDefinition?> skeletons = new Dictionary<int, SkeletonDefinition?>();

        public int CachedFrameSets => frameSets.Count;

        public int CachedSkeletons => skeletons.Count;

        public CacheAnimationDataSource(RSCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException("cache");
        }

        public void Clear()
        {
            frameSets.Clear();
            skeletons.Clear();
        }

        public FrameDefinition? GetFrame(int packedFrameId)
        {
            if (packedFrameId < 0)
            {
                return null;
            }
            int num = AnimationDefinition.FrameGroupOf(packedFrameId);
            int key = AnimationDefinition.FrameIndexOf(packedFrameId);
            if (!frameSets.TryGetValue(num, out Dictionary<int, FrameDefinition>? value))
            {
                value = ReadFrameSet(num);
                frameSets[num] = value;
            }
            FrameDefinition? value2;
            return value.TryGetValue(key, out value2) ? value2 : null;
        }

        public SkeletonDefinition? GetSkeleton(int skeletonId)
        {
            if (skeletonId < 0)
            {
                return null;
            }
            if (skeletons.TryGetValue(skeletonId, out SkeletonDefinition? value))
            {
                return value;
            }
            SkeletonDefinition? skeletonDefinition = ReadSkeleton(skeletonId);
            skeletons[skeletonId] = skeletonDefinition;
            return skeletonDefinition;
        }

        private Dictionary<int, FrameDefinition> ReadFrameSet(int group)
        {
            Dictionary<int, FrameDefinition> dictionary = new Dictionary<int, FrameDefinition>();
            IReadOnlyDictionary<int, JagStream> readOnlyDictionary;
            try
            {
                readOnlyDictionary = cache.ReadGroup(0, group);
            }
            catch (Exception)
            {
                return dictionary;
            }
            foreach (KeyValuePair<int, JagStream> item in readOnlyDictionary)
            {
                try
                {
                    dictionary[item.Key] = new FrameDefinition
                    {
                        Id = AnimationDefinition.PackFrame(group, item.Key)
                    }.Decode(item.Value);
                }
                catch (Exception)
                {
                }
            }
            return dictionary;
        }

        private SkeletonDefinition? ReadSkeleton(int skeletonId)
        {
            IReadOnlyDictionary<int, JagStream> readOnlyDictionary;
            try
            {
                readOnlyDictionary = cache.ReadGroup(1, skeletonId);
            }
            catch (Exception)
            {
                return null;
            }
            using (IEnumerator<KeyValuePair<int, JagStream>> enumerator = readOnlyDictionary.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    KeyValuePair<int, JagStream> current = enumerator.Current;
                    try
                    {
                        return new SkeletonDefinition
                        {
                            Id = skeletonId
                        }.Decode(current.Value);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
            return null;
        }
    }

    public sealed class InMemoryAnimationDataSource : IAnimationDataSource
    {
        private readonly Dictionary<int, FrameDefinition> frames = new Dictionary<int, FrameDefinition>();

        private readonly Dictionary<int, SkeletonDefinition> skeletons = new Dictionary<int, SkeletonDefinition>();

        public InMemoryAnimationDataSource AddFrame(int packedFrameId, FrameDefinition frame)
        {
            frames[packedFrameId] = frame ?? throw new ArgumentNullException("frame");
            return this;
        }

        public InMemoryAnimationDataSource AddSkeleton(int skeletonId, SkeletonDefinition skeleton)
        {
            skeletons[skeletonId] = skeleton ?? throw new ArgumentNullException("skeleton");
            return this;
        }

        public FrameDefinition? GetFrame(int packedFrameId)
        {
            FrameDefinition? value;
            return frames.TryGetValue(packedFrameId, out value) ? value : null;
        }

        public SkeletonDefinition? GetSkeleton(int skeletonId)
        {
            SkeletonDefinition? value;
            return skeletons.TryGetValue(skeletonId, out value) ? value : null;
        }
    }
}
