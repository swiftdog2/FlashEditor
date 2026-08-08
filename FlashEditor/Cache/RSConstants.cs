using System;

namespace FlashEditor.cache {
    public static class RSConstants {
        /*
         * Compression constants
         */
        public const int NO_COMPRESSION = 0,
            BZIP2_COMPRESSION = 1,
            GZIP_COMPRESSION = 2;

        /*
         * Index Constants
         */
        public const int FRAMES_INDEX = 0,
            SKINS = 1,
            CONFIG = 2,
            INTERFACE_DEFINITIONS_INDEX = 3,
            SOUND_EFFECTS = 4,
            MAPS_INDEX = 5,
            MUSIC_INDEX = 6,
            MODELS_INDEX = 7,
            SPRITES_INDEX = 8,
            TEXTURES = 9,
            HUFFMAN_INDEX = 10,
            MUSIC_2 = 11,
            CLIENT_SCRIPTS_INDEX = 12,
            FONTS_INDEX = 13,
            //Vorbis sound-effect samples. Not the MIDI instrument bank - that is index 15, which
            //Particle_Sub3_Sub5_Sub2.java:99-100 hands to the synth alongside this one and index 4.
            SFX2_INDEX = 14,
            //The MIDI patch bank, not a third sound-effect bank: 176 groups of one file, each a
            //sparse 128-key instrument. Class355.method3875 (Class355.java:15-19) fetches a group
            //by program number and Node_Sub31_Sub2.java:1141-1146 is its only caller. The name was
            //SFX3_INDEX and contradicted the comment two lines above it; it had no adoption site
            //anywhere, so nothing but this line changed.
            MIDI_PATCH_INDEX = 15,
            OBJECTS_DEFINITIONS_INDEX = 16,
            //The enum table, not client-script settings. The client's own field for the store it
            //opens here is enumFileStore (Node_Sub10_Sub24.java:9); an enum id splits 256 to a
            //group. The name is left alone because renaming it is another change's job.
            CLIENTSCRIPT_SETTINGS = 17,
            NPC_DEFINITIONS_INDEX = 18,
            ITEM_DEFINITIONS_INDEX = 19,
            ANIMATIONS_INDEX = 20,
            GRAPHICS_INDEX = 21,
            SCRIPT_CONFIGS = 22, //aka varbits: one bit range of one varp per file, 1024 files a group
            WORLD_MAP = 23,
            QUICK_CHAT_MESSAGES = 24,
            QUICK_CHAT_MENU = 25,
            MATERIALS = 26,
            CONFIG_PARTICLES = 27, //map effects
            //Not fonts, which are index 13, and not the sprite ids and colours AGENTS.md's index map
            //claims: group 1 is the default environment cube map plus the player-title enum tables
            //(Class276.method3284), group 3 the hitsplat slot layout plus the benchmark model id
            //(Class155.method2495). Two groups, ids 1 and 3, one file each.
            DEFAULTS = 28,
            CONFIG_BILLBOARD = 29,
            NATIVE_LIBRARIES = 30,
            GRAPHICS_SHADERS = 31,
            LOADING_SPRITES = 32, //in jpg format
            GAME_TIPS = 33, //loading screens
            LOADING_SPRITES_RAW = 34, //in jagex format
            THEORA_AKA_CUTSCENES = 35,
            VORBIS = 36,
            META_INDEX = 255;

        /*
         * Groups within the CONFIG index (2). These are groups, not indexes: an earlier spec
         * recorded floor overlays as "index 4" and underlays as "index 3", which are unrelated
         * archives. Verified against Class153.java:201,241 and Class32.java:79,129, both of which
         * operate on client.BIT_CONFIG = index 2.
         */
        public const int FLOOR_UNDERLAY_GROUP = 1,
            FLOOR_OVERLAY_GROUP = 4,
            MAP_SCENE_GROUP = 34,
            MAP_ELEMENT_GROUP = 36;

        public static string[] indexNames = new [] {"FRAMES",
            "SKINS",
            "CONFIG",
            "INTERFACE_DEFINITIONS",
            "SOUND_EFFECTS",
            "MAPS",
            "MUSIC",
            "MODELS",
            "SPRITES",
            "TEXTURES",
            "HUFFMAN",
            "MUSIC_2",
            "CLIENT_SCRIPTS",
            "FONTS",
            "SFX2",
            "MIDI_PATCH",
            "OBJECTS_DEFINITIONS",
            "CLIENTSCRIPT_SETTINGS",
            "NPC_DEFINITIONS",
            "ITEM_DEFINITIONS",
            "ANIMATIONS",
            "GRAPHICS",
            "SCRIPT_CONFIGS",
            "WORLD_MAP",
            "QUICK_CHAT_MESSAGES",
            "QUICK_CHAT_MENU",
            "MATERIALS",
            "CONFIG_PARTICLES",
            "DEFAULTS",
            "CONFIG_BILLBOARD",
            "NATIVE_LIBRARIES",
            "GRAPHICS_SHADERS",
            "LOADING_SPRITES",
            "GAME_TIPS",
            "LOADING_SPRITES_RAW",
            "THEORA_AKA_CUTSCENES",
            "VORBIS"};

