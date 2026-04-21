using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;


namespace DraxTechnology
{
    public class CSAMX
    {
        #region constants

        public event EventHandler OutsideEvents;

        //private const string kgenextension = "GEN";
        private const int kmaxfilenumber = 1000000;
        private const string csamxfolder = "Temp";
        #endregion

        #region private variables
        private int filenumber = 0;

        private string logfiles = "";
        private string extension = "";
        private List<NVM> nvms = new List<NVM>();
        private readonly object _nvmsLock = new object();
        #endregion

        #region constructor
        public CSAMX()
        {
            

            // determine starting filenumber
           
        }
        
        #endregion

        #region public methods
        public void Startup(string logfiles,string extension)
        {
            this.extension = extension;
            this.logfiles = Path.Combine(logfiles, csamxfolder);
            AMXTransfer.Instance.OutsideEvents += Instance_OutsideEvents;
            // check if the temp directory exists   

            if (!Directory.Exists(this.logfiles))
            {
                Directory.CreateDirectory(this.logfiles);
            }
            determinelastfilenumber();
        }

        private void Instance_OutsideEvents(object sender, EventArgs e)
        {
            OutsideEvents?.Invoke(this, e);
        }

        // MIKE

        /* *********************************************************** 
This function reads one data structure from an NWM transfer file

Data is passed back in the array Ddat:
	0-7	= Spare
	8	= Type
	9	= Event number
	10	= Time
	11	= length of text
	12  = onoff

If Dtext = NULL then no text is passed back

Function created: 11/11/97   Revised: 

Returns: -1 on success, else 0
*/
/*
 * DllExport __int16  WINAPI GetNWMData(fname, index, Ddat, LocText, ExText, ExText2)
char* fname;
        __int16 index;
        long* Ddat;
        char* LocText;
        char* ExText;
        char* ExText2;
{
            __int16 r = -1, i;

    long p;
        struct NWMSTRUCT Nwm;
	FILE* ftmp;

	for(i=0;i<20;i++)
		Ddat[i] = 0L;		// Clear the array of longs
	p = (long) (index-1) * (long) (long)sizeof(struct NWMSTRUCT);
	ftmp = fopen(fname,"rb");		// NB - explicit filename
	if(ftmp == NULL) {
		return 0;
	}
	if(fseek(ftmp, p, SEEK_SET) != 0) {
		r = 0;
	}
	else {
		if(fread(&Nwm, (long)sizeof(struct NWMSTRUCT), 1, ftmp) < 1) {
			r = 0;
		}

        else
{
    for (i = 0; i < 8; i++)
    {
        Ddat[i] = Nwm.spare[i];     // Copy the additional data 
    }
    Ddat[8] = Nwm.type;
    Ddat[9] = Nwm.event;
    Ddat[10] = Nwm.time;
    Ddat[12] = (long)Nwm.OnOff;
    Ddat[13] = Nwm.Dat1;
    Ddat[14] = Nwm.Dat2;
    Ddat[15] = Nwm.Dat3;
    Ddat[16] = Nwm.Dat4;
    Ddat[17] = Nwm.Dat5;
    Ddat[18] = Nwm.Dat6;
    Ddat[19] = (long)Nwm.Value;
    Ddat[20] = (long)Nwm.ControlType;
    Ddat[21] = (long)Nwm.Node;
    // Copy the texts
    Ddat[11] = (long)copystring(LocText, Nwm.Text);     // Location/description
    Ddat[22] = (long)copystring(ExText, Nwm.Text2);     // Extended - e.g device type - used for event type phrase when sending to output NWMs
    Ddat[23] = (long)copystring(ExText2, Nwm.Text3);    // Extended - e.g zone text 
}
	}
	fclose(ftmp);
return r;
}
*/


        public int IncrementInputNumber(int inputNumber)
        {
            return (int) (inputNumber + 0x80000000);
        }
        public int MakeInputNumber(int node, int loop, int inputn, int inputtype, bool on = true)
        {

            int no = inputn + (loop * 0x100) + (node * 0x10000) + (inputtype * 0x8000000);
            if (on)
            {
                no |= unchecked((int)0x80000000);
            }

            return no;
        }

        public void WriteData(NwmData eventtype, int eventnumber,
             string textparameter, string textparameter2, string textparameter3, bool on=true)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = Convert.ToInt32(eventtype);
            ournvm.OurEvent = eventnumber;
            ournvm.On = on?1:0;

            ournvm.Text = textparameter;
            ournvm.Text2 = textparameter2;
            ournvm.Text3 = textparameter3;

            lock (_nvmsLock)
            {
                nvms.Add(ournvm);
            }
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

        public void SendAlarmToAMX(int eventnumber, string dtext = "", string dtext2 = "", string dtext3 = "")
        {
            sendalarmorreset(eventnumber, dtext, dtext2, dtext3, true);
        }
        public void SendAlarmToAMX_disable(int eventnumber, string dtext, string dtext2, string dtext3, bool on)
        {
            sendalarmorreset_disable(eventnumber, dtext, dtext2, dtext3, on);
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
            lock (_nvmsLock)
            {
                nvms.Add(ournvm);
            }
        }


        public void FlushMessages()
        {
            List<NVM> toProcess;
            lock (_nvmsLock)
            {
                if (nvms.Count == 0) return;
                toProcess = new List<NVM>(nvms);
                nvms.Clear();
            }

            List<byte> contents = new List<byte>();
            foreach (NVM ournvm in toProcess)
            {
                contents.AddRange(ournvm.RenderBytes());
            }

            // safety check
            if (contents.Count == 0)
            {
                return;
            }

            filenumber++;
            if (filenumber > kmaxfilenumber) filenumber = 1;

            string filename = filenumber.ToString() +"."+ extension;
            string fullfilename = Path.Combine(logfiles, filename);

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
            //File.Delete(fullfilename);  // If I delete the file straight away then nothing appears on AMX
            var files = Directory.GetFiles(this.logfiles, "*." + extension);
            foreach (var file in files)
            {
                try
                {
                    if (!file.Equals(fullfilename, StringComparison.OrdinalIgnoreCase))
                    {
                        //File.Delete(fullfilename);
                        File.Delete(file);
                    }
                }
                catch
                {}
            }

            Thread.Sleep(100);
        }
        #endregion
        #region private methods
        /// <summary>
        /// gets the last file in the amx folder and increments file number by 1;
        /// </summary>
        private void determinelastfilenumber()
        {
            var dirInfo = new DirectoryInfo(logfiles);
            var allFiles = dirInfo.GetFiles("*." + extension, SearchOption.TopDirectoryOnly);
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

        public void sendalarmorreset_disable(int eventnumber, string dtext, string dtext2, string dtext3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = 2;
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