using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Tests {
    class BitShifting {
        static void Main() {
            short x = 19;
            string bs = Convert.ToString(x, 2).PadLeft(8, '0');
            Console.WriteLine("x is " + x + ", " + Convert.ToString(x, 2).PadLeft(16, '0'));

            x <<= 8;
            Console.WriteLine("x is " + x + ", " + Convert.ToString(x, 2).PadLeft(16, '0'));

            x >>= 8;
            Console.WriteLine("x is " + x + ", " + Convert.ToString(x, 2).PadLeft(16, '0'));

        }
    }
}
