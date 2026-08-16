using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Animation;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Billboards;
using FlashEditor.Definitions.ClientScripts;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Defaults;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Enums;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.LoadingScreens;
using FlashEditor.Definitions.LoadingSprites;
using FlashEditor.Definitions.Particles;
using FlashEditor.Definitions.QuickChat;
using FlashEditor.Definitions.SpotAnims;
using FlashEditor.Definitions.VarBits;
using FlashEditor.Definitions.WorldMap;

namespace FlashEditor.Export {
    /// <summary>How an index is written out.</summary>
    public enum ExportCoverage {
        /// <summary>Every record decoded to JSON.</summary>
        Structured,

        /// <summary>
        ///     Only what the reference table and the idx file state: ids, sizes, CRCs, versions.
        /// </summary>
        /// <remarks>
        ///     What every index whose payload is a binary asset gets. The bytes are the asset and no
        ///     amount of JSON around them makes them queryable, so the export names them and their
        ///     lengths and leaves the payload alone.
        /// </remarks>
        Manifest,

        /// <summary>The cache declares no reference table for this index.</summary>
        Absent
    }

    /// <summary>
    ///     Which indexes are decoded, which are named and left alone, and why.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The split is the export's main editorial decision, so it is stated in one table rather
    ///     than spread through the writer. An index is <see cref="ExportCoverage.Manifest"/> when its
    ///     payload is an asset - geometry, pixels, audio, native code, compiled shaders - because
    ///     that content answers no query in JSON form, and because dumping it would make the export
    ///     an order of magnitude larger than the part anyone reads.
    ///     </para>
    ///     <para>
    ///     Every reason is written into the export's own manifest, so a reader who wonders why an
    ///     index has no records does not have to come back to this file to find out.
    ///     </para>
    /// </remarks>
    public static class CacheExportPlan {
        /// <summary>Why an index is written as a manifest rather than decoded.</summary>
        private static readonly Dictionary<int, string> ManifestReasons = new Dictionary<int, string> {
            [RSConstants.MAPS_INDEX] =
                "Map squares. The group list, its map-square names and its encryption state are" +
                " reported; the 64x64 terrain tiles and the location placements inside each square" +
                " are not expanded, because that is millions of records serving a view the map tab" +
                " already gives.",
            [RSConstants.MUSIC_INDEX] = "Packed MIDI tracks. Audio payload.",
            [RSConstants.SPRITES_INDEX] = "Sprite pixels. Image payload.",
            [RSConstants.HUFFMAN_INDEX] = "The chat Huffman code table. A code table, not id-bearing data.",
            [RSConstants.MUSIC_2] = "Packed MIDI jingles. Audio payload. This table carries no name hashes at all.",
            [RSConstants.SFX2_INDEX] = "Vorbis samples, with the setup header and codebooks in group 0. Audio payload.",
            [RSConstants.NATIVE_LIBRARIES] = "Native libraries. Executable payload.",
            [RSConstants.GRAPHICS_SHADERS] = "Shader programs. Compiled shader payload."
        };

        /// <summary>Indexes written as a manifest, in ascending order.</summary>
        public static IReadOnlyCollection<int> ManifestIndexes => ManifestReasons.Keys;

        /// <summary>Why an index carries no records, or null when it is decoded.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>The reason, or null.</returns>
        public static string? ManifestReason(int indexId) {
            return ManifestReasons.TryGetValue(indexId, out string? reason) ? reason : null;
        }

