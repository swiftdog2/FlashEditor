using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FlashEditor.Definitions;

namespace FlashEditor.Export {
    /// <summary>
    ///     Writes a decoded record to JSON by walking whatever fields its decoder produced.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Reflective rather than a hand-written projection per record type, and deliberately. There
    ///     are more than thirty record types here and a hand-written projection for each would be
    ///     thirty places to forget a field: a field added to a decoder would silently stop being
    ///     exported, and nothing would fail. Walking the type means the export is exactly what the
    ///     decoder produced, which is the only thing it can honestly claim to be.
    ///     </para>
    ///     <para>
    ///     The cost of that is that the walk has to be bounded, because a record graph reaches
    ///     bitmaps, streams and controls if it is allowed to. <see cref="IsOpaque"/> is the stop
    ///     list, and depth, array length and byte length are all capped. A capped value is written as
    ///     an object saying what was elided rather than truncated silently.
    ///     </para>
    ///     <para>
    ///     <b>An opcode stream is written whole and never capped.</b> The recorded opcode order is
    ///     half of what makes this export worth having - the formats here are not canonical, and the
    ///     order and repetition a record was decoded from exist nowhere else once the fields are
    ///     read.
    ///     </para>
    /// </remarks>
    public sealed class RecordJsonWriter {
        /// <summary>How deep the walk follows nested records.</summary>
        /// <remarks>
        ///     Deep enough for a listing wrapping a definition wrapping a list of entries, which is
        ///     the deepest shape any decoder here produces, and shallow enough that an unexpected
        ///     back-reference costs a bounded amount of output rather than an unbounded one.
        /// </remarks>
        private const int MaxDepth = 6;

        /// <summary>How many elements of one sequence are written before it is summarised.</summary>
        private const int MaxSequenceItems = 8192;

        /// <summary>How many bytes of a blob are written inline as hex before it is summarised.</summary>
        private const int MaxInlineBytes = 256;

        /// <summary>Types the walk refuses to enter, whatever holds them.</summary>
        /// <remarks>
        ///     Everything here is either a resource with a lifetime (a stream, a bitmap, a control) or
        ///     a runtime artefact that says nothing about the cache. A record that holds one is still
        ///     exported; the member is omitted, and its name appears in the record's
        ///     <c>omittedMembers</c> so the gap is visible rather than silent.
        /// </remarks>
        private static readonly string[] OpaqueTypeNames = {
            "FlashEditor.IO.JagStream",
            "System.IO.Stream",
            "System.Drawing.Image",
            "System.Drawing.Bitmap",
            "System.Drawing.Color",
            "System.Windows.Forms.Control",
            "System.Delegate",
            "System.Type",
            "System.Reflection.MemberInfo"
        };

        private readonly Utf8JsonWriter writer;

        /// <summary>References already on the walk's own path, so a cycle cannot be followed.</summary>
        private readonly HashSet<object> visiting = new HashSet<object>(ReferenceEqualityComparer.Instance);

