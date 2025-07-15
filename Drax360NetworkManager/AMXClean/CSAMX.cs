using System;
using System.Collections.Generic;
using System.IO;


namespace Drax360Service.AMXClean
{
    public class CSAMX
    {
        #region constants
        private const int kstartingfilenumber = 9000;
        private const string kgenextension = ".GEN";
        private const int kmaxfilenumber = 1000000;
        #endregion

        #region private variables
        private int filenumber = kstartingfilenumber;

        private string workingfolder = "";
        private List<NVM> nvms = new List<NVM>();
        #endregion


        #region constructor
        public CSAMX()
        {
            workingfolder = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),"Temp");
            if (!Directory.Exists(workingfolder))
            {
                Directory.CreateDirectory(workingfolder);
            }
        }
        #endregion

        #region public methods

        public int IncrementInputNumber(int inputNumber)
        {
            return (int) (inputNumber + 0x80000000);
        }
        public int MakeInputNumber(int node, int zone, int inputn, int inputtype)
        { 
            
            return inputn + (zone * 0x100) + (node * 0x10000) + (inputtype * 0x8000000);
        }

        public void WriteData(int eventtype, int eventnumber,
             string textparameter, string textparameter2, string textparameter3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = eventtype;
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


            string contents = "";
            foreach (NVM ournvm in nvms)
            {
                contents += ournvm.Render();
            }

            // safety check
            if (String.IsNullOrEmpty(contents))
            {
                nvms.Clear();
                return;
            }

            filenumber++;
            string filename = filenumber.ToString() + kgenextension;
            string fullfilename = Path.Combine(workingfolder, filename);

            // Open the file in append mode
            using (FileStream fileStream = new FileStream(fullfilename, FileMode.Append, FileAccess.Write))
            {
                using (StreamWriter writer = new StreamWriter(fileStream))
                {
                    writer.Write(contents);
                }
            }

            if (filenumber > kmaxfilenumber) filenumber = kstartingfilenumber;
            nvms.Clear();
        }
        #endregion
    }
}