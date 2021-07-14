using System;

namespace FlashEditor.utils {
    class DebugUtil {
        //Change the order of the indexes when you change the layout of the editor tabs
        public enum LOG_DETAIL {
            NONE = 0,
            BASIC = 1,
            ADVANCED = 2,
            INSANE = 3
        };

        //The current logging detail level, change for lower/higher detailed logs
        public static LOG_DETAIL LOG_LEVEL = LOG_DETAIL.INSANE;

        /// <summary>
        /// Prints out the debug message and waits for user input
        /// </summary>
        /// <param name="output">The debug message</param>
        public static void Debug(string output) {
            Console.WriteLine(output);
            Console.ReadLine();
        }

        public static void Debug(string output, LOG_DETAIL level) {
            //Is logging disabled?
            if(level == LOG_DETAIL.NONE)
                return;

            //Otherwise, log if the level is below or equal to current level
            if(level <= LOG_LEVEL)
                Debug(output);
        }


        public static void Debug2(string output) {
            Console.Write(output);
        }

        /// <summary>
        /// Prints out the entire byte array separated by spaces
        /// </summary>
        /// <param name="buffer">The byte buffer to print</param>
        internal static void PrintByteArray(byte[] buffer) {
            PrintByteArray(buffer, buffer.Length);
        }

        /// <summary>
        /// Prints the first and last length bytes, with no overlap
        /// </summary>
        /// <param name="buffer">The byte buffer to print</param>
        /// <param name="length">The number of bytes to print from the beginning and end of the buffer</param>
        public static void PrintByteArray(byte[] buffer, int length) {
            //We cannot print more than max bytes on either side
            int max = buffer.Length / 2;

            //Obviously we can't read more bytes than there are in the buffer...
            length = Math.Min(length, max);

            //Print out the left side (from 0 to length)
            for(int k = 0; k < length; k++) {
                Console.Write("{0} ", (int) (buffer[k] & 0xFF));
            }

            System.Console.Write("...");

            //Print out the right side (from length + 1 to end)
            for(int k = buffer.Length - length; k < buffer.Length; k++)
                Console.Write("{0} ", (int) (buffer[k] & 0xFF));

            Console.WriteLine();
        }

        internal static void WriteLine(string output) {
            Console.WriteLine("'");
        }

        internal static void DebugWTF() {
            Debug("wat the fuck");
            throw new Exception();
        }
    }
}