        /// <summary>
        /// Return a short, user-friendly name for a cache index.
        /// </summary>
        /// <remarks>
        /// This is mainly for GUI display (tab titles, labels, logging).
        /// If you add new indexes later, extend the <c>switch</c>.
        /// </remarks>

        internal static string GetIndexName(int indexId) {
            if(indexId >= indexNames.Length) {
                if(indexId == META_INDEX)
                    return "CRCTABLE";
                else
                    return "NULL";
            } else {
                return indexNames[indexId];
            }
        }

        /*
         * General constants
         */
        public const bool ENCRYPTED_CACHE = true;
        public const int MAX_VALID_ARCHIVE_LENGTH = 1000000;

        /*
         * These three were compile-time literals naming one machine, and a const string is inlined
         * into every caller, so there was no way to point the editor anywhere else without a
         * rebuild. They now resolve at runtime through CachePaths, which searches for a cache and
         * for its key file and keeps these literals only as the last fallback. They stay here as
         * forwarding properties so nothing that already names them has to change.
         */

        /// <summary>The cache being read when the user has chosen none. See <see cref="CachePaths.Input"/>.</summary>
        public static string CACHE_DIRECTORY => CachePaths.Input;

        /// <summary>Where edits and item exports are written. See <see cref="CachePaths.Output"/>.</summary>
        public static string CACHE_OUTPUT_DIRECTORY => CachePaths.Output;

        /// <summary>The untouched copy the revert button reloads. See <see cref="CachePaths.Pristine"/>.</summary>
        public static string CACHE_ORIGINAL_COPY => CachePaths.Pristine;

        /*
         * Configuration sub-archive details
         */

        /*
        Archive 1: Floor underlay
        Archive 3: Identikit
        Archive 4: Floor overlay
        Archive 5: Inventories
        Archive 6: Empty (Pre 488: Locations)
        Archive 7: Unknown (Server sided only)
        Archive 8: Empty (Pre 488: Enums)
        Archive 9: Empty (Pre 488: Npcs)
        Archive 10: Empty (Pre 488: Objects)
        Archive 11: Params
        Archive 12: Empty (Pre 488: Sequences)
        Archive 13: Empty (Pre 488: Spotanim)
        Archive 14: Empty (Pre 488: Var Bit)
        Archive 15: Empty (Pre 745: Var Client Strings)
        Archive 16: Empty (Pre 745: Var Player)
        Archive 18: Areas (Server sided only)
        Archive 19: Empty (Pre 745: Var Client)
        Archive 26: Empty (Pre 763: Structs)
        Archive 29: Skyboxes
        Archive 30: Sun definitions (Archive is empty)
        Archive 31: Light intensity
        Archive 32: Render anims
        Archive 33: Cursors
        Archive 34: Mapscenes
        Archive 35: Quests
        Archive 36: Worldmap info
        Archive 40: Database Tables (Server sided only)
        Archive 41: Database Rows (Server sided only)
        Archive 42: Unknown (Server sided only)
        Archive 46: Hitmarks
        Archive 47: Empty (Pre 745: Var Clan)
        Archive 48: Item Codes (Server sided only)
        Archive 49: Categories (Server sided only)
        Archive 54: Empty (Pre 745: Var Clan Settings)
        Archive 60: Var Player
        Archive 61: Var Npc
        Archive 62: Var Client
        Archive 63: Var World (Server sided only)
        Archive 64: Var Region (Server sided only)
        Archive 65: Var Object (Server sided only)
        Archive 66: Var Clan
        Archive 67: Var Clan Setting
        Archive 68: Unknown Var related (Server sided only)
        Archive 69: Var Bit
        Archive 70: Game log event (Server sided only)
        Archive 72: Hitbars
        Archive 73: Unknown (Server sided only)
        Archive 75: Unknown Var related (Server sided only)
        Archive 76: Unknown (Server sided only)
        Archive 77: Anim flow control
        Archive 80: Var Group
        */
    }
}
