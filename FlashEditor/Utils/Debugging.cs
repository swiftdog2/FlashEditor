using FlashEditor.cache;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlashEditor.Utils {
    /// <summary>
    /// Console-based debug logging utility with configurable verbosity levels.
    /// </summary>
    public static class DebugUtil {
        /// <summary>Controls how much detail is emitted to the console.</summary>
        public enum LOG_DETAIL {
            NONE = 0,
            BASIC = 1,
            ADVANCED = 2,
            INSANE = 3
        };

        /// <summary>The active verbosity threshold. Messages above this level are suppressed.</summary>
        public static LOG_DETAIL LOG_LEVEL = LOG_DETAIL.BASIC;

        /// <summary>
        /// Prints out the debug message and waits for user input
        /// </summary>
        /// <param name="output">The debug message</param>
        public static void Debug(string output) {
            if(LOG_LEVEL == LOG_DETAIL.NONE)
                return;

            Console.WriteLine(output);
        }

        /// <summary>Writes a debug message if <paramref name="level"/> does not exceed <see cref="LOG_LEVEL"/>.</summary>
        /// <param name="output">The message to print.</param>
        /// <param name="level">The verbosity level of this message.</param>
        public static void Debug(string output, LOG_DETAIL level) {
            if(level == LOG_DETAIL.NONE)
                return;

            if(level <= LOG_LEVEL)
                Debug(output);
        }

        /// <summary>Writes a debug message without a trailing newline.</summary>
        /// <param name="output">The message to print.</param>
        /// <param name="level">The verbosity level of this message.</param>
        public static void Debug2(string output, LOG_DETAIL level) {
            if(level == LOG_DETAIL.NONE)
                return;

            if(level <= LOG_LEVEL)
                Console.Write(output);
        }

        /// <summary>
        /// Prints out the entire byte array separated by spaces
        /// </summary>
        /// <param name="buffer">The byte buffer to print</param>
        public static void PrintByteArray(byte[] buffer) {
            PrintByteArray(buffer, buffer.Length);
        }

        /// <summary>
        /// Prints the first and last length bytes, with no overlap
        /// </summary>
        /// <param name="buffer">The byte buffer to print</param>
        /// <param name="length">The number of bytes to print from the beginning and end of the buffer</param>
        public static void PrintByteArray(byte[] buffer, int length) {
            if(LOG_LEVEL < LOG_DETAIL.INSANE)
                return;

            //We cannot print more than max bytes on either side
            int max = 20; //buffer.Length;

            Console.Write(length + "/" + buffer.Length + ": ");

            //Obviously we can't read more bytes than there are in the buffer...
            length = Math.Min(length, max);

            //Print out the left side (from 0 to length)
            for(int k = 0; k < length; k++)
                Console.Write("{0} ", buffer[k] & 0xFF);

            Console.Write("...");

            //Print out the right side (from length + 1 to end)
            for(int k = buffer.Length - length; k < buffer.Length; k++)
                Console.Write("{0} ", buffer[k] & 0xFF);

            Console.WriteLine();
        }

        /// <summary>Writes a line to the console if logging is enabled.</summary>
        /// <param name="output">The message to print.</param>
        public static void WriteLine(string output) {
            if(LOG_LEVEL == LOG_DETAIL.NONE)
                return;

            Console.WriteLine(output);
        }

        /// <summary>Returns the 8-bit binary representation of a byte.</summary>
        public static string ToBitString(byte b) {
            return Convert.ToString(b, 2).PadLeft(8, '0');
        }

        /// <summary>Returns the 16-bit binary representation of a short.</summary>
        public static string ToBitString(short s) {
            return Convert.ToString(s, 2).PadLeft(16, '0');
        }

        /// <summary>Returns the 32-bit binary representation of an int (also used for medium values).</summary>
        public static string ToBitString(int i) {
            return Convert.ToString(i, 2).PadLeft(32, '0');
        }

        /// <summary>
        /// Serializes two objects to JSON and logs any properties whose values differ.
        /// </summary>
        /// <param name="a">First object to compare.</param>
        /// <param name="b">Second object to compare.</param>
        public static void PrintDifferences(object a, object b) {
            if(a == null || b == null) {
                if(a != b)
                    Debug("\tObjects differ (null vs non-null)");
                return;
            }

            Dictionary<string, object> propsA = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(a));
            Dictionary<string, object> propsB = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(b));

            if(propsA == null || propsB == null) {
                Debug("Unable to evaluate differences - serialization failed");
                return;
            }

            Debug("Evaluating changes...");

            foreach(KeyValuePair<string, object> kvp in propsA) {
                //Only look at properties with common names
                if(!propsB.ContainsKey(kvp.Key))
                    continue;

                string propName = kvp.Key;
                object pA = kvp.Value;
                object pB = propsB[propName];

                bool equal = true;

                //If type is null, it is not primitive, so take a peek
                if(pA == null || pB == null) {
                    //Maybe one is null, and the other is not
                    if((pA == null && pB != null) || (pA != null) && (pB == null))
                        equal = false;
                } else {
                    if(pA.GetType().IsPrimitive || pA is string) {
                        //Simple comparison of primitive types
                        equal = pA.Equals(pB);
                    } else if(pA is JArray) {
                        //Primitive arrays reserialize as JArray
                        equal = JToken.DeepEquals((JArray) pA, (JArray) pB);
                    } else {
                        //Unknown type, further investigation required
                        Debug(propName + " type is " + pA.GetType().Name);
                    }
                }

                if(!equal)
                    Debug("\t" + propName + ": " + propsA[propName] + " != " + propsB[propName]);
            }
        }
    }
}
