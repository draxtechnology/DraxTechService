using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DraxTechnology
{
    // Move the extension method to a non-generic static class
    internal static class ElementsExtensions
    {
        public static bool In<T>(this T item, params T[] items)
        {
            if (items == null)
                throw new ArgumentNullException("items");
            return items.Contains(item);
        }
    }

    internal class Elements
    {
        public static bool isService
        {
            get
            {
                return Console.IsErrorRedirected;
            }
        }

        /// <summary>
        /// Chunk the data in a chunksize elements
        /// </summary>
        /// <param name="data"></param>
        /// <param name="chunksize"></param>
        /// <returns></returns>
        public static List<byte[]> Chunker(byte[] data, int chunksize)
        {
            List<byte[]> chunks = new List<byte[]>();

            for (int intcount = 0; intcount < data.Length / chunksize; intcount++)
            {
                byte[] ret = data.Skip(intcount * chunksize).Take(chunksize).ToArray();

                chunks.Add(ret);
            }
            return chunks;

        }

        public static List<byte[]> Chunker(byte[] data, byte start, byte end, out int removelength)
        {
            List<byte[]> chunks = new List<byte[]>();
            int startpos = -1;
            int endpos = -1;
            removelength = 0;
 
            for (int intcount = 0; intcount < data.Length; intcount++)
            {
                byte workingbyte = data[intcount];
                if (workingbyte == start)
                {
                    startpos = intcount;
                    continue;
                }

                if (workingbyte == end)
                {
                    endpos = intcount;

                    if (startpos > -1 && endpos > startpos)
                    {
                        byte[] ret = data.Skip(startpos + 1).Take((endpos - startpos) - 1).ToArray();
                        chunks.Add(ret);
                        removelength += ret.Length + 2; // allow for start end char
                    }
                    startpos = -1;
                    endpos = -1;

                }

            }
            return chunks;
        }
    }
}