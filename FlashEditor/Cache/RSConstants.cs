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
            SFX2_INDEX = 14, //VORBIS/midi instruments
            SFX3_INDEX = 15,
            OBJECTS_DEFINITIONS_INDEX = 16,
            CLIENTSCRIPT_SETTINGS = 17,
            NPC_DEFINITIONS_INDEX = 18,
            ITEM_DEFINITIONS_INDEX = 19,
            ANIMATIONS_INDEX = 20,
            GRAPHICS_INDEX = 21,
            SCRIPT_CONFIGS = 22, //aka varbits
            WORLD_MAP = 23,
            QUICK_CHAT_MESSAGES = 24,
            QUICK_CHAT_MENU = 25,
            MATERIALS = 26,
            CONFIG_PARTICLES = 27, //map effects
            DEFAULTS = 28, //fonts?
            CONFIG_BILLBOARD = 29,
            NATIVE_LIBRARIES = 30,
            GRAPHICS_SHADERS = 31,
            LOADING_SPRITES = 32, //in jpg format
            GAME_TIPS = 33, //loading screens
            LOADING_SPRITES_RAW = 34, //in jagex format
            THEORA_AKA_CUTSCENES = 35,
            VORBIS = 36,
            META_INDEX = 255;

        public static string[] containerNames = new [] {"FRAMES",
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
            "SFX3",
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

        internal static string GetContainerNameForType(int type) {
            if(type >= containerNames.Length) {
                if(type == META_INDEX)
                    return "CRCTABLE";
                else
                    return "NULL";
            } else {
                return containerNames[type];
            }
        }

        /*
         * General constants
         */
        public const int CLIENT_BUILD = 639;
        public const bool ENCRYPTED_CACHE = true;
        public const int MAX_VALID_ARCHIVE_LENGTH = 1000000;
        public const string CACHE_DIRECTORY =           "C:/Users/CJ/Desktop/RSPS/Hydra/cache/";
        public static string CACHE_OUTPUT_DIRECTORY  =  "C:/Users/CJ/Desktop/RSPS/Hydra/cache2/";
        public const string CACHE_ORIGINAL_COPY =           "C:/Users/CJ/Desktop/RSPS/Hydra/cache0/";

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
