using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
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


        public static List<byte[]> Chunker(byte[] data, byte end, out int removelength)
        {
            List<byte[]> chunks = new List<byte[]>();
            int startpos = 1;
            removelength = 0;

            while (true)
            {
                byte workinglength = data[startpos];
                if (workinglength == end)
                {
                    // include the end byte in the remove length
                    removelength += 1;
                    break;
                }
                byte[] workingbytes = data.Skip(startpos + 1).Take(workinglength).ToArray();
                chunks.Add(workingbytes);
                removelength+= workinglength+1;
                // mover to end pos
                startpos += workinglength + 1;

            }


            return chunks;

        }
    }
}
   