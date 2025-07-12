using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
    internal class AMX
    {
        int transferfilenumber = 0;
        string workingfilename = "";
        List<string> quedmessages = new List<string>();
        private void buildfilename()
        {
            workingfilename = transferfilenumber + ".GEN";
        }
        public void QueueTransferData(string msg)
        {
            quedmessages.Add(msg);
            // $"NTX:{GENNetManager.TransferFile}"
        }

        public void Flush()
        {
            buildfilename();
            if (File.Exists(workingfilename))
            {
                File.Delete(workingfilename);
            }
            string contents = "";
            if (quedmessages.Count > 0)
            {
                foreach (string line in quedmessages)
                {
                    contents += line + Environment.NewLine;
                }

                File.WriteAllText(workingfilename, contents);
                quedmessages.Clear();
            }
            
            transferfilenumber++;
            if (transferfilenumber > 1000000)
            {
                transferfilenumber = 0;
            }
        }
    }
}