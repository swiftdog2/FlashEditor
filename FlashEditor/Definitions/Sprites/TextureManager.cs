using FlashEditor;
using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// Loads all texture definitions from the materials index (index 26).
    /// The Hydra client stores texture metadata in a single columnar file
    /// (archive 0, file 0 of the MATERIALS index), read by Class260.
    /// </summary>
    public class TextureManager {
        private readonly RSCache cache;
        public static readonly SortedDictionary<int, TextureDefinition> Textures = new();
        private static readonly Bitmap _fallbackThumb = new Bitmap(100, 100);

        /// <summary>
        /// Raw bytes of the entire columnar file for lossless round-trip.
        /// </summary>
        public static byte[] RawIndexData;

        public TextureManager(RSCache cache) {
            this.cache = cache;
        }

        public static void Clear() {
            foreach (var def in Textures.Values)
                def?.Dispose();
            Textures.Clear();
            RawIndexData = null;
        }

        public void Load() {
            Clear();

            // Step 1: Load Materials metadata (index 26) — the full set of texture IDs
            try {
                LoadFromMaterialsIndex();
            } catch (Exception ex) {
                Debug($"TextureManager: MATERIALS index unavailable ({ex.Message})", LOG_DETAIL.BASIC);
            }

            // Step 2: Load sprite references from TEXTURES (index 9) — merges into existing entries
            LoadFromTextureIndex();

            Debug($"Loaded {Textures.Count} texture definitions total", LOG_DETAIL.BASIC);
        }

        private void LoadFromMaterialsIndex() {
            RSContainer container = cache.GetContainer(RSConstants.MATERIALS, 0);
            if (container == null || container.GetStream() == null) {
                Debug("TextureManager: no materials container at archive 0", LOG_DETAIL.BASIC);
                return;
            }

            JagStream data = container.GetStream();
            data.Seek0();

            // Store raw data for round-trip
            RawIndexData = new byte[data.Length];
            data.Read(RawIndexData, 0, RawIndexData.Length);
            data.Seek0();
            container.ReleaseData();

            DecodeColumnar(new JagStream(RawIndexData));

            Debug($"Loaded {Textures.Count} texture definitions from MATERIALS index", LOG_DETAIL.BASIC);
        }

        private void LoadFromTextureIndex() {
            RSReferenceTable table;
            try {
                table = cache.GetReferenceTable(RSConstants.TEXTURES);
            } catch (Exception ex) {
                Debug($"TextureManager: TEXTURES reference table unavailable: {ex.Message}", LOG_DETAIL.BASIC);
                return;
            }

            // Textures in index 9 are stored as multiple files within archive 0.
            // Each file = one texture definition. The file ID = texture ID.
            // Texture.Decode extracts sprite file IDs referenced from SPRITES (index 8).
            int loaded = 0, errors = 0, withSprites = 0;
            foreach (var (archiveId, archiveEntry) in table.GetArchiveEntries()) {
                try {
                    int[] fileIds = archiveEntry.GetValidFileIds();
                    if (fileIds.Length == 0) continue;

                    RSContainer container = cache.GetContainer(RSConstants.TEXTURES, archiveId);
                    if (container == null || container.GetStream() == null)
                        continue;

                    RSArchive archive = RSCache.GetArchive(container, fileIds);

                    foreach (int fileId in fileIds) {
                        try {
                            JagStream stream = archive.GetFile(fileId);
                            if (stream == null) continue;
                            stream.Seek0();

                            Texture tex = Texture.Decode(stream);
                            int textureId = fileId;

                            // Merge into existing entry from Materials, or create new
                            var def = Textures.ContainsKey(textureId) ? Textures[textureId] : new TextureDefinition { id = textureId };
                            def.spriteFileIds = tex.FileIds;

                            // Load the first sprite as a thumbnail for the GUI
                            if (tex.Count > 0) {
                                try {
                                    SpriteDefinition sprite = cache.GetSprite(tex.FileIds[0]);
                                    if (sprite != null && sprite.GetFrameCount() > 0) {
                                        var frame = sprite.GetFrame(0);
                                        if (frame?.thumb != null)
                                            def.thumb = new Bitmap(frame.thumb);
                                    }
                                } catch (Exception ex) {
                                    Debug($"TextureManager: failed to load sprite for texture {textureId}: {ex.Message}", LOG_DETAIL.ADVANCED);
                                }
                            }

                            Textures[textureId] = def;
                            loaded++;
                            if (tex.Count > 0) withSprites++;
                        } catch (Exception ex) {
                            Debug($"TextureManager: error decoding texture file {fileId} in archive {archiveId}: {ex.Message}", LOG_DETAIL.ADVANCED);
                            errors++;
                        }
                    }

                    container.ReleaseData();
                } catch (Exception ex) {
                    Debug($"TextureManager: error loading texture archive {archiveId}: {ex.Message}", LOG_DETAIL.ADVANCED);
                    errors++;
                }
            }

            Debug($"LoadFromTextureIndex: loaded {loaded} textures ({withSprites} with sprite IDs), {errors} errors", LOG_DETAIL.BASIC);
        }

        /// <summary>
        /// Decodes the columnar texture definition format from the materials index.
        /// Matches the Class260 constructor in the Hydra client.
        /// </summary>
        public static void DecodeColumnar(JagStream s) {
            int count = s.ReadUnsignedShort();
            var defs = new TextureDefinition[count];

            // Pass 0: existence flags
            for (int i = 0; i < count; i++) {
                if (s.ReadUnsignedByte() == 1)
                    defs[i] = new TextureDefinition { id = i };
            }

            // Pass 1: field1825 — boolean, true when byte == 0 (inverted)
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1825 = s.ReadUnsignedByte() == 0;

            // Pass 2: field1822 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1822 = s.ReadUnsignedByte() == 1;

            // Pass 3: field1833 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1833 = s.ReadUnsignedByte() == 1;

            // Pass 4: field1829 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1829 = s.ReadSignedByte();

            // Pass 5: field1830 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1830 = s.ReadSignedByte();

            // Pass 6: field1820 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1820 = s.ReadSignedByte();

            // Pass 7: field1816 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1816 = s.ReadSignedByte();

            // Pass 8: field1831 — unsigned short
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1831 = s.ReadUnsignedShort();

            // Pass 9: field1823 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1823 = s.ReadSignedByte();

            // Pass 10: field1837 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1837 = s.ReadSignedByte();

            // Pass 11: field1827 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1827 = s.ReadUnsignedByte() == 1;

            // Pass 12: field1824 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1824 = s.ReadUnsignedByte() == 1;

            // Pass 13: field1832 — signed byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1832 = s.ReadSignedByte();

            // Pass 14: field1826 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1826 = s.ReadUnsignedByte() == 1;

            // Pass 15: field1819 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1819 = s.ReadUnsignedByte() == 1;

            // Pass 16: field1817 — boolean, true when byte == 1
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1817 = s.ReadUnsignedByte() == 1;

            // Pass 17: field1821 — unsigned byte (stored as int)
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1821 = s.ReadUnsignedByte();

            // Pass 18: field1835 — full 4-byte int
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1835 = s.ReadInt();

            // Pass 19: field1818 — unsigned byte (stored as int)
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    defs[i].field1818 = s.ReadUnsignedByte();

            // Populate the dictionary
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    Textures[i] = defs[i];
        }

        /// <summary>
        /// Encodes all texture definitions back into the columnar format.
        /// Returns a JagStream ready for writing to MATERIALS index archive 0.
        /// </summary>
        public static JagStream EncodeColumnar() {
            // If we have raw data and nothing was modified, use it directly
            if (RawIndexData != null) {
                var raw = new JagStream(RawIndexData.Length);
                raw.Write(RawIndexData, 0, RawIndexData.Length);
                raw.Flip();
                return raw;
            }

            return EncodeFromFields();
        }

        /// <summary>
        /// Encodes from field values (used when textures have been edited).
        /// </summary>
        public static JagStream EncodeFromFields() {
            // Determine the count — highest texture ID + 1
            int count = 0;
            foreach (var kvp in Textures)
                if (kvp.Key >= count)
                    count = kvp.Key + 1;

            var s = new JagStream();
            s.WriteShort(count);

            // Build dense array for iteration
            var defs = new TextureDefinition[count];
            foreach (var kvp in Textures)
                defs[kvp.Key] = kvp.Value;

            // Pass 0: existence flags
            for (int i = 0; i < count; i++)
                s.WriteByte((byte)(defs[i] != null ? 1 : 0));

            // Pass 1: field1825 — inverted boolean (true → 0, false → 1)
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1825 ? 0 : 1));

            // Pass 2: field1822
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1822 ? 1 : 0));

            // Pass 3: field1833
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1833 ? 1 : 0));

            // Pass 4: field1829
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1829);

            // Pass 5: field1830
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1830);

            // Pass 6: field1820
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1820);

            // Pass 7: field1816
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1816);

            // Pass 8: field1831 — unsigned short
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteShort(defs[i].field1831);

            // Pass 9: field1823
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1823);

            // Pass 10: field1837
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1837);

            // Pass 11: field1827
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1827 ? 1 : 0));

            // Pass 12: field1824
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1824 ? 1 : 0));

            // Pass 13: field1832
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteSignedByte(defs[i].field1832);

            // Pass 14: field1826
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1826 ? 1 : 0));

            // Pass 15: field1819
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1819 ? 1 : 0));

            // Pass 16: field1817
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)(defs[i].field1817 ? 1 : 0));

            // Pass 17: field1821 — unsigned byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].field1821);

            // Pass 18: field1835 — full int
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteInteger(defs[i].field1835);

            // Pass 19: field1818 — unsigned byte
            for (int i = 0; i < count; i++)
                if (defs[i] != null)
                    s.WriteByte((byte)defs[i].field1818);

            s.Flip();
            return s;
        }

        internal static Image GetThumbnailForTexture(string key) {
            if (int.TryParse(key, out int id) && Textures.TryGetValue(id, out var def) && def.thumb != null)
                return def.thumb;

            return _fallbackThumb;
        }
    }
}
