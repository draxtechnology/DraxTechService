using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
    internal class Elements
    {

        /// <summary>
        /// Chunk the data in a chunksize elements
        /// </summary>
        /// <param name="data"></param>
        /// <param name="chunksize"></param>
        /// <returns></returns>
        public static List<byte[]> Chunker(byte[] data, int chunksize)
        {
            var chunks = new List<byte[]>();
            if (data.Length < chunksize) return chunks;
            List<byte[]> rets = data.Select((value, index) => new { PairNum = Math.Floor(index / (double)chunksize), value }).GroupBy(pair => pair.PairNum).Select(grp => grp.Select(g => g.value).ToArray()).ToList();
            // make sure each item is chunksize
            foreach (byte[] r in rets)
            {
                if (r.Length == chunksize)
                {
                    chunks.Add(r);
                }
            }
            return chunks;

        }
    }
}
   