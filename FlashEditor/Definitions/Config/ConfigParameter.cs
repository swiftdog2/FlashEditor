using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     One entry of an opcode 249 parameter block: a key into the group 11 parameter type table
    ///     and either a string or an integer.
    /// </summary>
    /// <remarks>
    ///     The key is not a free integer. Measured over the whole cache: group 26's 1,730 records
    ///     carry 12,269 of these entries using 232 distinct keys, every one of them a live file id in
    ///     group 11, and the per-entry string flag agrees with that record's type letter (<c>'s'</c>
    ///     versus anything else) on all 12,269. So the flag is redundant with the keyed type and the
    ///     join is self-proving, but the flag is what the reader dispatches on - keep it as stored
    ///     rather than deriving it, or a record whose two disagree stops round-tripping.
    /// </remarks>
    public readonly struct ConfigParameter {
        /// <summary>Whether the value is a string rather than a 32-bit integer.</summary>
        public bool IsString { get; }

        /// <summary>The parameter type id, a file id in group 11.</summary>
        public int Key { get; }

        /// <summary>The value when <see cref="IsString"/>, otherwise null.</summary>
        public string? StringValue { get; }

        /// <summary>The value when not <see cref="IsString"/>.</summary>
        public int IntValue { get; }

        /// <summary>A string-valued parameter.</summary>
        /// <param name="key">The parameter type id.</param>
        /// <param name="value">The string value.</param>
        public ConfigParameter(int key, string value) {
            IsString = true;
            Key = key;
            StringValue = value;
            IntValue = 0;
        }

        /// <summary>An integer-valued parameter.</summary>
        /// <param name="key">The parameter type id.</param>
        /// <param name="value">The integer value.</param>
        public ConfigParameter(int key, int value) {
            IsString = false;
            Key = key;
            StringValue = null;
            IntValue = value;
        }
    }

    /// <summary>The opcode 249 parameter block, shared by every config family that carries one.</summary>
    /// <remarks>
    ///     Held as an ordered list rather than a dictionary. The client's own store keeps the
    ///     <b>first</b> occurrence of a duplicate key (InterfaceConfig.java:125), so folding the
    ///     block into a map both drops the losing entry and reorders what survives, which changes the
    ///     bytes of a record nobody edited.
    /// </remarks>
    public static class ConfigParameters {
        /// <summary>Reads a parameter block, leaving the stream after its last entry.</summary>
        /// <param name="stream">The definition file, positioned at the entry count.</param>
        /// <param name="into">The list to fill, cleared first.</param>
        public static void Read(JagStream stream, List<ConfigParameter> into) {
            into.Clear();

            int count = stream.ReadUnsignedByte();
            for (int i = 0; i < count; i++) {
                bool isString = stream.ReadUnsignedByte() == 1;
                int key = stream.ReadMedium();
                into.Add(isString
                    ? new ConfigParameter(key, stream.ReadJagexString())
                    : new ConfigParameter(key, stream.ReadInt()));
            }
        }

        /// <summary>Writes a parameter block.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="parameters">The entries, in the order they were stored.</param>
        public static void Write(JagStream stream, List<ConfigParameter> parameters) {
            stream.WriteByte(parameters.Count);

            foreach (ConfigParameter parameter in parameters) {
                stream.WriteByte(parameter.IsString ? 1 : 0);
                stream.WriteMedium(parameter.Key);
                if (parameter.IsString)
                    stream.WriteJagexString(parameter.StringValue ?? "");
                else
                    stream.WriteInteger(parameter.IntValue);
            }
        }
    }
}
