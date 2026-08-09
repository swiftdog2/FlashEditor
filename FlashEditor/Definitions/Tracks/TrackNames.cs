using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Tracks {
    /// <summary>
    ///     Display names for the music tracks in index 6.
    /// </summary>
    /// <remarks>
    ///     The reference table for index 6 carries a name hash per group, and that hash is the
    ///     track's own name: group 0's identifier is exactly the hash of <c>"scape main"</c>, which
    ///     is the name the client itself passes to index 6 (InterfaceSettings.java:216, on the
    ///     archive opened as index 6 at :164). The hash is one way, so a name can be confirmed but
    ///     never recovered. Turning it back into text needs a list of candidate names to hash.
    ///
    ///     That list is an enum, id 1345, which addresses index 17 group 5 file 65 - the client
    ///     splits an enum id as <c>group = id >>> 8</c>, <c>file = id &amp; 0xff</c>
    ///     (Class153.method2490 and Node_Sub10_Sub9.method1032, called from Class29.getEnum).
    ///     Index 17 is <see cref="RSConstants.CLIENTSCRIPT_SETTINGS"/> here, which is a misnomer:
    ///     the client's own field for it is <c>enumFileStore</c> (InterfaceSettings.java:172). The
    ///     constant is left alone because renaming it is another change's job, so every reference to
    ///     the enum store in this file reads as though it were about client scripts.
    ///
    ///     The join is by hash, not by key, and that distinction was measured rather than assumed.
    ///     The enum's keys look like index-6 group ids - 958 of its 970 keys land on a group that
    ///     exists - but they are not. Its values are in alphabetical order (Adventure, Al Kharid,
    ///     Alone, Ambient Jungle, ...), so the key is the track's position in the music player's
    ///     own list. Keying by it names group 0 "Adventure" when group 0's stored hash proves it is
    ///     "Scape Main", which the enum holds at key 150. Hashing the value instead cannot go wrong
    ///     that way: a name is only ever attached to a group whose stored hash it reproduces.
    ///
    ///     The price is coverage. 598 of the 963 groups are named; the other 365 carry a hash no
    ///     name in the enum reproduces. Pooling the strings of every enum in index 17 raises that
    ///     only to 626 and starts attaching names that are not music names at all, so it is not
    ///     worth the loss of meaning. Index 11 carries no identifiers, so no jingle is ever named.
    /// </remarks>
    public static class TrackNames {
        /// <summary>The enum id holding the music player's track names.</summary>
        public const int MusicNameEnumId = 1345;

        /// <summary>
        ///     Reads the music name enum and keys it by the archive name hash a track would carry.
        /// </summary>
        /// <remarks>
        ///     Keyed by hash rather than by group id so the caller joins on
        ///     <see cref="Track.NameHash"/>, which is what makes a wrong name impossible - see the
        ///     note on the class. Names are a display convenience, so a cache without the enum has
        ///     to degrade to unnamed tracks rather than failing the whole tab.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>Name hash to display name, possibly empty.</returns>
        public static Dictionary<int, string> Load(RSCache cache) {
            var byHash = new Dictionary<int, string>();

            JagStream data;
            try {
                data = cache.ReadFile(RSConstants.CLIENTSCRIPT_SETTINGS,
                    MusicNameEnumId >>> 8, MusicNameEnumId & 0xFF);
            }
            catch (IOException) {
                //Absent enum group or file. Either way there are no names to be had.
                return byHash;
            }

            if (data == null)
                return byHash;

            foreach (string name in ReadStringValues(data))
                byHash[NameHasher.GetNameHash(name)] = name;

            return byHash;
        }

        /// <summary>
        ///     Reads the string values out of an enum file, discarding the keys.
        /// </summary>
        /// <remarks>
        ///     Deliberately narrow: it understands the opcodes <c>GameConfig.extractEnumData</c>
        ///     understands, and takes only the string array. This is not a general enum decoder -
        ///     the editor has no enum feature to hang one off, and writing a half of one that other
        ///     code starts depending on is worse than writing this. The keys are dropped because
        ///     they are the music player's list positions, which name nothing on their own.
        /// </remarks>
        private static List<string> ReadStringValues(JagStream stream) {
            var values = new List<string>();

            while (true) {
                int opcode = stream.ReadUnsignedByte();
                if (opcode == 0)
                    return values;

                switch (opcode) {
                    case 1: //key type, a single character
                    case 2: //value type, a single character
                        stream.ReadUnsignedByte();
                        break;
                    case 3: //default string
                        stream.ReadJagexString();
                        break;
                    case 4: //default int
                        stream.ReadInt();
                        break;
                    case 5: { //int key to string value
                        int size = stream.ReadUnsignedShort();
                        for (int i = 0; i < size; i++) {
                            stream.ReadInt();
                            values.Add(stream.ReadJagexString());
                        }
                        break;
                    }
                    case 6: { //int key to int value, skipped
                        int size = stream.ReadUnsignedShort();
                        for (int i = 0; i < size; i++) {
                            stream.ReadInt();
                            stream.ReadInt();
                        }
                        break;
                    }
                    default:
                        //Anything else means the stream has desynchronised, and the entries read so
                        //far are still worth keeping.
                        return values;
                }
            }
        }
    }
}
