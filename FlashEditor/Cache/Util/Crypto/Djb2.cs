using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Util.Crypto {
    //Straight rip no idea if it works or not lol

    class Djb2 {
        /**
		 * An implementation of Dan Bernstein's {@code djb2} hash function which is
		 * slightly modified. Instead of the initial hash being 5381, it is zero.
		 * 
		 * @param str
		 *            The string to hash.
		 * @return The hash code.
		 */
        public static int Hash(string str) {
            int hash = 0;
            char[] chars = str.ToCharArray();

            for(int i = 0; i < str.Length; i++)
                hash = chars[i] + ((hash << 5) - hash);

            return hash;
        }
    }
}
