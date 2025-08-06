using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Drax360Service
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
        public int MakeInputNumber(int node, int loop, int inputn, int inputtype, bool on)
        {
            int no = inputn + (loop * 0x100) + (node * 0x10000) + (inputtype * 0x8000000);
            if (on)
            {
                no |= unchecked((int)0x80000000);
            }

            return no;
        }

        public void WriteData(NwmData eventtype, int eventnumber,
             string textparameter, string textparameter2, string textparameter3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = Convert.ToInt32(eventtype);
            ournvm.OurEvent = eventnumber;
            ournvm.On = on?1:0;

            ournvm.Text = textparameter;
            ournvm.Text2 = textparameter2;
            ournvm.Text3 = textparameter3;

            nvms.Add(ournvm);
        }

        /*
         
        DllExport __int16  WINAPI SendResetToAMX1(AlarmType, EventNumber, LongTime, iPar, Dtext, Dtext2, Dtext3, TxFile)
__int16 AlarmType;
long    EventNumber;		
long	LongTime;			// Time in c long time format
__int16	iPar;				// Integer parameter
unsigned char *Dtext;		// String parameter
unsigned char *Dtext2;		// String parameter
unsigned char *Dtext3;		// String parameter
unsigned char *TxFile;
{
	__int16 res=0;
	struct NWMSTRUCT NwmCmd;
	time_t tm;					// Used in time routines

	if(LongTime == 0L) {
		tm = time(&tm);
	}
	else{
		tm = (time_t)LongTime;
	}
	memset(&NwmCmd, 0, sizeof(struct NWMSTRUCT));								
	NwmCmd.type = 1;					// Event Type
	NwmCmd.event = EventNumber;			// Event number
	NwmCmd.time = (long)tm;
	NwmCmd.OnOff = 0;
	if((Dtext != 0) && (Dtext != NULL)) {
		strncpy(NwmCmd.Text, Dtext, 64);
	}
	if((Dtext2 != 0) && (Dtext2 != NULL)) {
		strncpy(NwmCmd.Text2, Dtext2, 40);		
	}
	if((Dtext3 != 0) && (Dtext3 != NULL)) {
		strncpy(NwmCmd.Text3, Dtext3, 40);	
	}
	res += SendEventToAMX1(TxFile, &NwmCmd);	// Now send it
	return res;
}

          
         */

        /*
         
        DllExport __int16  WINAPI SendAlarmToAMX1(AlarmType, EventNumber, LongTime, iPar, Dtext, Dtext2, Dtext3, TxFile)
__int16 AlarmType;
long    EventNumber;		
long	LongTime;			// Time in c long time format
__int16	iPar;				// Integer parameter
unsigned char *Dtext;		// String parameter
unsigned char *Dtext2;		// String parameter
unsigned char *Dtext3;		// String parameter
unsigned char *TxFile;
{
	__int16 res=0;
	struct NWMSTRUCT NwmCmd;
	time_t tm;				// Used in time routines

	if(LongTime == 0L) {
		tm = time(&tm);
	}
	else{
		tm = (time_t)LongTime;
	}
	memset(&NwmCmd, 0, sizeof(struct NWMSTRUCT));								
	NwmCmd.type = 1;							// Event Type
	NwmCmd.event = EventNumber;					// Event number
	NwmCmd.time = (long)tm;
	NwmCmd.OnOff = 1;
	NwmCmd.event |= 0x80000000;
	if((Dtext != 0) && (Dtext != NULL)) {
		strncpy(NwmCmd.Text, Dtext, 64);
	}
	if((Dtext2 != 0) && (Dtext2 != NULL)) {
		strncpy(NwmCmd.Text2, Dtext2, 40);		
	}
	if((Dtext3 != 0) && (Dtext3 != NULL)) {
		strncpy(NwmCmd.Text3, Dtext3, 40);	
	}

	res += SendEventToAMX1(TxFile, &NwmCmd);	// Now send it
	return res;
}

        */

        public void SendAlarmToAMX( int eventnumber, string dtext = "", string dtext2="", string dtext3 = "")
        {
            sendalarmorreset(eventnumber, dtext, dtext2, dtext3, true);
        }

        public void SendResetToAMX(int eventnumber, string dtext = "", string dtext2 = "", string dtext3 = "")
        {
            sendalarmorreset(eventnumber, dtext, dtext2, dtext3, false);
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
            
            if (System.IO.File.Exists(fullfilename)) {
                System.IO.File.Delete(fullfilename);  
            }

            using (FileStream fileStream = new FileStream(fullfilename, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] ourbytes = contents.ToArray();
                fileStream.Write(ourbytes, 0, ourbytes.Length);
                fileStream.Close();
                if (AMXTransfer.Instance.IsConnected)
                {
                    AMXTransfer.Instance.SendMessage($"NTX:" + fullfilename);
                }
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

        public void sendalarmorreset(int eventnumber, string dtext, string dtext2, string dtext3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = 1;
            ournvm.OurEvent = eventnumber;
            ournvm.On = on ? 65535 : 0;
            ournvm.Text = dtext;
            ournvm.Text2 = dtext2;
            ournvm.Text3 = dtext3;

            nvms.Add(ournvm);
        }
        #endregion
    }
}