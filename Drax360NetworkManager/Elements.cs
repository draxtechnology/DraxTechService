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
    }
}
   