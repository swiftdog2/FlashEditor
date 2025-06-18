using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Represents an object placed within a region, including its
    ///     identifier, orientation and absolute <see cref="Position"/>.
    /// </summary>
    public class Location {

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
        /// <summary>
        ///     Gets the location type value stored in the map.
        /// </summary>
        public int GetLocationType() {
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