        /// <summary>
        ///     The definition-list descriptors that decode an index, or an empty list when none do.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Reusing the editor's own descriptors rather than restating each index's addressing and
        ///     decode here. They already state, per index, what to enumerate and how to read one
        ///     record, and they are what the tabs are built from - so an index the editor can show is
        ///     an index this can export, and the two cannot drift apart.
        ///     </para>
        ///     <para>
        ///     Several indexes take more than one descriptor because they hold more than one record
        ///     type: index 2 is thirty-five unrelated config families, 27 splits emitters from
        ///     effectors, 28 splits scene defaults from the hitsplat layout, 33 splits the manifest
        ///     from the screens, and 24 and 25 are each a complete quick-chat bank with its menus in
        ///     one group and its messages in another.
        ///     </para>
        ///     <para>
        ///     Index 7 is absent here on purpose. <see cref="ModelListDescriptor"/> reads no payload
        ///     at all, so it would export a list of ids; the export decodes models itself, keeps the
        ///     emitter, effector and billboard references from the footer, and throws the geometry
        ///     away.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index.</param>
        /// <returns>The descriptors, in the order their sections are written.</returns>
        public static IReadOnlyList<IDefinitionListDescriptor> DescriptorsFor(int indexId) {
            switch (indexId) {
                case RSConstants.FRAMES_INDEX:
                    return One(new FrameSetListDescriptor());
                case RSConstants.SKINS:
                    return One(new SkeletonListDescriptor());
                case RSConstants.CONFIG:
                    return ConfigDescriptors();
                case RSConstants.INTERFACE_DEFINITIONS_INDEX:
                    return One(new InterfaceComponentListDescriptor());
                case RSConstants.SOUND_EFFECTS:
                    return One(new SoundEffectListDescriptor());
                case RSConstants.CLIENT_SCRIPTS_INDEX:
                    return One(new ClientScriptListDescriptor());
                case RSConstants.FONTS_INDEX:
                    return One(new FontListDescriptor());
                case RSConstants.OBJECTS_DEFINITIONS_INDEX:
                    return One(new ObjectListDescriptor());
                case RSConstants.CLIENTSCRIPT_SETTINGS:
                    return One(new EnumListDescriptor());
                case RSConstants.NPC_DEFINITIONS_INDEX:
                    return One(new NPCListDescriptor());
                case RSConstants.ITEM_DEFINITIONS_INDEX:
                    return One(new ItemListDescriptor());
                case RSConstants.ANIMATIONS_INDEX:
                    return One(new AnimationListDescriptor());
                case RSConstants.GRAPHICS_INDEX:
                    return One(new GraphicListDescriptor());
                case RSConstants.SCRIPT_CONFIGS:
                    return One(new VarBitListDescriptor());
                case RSConstants.WORLD_MAP:
                    return One(new WorldMapAreaListDescriptor());
                case RSConstants.QUICK_CHAT_MESSAGES:
                case RSConstants.QUICK_CHAT_MENU:
                    return new IDefinitionListDescriptor[] {
                        new QuickChatMenuListDescriptor(indexId),
                        new QuickChatMessageListDescriptor(indexId)
                    };
                case RSConstants.CONFIG_PARTICLES:
                    return new IDefinitionListDescriptor[] {
                        new ParticleEmitterListDescriptor(), new ParticleEffectorListDescriptor()
                    };
                case RSConstants.DEFAULTS:
                    return new IDefinitionListDescriptor[] {
                        new SceneDefaultsListDescriptor(), new HitsplatLayoutListDescriptor()
                    };
                case RSConstants.CONFIG_BILLBOARD:
                    return One(new BillboardListDescriptor());
                case RSConstants.LOADING_SPRITES:
                    return One(new LoadingSpriteListDescriptor());
                case RSConstants.GAME_TIPS:
                    return new IDefinitionListDescriptor[] {
                        new LoadingScreenManifestListDescriptor(), new LoadingScreenListDescriptor()
                    };
                default:
                    return Array.Empty<IDefinitionListDescriptor>();
            }
        }

        /// <summary>One descriptor per index 2 family, in group order.</summary>
        /// <returns>Thirty-five descriptors, one for every group index 2 declares.</returns>
        private static IReadOnlyList<IDefinitionListDescriptor> ConfigDescriptors() {
            var descriptors = new List<IDefinitionListDescriptor>();
            foreach (ConfigFamily family in ConfigFamily.Modelled)
                descriptors.Add(new ConfigListDescriptor(family));
            return descriptors;
        }

        /// <summary>Wraps a single descriptor as a list.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <returns>The one-element list.</returns>
        private static IReadOnlyList<IDefinitionListDescriptor> One(IDefinitionListDescriptor descriptor) {
            return new[] { descriptor };
        }
    }
}
