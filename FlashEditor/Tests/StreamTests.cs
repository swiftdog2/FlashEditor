using FlashEditor.utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Tests {
    class StreamTests {
        static void Main() {
            Idk();
        }

        static void Idk() {
            JagStream stream = new JagStream();
            for(int k = 0; k < 50; k++) {
                stream.WriteByte((byte) k);
                stream.Position++;
            }

            DebugUtil.PrintByteArray(stream.ToArray());
        }

        public static bool StreamDifference(JagStream stream1, JagStream stream2, string stream) {
            if(stream1 == null || stream2 == null)
                throw new NullReferenceException("Error, stream(s) are null");

            bool diff = false;

            //Rewind the streams before comparing dumbass
            stream1.Seek0();
            stream2.Seek0();

            //Rudimentary check, fast if the streams are different lengths
            if(stream1.Length != stream2.Length) {
                long delta = stream2.Length - stream1.Length;
                DebugUtil.Debug("Difference x in " + stream + " data, len: " + delta + " bytes");
                diff = true;
            }

            //Same length? Check the bytes I guess.
            for(int k = 0; k < Math.Min(stream1.Length, stream2.Length); k++) {
                int x = stream1.ReadByte();
                int y = stream2.ReadByte();
                if(x != y) {
                    DebugUtil.Debug("Difference y in " + stream + " @ " + k + " -- x: " + x + ", y: " + y);
                    diff = true;
                    break;
                }
            }

            if(!diff)
                DebugUtil.Debug(stream + " streams are equal!");

            return diff;
        }
    }
}