        /// <summary>Writes records into an open JSON writer.</summary>
        /// <param name="writer">The writer, positioned wherever a value may be written.</param>
        public RecordJsonWriter(Utf8JsonWriter writer) {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>
        ///     Writes one record as a JSON object: its decoded members, and its opcode stream where
        ///     it kept one.
        /// </summary>
        /// <param name="record">The decoded record.</param>
        public void WriteRecord(object record) {
            if (record == null) {
                writer.WriteNullValue();
                return;
            }

            WriteObjectBody(record, 0);
        }

        /// <summary>Writes any value at a given depth.</summary>
        /// <param name="value">The value.</param>
        /// <param name="depth">How many objects deep the walk already is.</param>
        private void WriteValue(object? value, int depth) {
            if (value == null) {
                writer.WriteNullValue();
                return;
            }

            switch (value) {
                case string text:
                    writer.WriteStringValue(text);
                    return;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    return;
                case char character:
                    writer.WriteStringValue(character.ToString());
                    return;
                case byte[] bytes:
                    WriteBytes(bytes);
                    return;
                case OpcodeStream opcodes:
                    WriteOpcodeStream(opcodes);
                    return;
                case Enum enumeration:
                    writer.WriteStringValue(enumeration.ToString());
                    return;
            }

            Type type = value.GetType();

            if (IsNumeric(type)) {
                WriteNumber(value);
                return;
            }

            if (value is IDictionary dictionary) {
                WriteDictionary(dictionary, depth);
                return;
            }

            if (value is IEnumerable sequence) {
                WriteSequence(sequence, depth);
                return;
            }

            if (depth >= MaxDepth || IsOpaque(type)) {
                writer.WriteStringValue(type.Name);
                return;
            }

            WriteObjectBody(value, depth);
        }

        /// <summary>Writes a record's members, guarding against a cycle through it.</summary>
        /// <param name="value">The record.</param>
        /// <param name="depth">How many objects deep the walk already is.</param>
        private void WriteObjectBody(object value, int depth) {
            //Value types cannot form a reference cycle and are cheap to re-enter, so only reference
            //types go on the path set. Adding structs would also box each one and never match.
            bool tracked = !value.GetType().IsValueType;

            if (tracked && !visiting.Add(value)) {
                writer.WriteStringValue("(cycle)");
                return;
            }

            try {
                writer.WriteStartObject();

                var omitted = new List<string>();

                foreach (MemberInfo member in MembersOf(value.GetType())) {
                    Type memberType = member is PropertyInfo property
                        ? property.PropertyType
                        : ((FieldInfo) member).FieldType;

                    if (IsOpaque(memberType)) {
                        omitted.Add(member.Name);
                        continue;
                    }

                    object? read;
                    try {
                        read = member is PropertyInfo readable
                            ? readable.GetValue(value)
                            : ((FieldInfo) member).GetValue(value);
                    } catch (Exception ex) when (ex is TargetInvocationException
                                                 || ex is InvalidOperationException
                                                 || ex is NotSupportedException) {
                        //A derived property can legitimately refuse to compute - CacheAddressing
                        //throws rather than guessing a split for an index whose shape is unrecorded,
                        //and several records expose a property that is only meaningful for some of
                        //their own states. That is a member with no value, not a broken export.
                        omitted.Add(member.Name);
                        continue;
                    }

                    writer.WritePropertyName(Camel(member.Name));
                    WriteValue(read, depth + 1);
                }

                if (omitted.Count > 0) {
                    writer.WriteStartArray("omittedMembers");
                    foreach (string name in omitted)
                        writer.WriteStringValue(name);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            } finally {
                if (tracked)
                    visiting.Remove(value);
            }
        }

        /// <summary>
        ///     Writes an opcode stream: every occurrence, in order, with the bytes it consumed.
        /// </summary>
        /// <remarks>
        ///     Uncapped, and the payloads are written in full however long they are. This is the
        ///     record of which of several valid encodings the packer chose, and a truncated one is
        ///     worse than none - it reads as complete.
        /// </remarks>
        /// <param name="opcodes">The recorded stream.</param>
        private void WriteOpcodeStream(OpcodeStream opcodes) {
            writer.WriteStartArray();

            foreach (OpcodeRecord record in opcodes) {
                writer.WriteStartObject();
                writer.WriteNumber("opcode", record.Opcode);
                writer.WriteString("payload", Hex(record.Payload, record.Payload.Length));
                if (opcodes.IsSuppressed(record.Opcode))
                    writer.WriteBoolean("suppressed", true);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        /// <summary>Writes a blob, inline as hex while it is short enough to be readable.</summary>
        /// <param name="bytes">The blob.</param>
        private void WriteBytes(byte[] bytes) {
            if (bytes.Length <= MaxInlineBytes) {
                writer.WriteStringValue(Hex(bytes, bytes.Length));
                return;
            }

            writer.WriteStartObject();
            writer.WriteNumber("length", bytes.Length);
            writer.WriteString("head", Hex(bytes, MaxInlineBytes));
            writer.WriteString("elided", "a blob longer than " + MaxInlineBytes +
                " bytes is summarised rather than written inline");
            writer.WriteEndObject();
        }

        /// <summary>Writes a keyed collection as a JSON object.</summary>
        /// <param name="dictionary">The collection.</param>
        /// <param name="depth">How many objects deep the walk already is.</param>
        private void WriteDictionary(IDictionary dictionary, int depth) {
            writer.WriteStartObject();

            int written = 0;
            foreach (DictionaryEntry entry in dictionary) {
                if (++written > MaxSequenceItems) {
                    writer.WriteNumber("elidedEntries", dictionary.Count - MaxSequenceItems);
                    break;
                }

                writer.WritePropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "null");
                WriteValue(entry.Value, depth + 1);
            }

            writer.WriteEndObject();
        }

        /// <summary>Writes a sequence as a JSON array, capped.</summary>
        /// <param name="sequence">The sequence.</param>
        /// <param name="depth">How many objects deep the walk already is.</param>
        private void WriteSequence(IEnumerable sequence, int depth) {
            writer.WriteStartArray();

            int written = 0;
            foreach (object? item in sequence) {
                if (++written > MaxSequenceItems) {
                    writer.WriteStringValue("(elided beyond " + MaxSequenceItems + " items)");
                    break;
                }

                WriteValue(item, depth + 1);
            }

            writer.WriteEndArray();
        }

        /// <summary>Writes a numeric value at its own width.</summary>
        /// <param name="value">The number.</param>
        private void WriteNumber(object value) {
            switch (value) {
                case byte number: writer.WriteNumberValue(number); return;
                case sbyte number: writer.WriteNumberValue(number); return;
                case short number: writer.WriteNumberValue(number); return;
                case ushort number: writer.WriteNumberValue(number); return;
                case int number: writer.WriteNumberValue(number); return;
                case uint number: writer.WriteNumberValue(number); return;
                case long number: writer.WriteNumberValue(number); return;
                case ulong number: writer.WriteNumberValue(number); return;
                case float number: writer.WriteNumberValue(number); return;
                case double number: writer.WriteNumberValue(number); return;
                case decimal number: writer.WriteNumberValue(number); return;
                default:
                    writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        /// <summary>Whether a type is written as a number.</summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is one of the numeric primitives.</returns>
        private static bool IsNumeric(Type type) {
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
                || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong) || type == typeof(float)
                || type == typeof(double) || type == typeof(decimal);
        }

        /// <summary>
        ///     Whether the walk refuses to enter a type.
        /// </summary>
        /// <remarks>
        ///     Matched by name up the base chain rather than by <c>typeof</c>, so this file needs no
        ///     reference to System.Drawing or System.Windows.Forms to name their types, and so a
        ///     subclass of a denied type is denied with it.
        /// </remarks>
        /// <param name="type">The type to test.</param>
        /// <returns>Whether it is opaque.</returns>
        private static bool IsOpaque(Type type) {
            for (Type? current = type; current != null; current = current.BaseType) {
                string? name = current.FullName;
                if (name == null)
                    continue;

                foreach (string opaque in OpaqueTypeNames)
                    if (string.Equals(name, opaque, StringComparison.Ordinal))
                        return true;
            }

            return false;
        }

        /// <summary>
        ///     The public instance members of a type worth writing, in declaration order.
        /// </summary>
        /// <remarks>
        ///     Fields as well as properties, because the older decoders here expose their decoded
        ///     values as public fields and a properties-only walk would export those records as
        ///     empty objects. Indexers are skipped: an indexer has no single value, and calling one
        ///     with a fabricated argument would invent data.
        /// </remarks>
        /// <param name="type">The record type.</param>
        /// <returns>The members to write.</returns>
        private static IEnumerable<MemberInfo> MembersOf(Type type) {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                yield return field;

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (!property.CanRead)
                    continue;
                if (property.GetIndexParameters().Length > 0)
                    continue;

                yield return property;
            }
        }

        /// <summary>A member name as JSON usually spells it.</summary>
        /// <param name="name">The member name.</param>
        /// <returns>The name with its first letter lowered.</returns>
        private static string Camel(string name) {
            if (name.Length == 0 || char.IsLower(name[0]))
                return name;

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>Lowercase hex, without a separator.</summary>
        /// <param name="bytes">The blob.</param>
        /// <param name="length">How many bytes to render.</param>
        /// <returns>The hex string.</returns>
        private static string Hex(byte[] bytes, int length) {
            return Convert.ToHexString(bytes, 0, length).ToLowerInvariant();
        }
    }
}
