#nullable disable

using FlashEditor.cache;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Cache.Util.Crypto {
    /// <summary>
    /// A lookup of XTEA decryption keys, resolved by index and archive id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a handful of indexes are encrypted in practice - in revision 639 that is
    /// the map index (<see cref="RSConstants.MAPS_INDEX"/>), whose archives are keyed
    /// by map square. Key dumps in the wild therefore usually record a map square /
    /// region id and omit the index entirely, so an entry with no explicit index is
    /// assumed to belong to the map index.
    /// </para>
    /// <para>
    /// The loader is deliberately tolerant, because there is no single agreed key-file
    /// format. All of the following are accepted:
    /// </para>
    /// <code>
    /// [ { "region":    12345, "keys": [k0, k1, k2, k3] }, ... ]
    /// [ { "mapsquare": 12345, "key":  [k0, k1, k2, k3] }, ... ]
    /// [ { "index": 5, "archive": 12345, "keys": [k0, k1, k2, k3] }, ... ]
    /// { "12345": [k0, k1, k2, k3], ... }
    /// </code>
    /// <para>
    /// A key of <c>[0, 0, 0, 0]</c> means "not encrypted" and is discarded on load, so
    /// it never reaches <see cref="RSContainer"/> as a pointless decrypt pass.
    /// </para>
    /// </remarks>
    public class XTEAKeyTable {
        /// <summary>Candidate file names probed by <see cref="FindKeyFile"/>, in priority order.</summary>
        private static readonly string[] CandidateFileNames = {
            "xteas.json", "xtea.json", "keys.json", "xteakeys.json"
        };

        /// <summary>Subdirectories probed by <see cref="FindKeyFile"/>, relative to the search root.</summary>
        private static readonly string[] CandidateSubdirectories = { "", "xteas", "keys" };

        /// <summary>Keys by packed index/archive id. See <see cref="PackKey"/>.</summary>
        private readonly Dictionary<long, int[]> keys = new Dictionary<long, int[]>();

        /// <summary>The number of keys held in this table.</summary>
        public int Count => keys.Count;

        /// <summary>
        /// Combines an index id and an archive id into a single dictionary key.
        /// </summary>
        private static long PackKey(int indexId, int archiveId) {
            return ((long) indexId << 32) | (uint) archiveId;
        }

        /// <summary>
        /// Returns true when every word of the key is zero, which by convention means
        /// the archive is not actually encrypted.
        /// </summary>
        private static bool IsEmptyKey(int[] key) {
            return key[0] == 0 && key[1] == 0 && key[2] == 0 && key[3] == 0;
        }

        /// <summary>
        /// Records a key for the given index/archive pair. Empty keys are ignored.
        /// </summary>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive the key decrypts.</param>
        /// <param name="key">Four 32-bit key words.</param>
        public void SetKey(int indexId, int archiveId, int[] key) {
            if (key == null || key.Length != 4 || IsEmptyKey(key))
                return;

            keys[PackKey(indexId, archiveId)] = key;
        }

        /// <summary>
        /// Retrieves the key for the given index/archive pair.
        /// </summary>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive to decrypt.</param>
        /// <returns>Four 32-bit key words, or null when no key is held.</returns>
        public int[] GetKey(int indexId, int archiveId) {
            return keys.TryGetValue(PackKey(indexId, archiveId), out int[] key) ? key : null;
        }

        /// <summary>
        /// Searches for a key file near the cache directory.
        /// </summary>
        /// <remarks>
        /// Probes <paramref name="cacheDir"/> and its parent, each combined with the
        /// subdirectories in <see cref="CandidateSubdirectories"/>, for the file names
        /// in <see cref="CandidateFileNames"/>.
        /// </remarks>
        /// <param name="cacheDir">The directory the cache was loaded from.</param>
        /// <returns>The full path to the first key file found, or null if there is none.</returns>
        public static string FindKeyFile(string cacheDir) {
            if (string.IsNullOrWhiteSpace(cacheDir))
                return null;

            List<string> roots = new List<string> { cacheDir };

            try {
                DirectoryInfo parent = Directory.GetParent(cacheDir.TrimEnd('/', '\\'));
                if (parent != null)
                    roots.Add(parent.FullName);
            } catch (Exception ex) {
                Debug("Could not resolve parent of " + cacheDir + ": " + ex.Message, LOG_DETAIL.ADVANCED);
            }

            foreach (string root in roots)
                foreach (string subdirectory in CandidateSubdirectories)
                    foreach (string fileName in CandidateFileNames) {
                        string candidate = Path.Combine(root, subdirectory, fileName);
                        if (File.Exists(candidate))
                            return candidate;
                    }

            return null;
        }

        /// <summary>
        /// Loads a key table from a JSON file. See the remarks on
        /// <see cref="XTEAKeyTable"/> for the accepted shapes.
        /// </summary>
        /// <param name="filePath">Path to the JSON key file.</param>
        /// <returns>
        /// The parsed table. Never null - a file that is missing, malformed, or holds
        /// no usable keys yields an empty table rather than throwing, so that a bad key
        /// file degrades to "no keys" instead of preventing the cache from opening.
        /// </returns>
        public static XTEAKeyTable LoadFromFile(string filePath) {
            XTEAKeyTable table = new XTEAKeyTable();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) {
                Debug("XTEA key file not found: " + filePath);
                return table;
            }

            try {
                JToken root = JToken.Parse(File.ReadAllText(filePath));

                if (root is JArray array)
                    table.LoadFromArray(array);
                else if (root is JObject obj)
                    table.LoadFromObject(obj);
                else
                    Debug("Unrecognised XTEA key file structure: " + filePath);
            } catch (JsonException ex) {
                Debug("Malformed XTEA key file " + filePath + ": " + ex.Message);
            } catch (IOException ex) {
                Debug("Could not read XTEA key file " + filePath + ": " + ex.Message);
            }

            return table;
        }

        /// <summary>
        /// Loads the array-of-entries shapes, where each element names an archive and
        /// carries its key.
        /// </summary>
        private void LoadFromArray(JArray array) {
            foreach (JToken element in array) {
                if (element is not JObject entry)
                    continue;

                int[] key = ReadKeyArray(entry["keys"] ?? entry["key"]);
                if (key == null)
                    continue;

                JToken archiveToken;
                JToken indexToken;

                /* OpenRS2 exports use "archive" for the *index* and "group" for the archive
                   id within it. Reading "archive" as the archive id there collapses an entire
                   dump onto archive 5 of index 5. A "group" member is the marker for that
                   dialect, because no other dumped shape carries one. */
                JToken groupToken = entry["group"];
                if (groupToken != null && groupToken.Type == JTokenType.Integer) {
                    archiveToken = groupToken;
                    indexToken = entry["archive"] ?? entry["index"];
                }
                else {
                    // "region" and "mapsquare" are map-index archive ids under other names.
                    archiveToken = entry["archive"] ?? entry["region"] ?? entry["mapsquare"] ?? entry["id"];
                    indexToken = entry["index"];
                }

                if (archiveToken == null || archiveToken.Type != JTokenType.Integer)
                    continue;

                int indexId = indexToken != null && indexToken.Type == JTokenType.Integer
                    ? (int) indexToken
                    : RSConstants.MAPS_INDEX;

                SetKey(indexId, (int) archiveToken, key);
            }
        }

        /// <summary>
        /// Loads the flat dictionary shape, where each property name is a map-index
        /// archive id and its value is the key array.
        /// </summary>
        private void LoadFromObject(JObject obj) {
            foreach (KeyValuePair<string, JToken> property in obj) {
                if (!int.TryParse(property.Key, out int archiveId))
                    continue;

                int[] key = ReadKeyArray(property.Value);
                if (key != null)
                    SetKey(RSConstants.MAPS_INDEX, archiveId, key);
            }
        }

        /// <summary>
        /// Reads a four-element key array from a JSON token.
        /// </summary>
        /// <returns>The key words, or null if the token is not four integers.</returns>
        private static int[] ReadKeyArray(JToken token) {
            if (token is not JArray array || array.Count != 4)
                return null;

            int[] key = new int[4];

            for (int i = 0; i < 4; i++) {
                if (array[i].Type != JTokenType.Integer)
                    return null;

                // Keys are frequently dumped as unsigned 32-bit values, which overflow
                // an int - read wide, then wrap to the signed representation XTEA uses.
                key[i] = unchecked((int) (long) array[i]);
            }

            return key;
        }
    }
}
