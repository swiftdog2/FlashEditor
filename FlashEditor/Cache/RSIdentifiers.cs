using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.CheckSum {
    /// <summary>
    /// Open-addressing hash table that maps name identifiers to file indices
    /// within a reference table.
    /// </summary>
    public class RSIdentifiers {
        int[] table;

        public int getFile(int identifier) {
            //Get mask to wrap around, and initial slot
            int mask = (table.Length >> 1) - 1;
            int i = identifier & mask;

            while(true) {
                /* Get id at current slot */
                int id = table[i + i + 1];
                if(id == -1) {
                    return -1;
                }

                /* Return current id, if identifier matches */
                if(table[i + i] == identifier) {
                    return id;
                }

                /* Move to next slot */
                i = i + 1 & mask;
            }
        }

        public RSIdentifiers(int[] identifiers) {

            //Initial identifier sizes
            int length = identifiers.Length;
            int halfLength = identifiers.Length >> 1;

            //Find maximum power of 2 below array and a half length 
            int size = 1;
            int mask = 1;
            for(int i = 1; i <= length + (halfLength); i <<= 1) {
                mask = i;
                size = i << 1;
            }

            //Increase power over the array length
            mask <<= 1;
            size <<= 1;

            //Fill table with null values
            table = Enumerable.Repeat(-1, size).ToArray();

            //Populate table with identifiers followed by their id
            for(int id = 0; id < identifiers.Length; id++) {
                int i;
                for(i = identifiers[id] & mask - 1; table[i + i + 1] != -1; i = i + 1 & mask - 1)
                    ;

                table[i + i] = identifiers[id];
                table[i + i + 1] = id;
            }
        }
    }
}
