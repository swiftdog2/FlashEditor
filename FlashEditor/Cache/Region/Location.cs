using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Region {
    //Straight rip no idea if it works or not lol

    class Location {

        private int id;
        private int type;
        private int orientation;
        private Position position;
	
	public Location(int id, int type, int ori, Position pos) {
            this.id = id;
            this.type = type;
            this.orientation = ori;
            this.position = pos;
        }

        /**
		 * @return the id
		 */
        public int GetId() {
            return id;
        }

        /**
		 * @return the type
		 */
        public int GetType() {
            return type;
        }

        /**
		 * @return the orientation
		 */
        public int GetOrientation() {
            return orientation;
        }

        /**
		 * @return the position
		 */
        public Position GetPosition() {
            return position;
        }

    }
}
