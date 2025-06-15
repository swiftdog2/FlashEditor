using System;
using System.Collections.Generic;

namespace FlashEditor.Collections {
    public static class ArrayUtil {
        /// <summary>
        /// Instantiates a 2-dimensional array for a given Type
        /// </summary>
        /// <typeparam name="T">The generic type to instantiate</typeparam>
        /// <param name="firstDimension">The length of the first dimension</param>
        /// <param name="secondDimension">The length of the second dimension</param>
        /// <returns></returns>
        public static T[][] ReturnRectangularArray<T>(int firstDimension, int secondDimension)
            where T : struct {
            T[][] result = new T[firstDimension][];
            for(int i = 0; i < firstDimension; i++)
                result[i] = new T[secondDimension];
            return result;
        }
    }
}
