using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Tests {
    class StreamTests {
        static void Main() {
            using(JagStream s = new JagStream()) {
                Random r = new Random();

                for(int k = 0; k < 50000; k++)
                    s.WriteInteger(r.Next(1000));

                JagStream.Save(s, "C:/Users/CJ/Desktop/gzip/shit.txt");

                byte[] compressedData = CompressionUtils.Gzip(s.ToArray());
                JagStream.Save(new JagStream(compressedData), "C:/Users/CJ/Desktop/gzip/compressed.txt");
            }


            /*
            JagStream stream = JagStream.Load("C:/Users/CJ/Desktop/shit.txt");
            while(stream.Remaining() > 0)
                System.Console.WriteLine(stream.ReadInt());
            */

            System.Console.ReadLine();
        }
    }
}
