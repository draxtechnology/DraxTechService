using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;


namespace Drax360Service.AMXClean
{
    public class CSAMX
    {
        #region constants
        
        private const string kgenextension = "GEN";
        private const int kmaxfilenumber = 1000000;
        #endregion

        #region private variables
        private int filenumber = 0;

        private string workingfolder = "";
        private List<NVM> nvms = new List<NVM>();
        #endregion


        #region constructor
        public CSAMX()
        {
            workingfolder = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Temp");
            if (!Directory.Exists(workingfolder))
            {
                Directory.CreateDirectory(workingfolder);
            }

            // determine starting filenumber
            determinelastfilenumber();
        }
        
        #endregion

        #region public methods

        public int IncrementInputNumber(int inputNumber)
        {
            return (int) (inputNumber + 0x80000000);
        }
        public int MakeInputNumber(int node, int loop, int inputn, int inputtype)
        { 
            
            return inputn + (loop * 0x100) + (node * 0x10000) + (inputtype * 0x8000000);
        }

        public void WriteData(NwmData eventtype, int eventnumber,
             string textparameter, string textparameter2, string textparameter3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = Convert.ToInt32(eventtype);
            ournvm.OurEvent = eventnumber;
            ournvm.OnOff = on ? -1 : 0;

            ournvm.Text = textparameter;
            ournvm.Text2 = textparameter2;
            ournvm.Text3 = textparameter3;

            nvms.Add(ournvm);
        }
        public void LogMessage(int eventtype, int eventnumber, string text, int ophandle)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = eventtype;
            ournvm.OurEvent = eventnumber;
            ournvm.Text = text;
            ournvm.Spare[0] = ophandle;
            nvms.Add(ournvm);
        }


        public void FlushMessages()
        {
            if (nvms.Count == 0) return;


            List<byte> contents = new List<byte>();
            foreach (NVM ournvm in nvms)
            {
                contents.AddRange(ournvm.RenderBytes());
            }

            // safety check
            if (contents.Count == 0)
            {
                nvms.Clear();
                return;
            }

            filenumber++;
            if (filenumber > kmaxfilenumber) filenumber = 1;

            string filename = filenumber.ToString() +"."+ kgenextension;
            string fullfilename = Path.Combine(workingfolder, filename);

            // Open the file in write mode, changed from append as we need to create a new file on flush
            
            if (File.Exists(fullfilename)) {
                File.Delete(fullfilename);  
            }

            using (FileStream fileStream = new FileStream(fullfilename, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] ourbytes = contents.ToArray();
                fileStream.Write(ourbytes, 0, ourbytes.Length);
                fileStream.Close();
            }

            
            nvms.Clear();
        }
        #endregion
        #region private methods
        /// <summary>
        /// gets the last file in the amx folder and increments file number by 1;
        /// </summary>
        private void determinelastfilenumber()
        {
            var dirInfo = new DirectoryInfo(workingfolder);
            var allFiles = dirInfo.GetFiles("*." + kgenextension, SearchOption.TopDirectoryOnly);
            FileInfo lastmodifiedfile = allFiles.OrderBy(fi => fi.LastWriteTime).LastOrDefault();
            if (lastmodifiedfile == null) return;

            string[] splits = lastmodifiedfile.Name.Split('.');

            if (splits.Length != 2) return;

            filenumber = Convert.ToInt32(splits[0]);
        }
        #endregion
    }
}