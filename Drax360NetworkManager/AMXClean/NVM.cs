
using System;
using System.Text;


namespace Drax360Service.AMXClean
{
    internal class NVM
    // NWM DATA STRUCT
    {
        #region public variables
        public int OurType; // Type of event - see below [RPJ - long]
        public int OurEvent;    // Event number - must be 0 if Node/Zone/Op/Dat1,2,3,4,5 used [RPJ - long]
        public int OnOff;   // On off. 0=Off <>0=On [RPJ - __int16]
        public int Value;   // [RPJ - __int16]
        public int Node;    // Node number	- set event to 0 if using this [RPJ - __int16]
        public int Zone;    // Zone number	- set event to 0 if using this [RPJ - __int16]
        public int Op;  // Output number - set event to 0 if using this [RPJ - __int16]
        public int ControlType; // Control type	- set event to 0 if using this [RPJ - __int16]
        public int Dat1;    // Data 1		- set event to 0 if using this used [RPJ - long]
        public int Dat2;    // Data 2		- set event to 0 if using this used [RPJ - long]
        public int Dat3;    // Data 3		- set event to 0 if using this used [RPJ - long]
        public int Dat4;    // Data 4		- set event to 0 if using this used [RPJ - long]
        public int Dat5;    // Data 5		- set event to 0 if using this used [RPJ - long]
        public int Dat6;    // Data 6		- set event to 0 if using this used [RPJ - long]
        //public DateTime Time;   // Time in long format used [RPJ - long, auto using current date time]
        public int[] Spare;   // For future expansion used [RPJ - long [8] ]
        public string Text; // Used for text messages used [RPJ - Text 64]
        public string Text2;    // This one used for device type strings etc [RPJ - Text 40]
        public string Text3;    // Spare (e.g. zone text in Advanced) [RPJ - Text 40]
        #endregion

        #region constructor
        public NVM()
        {
           
            Spare = new int[8];
            for (int i = 0; i < Spare.Length; i++)
            {
                Spare[i] = 0;
            }
            Text = "";
            Text2 = "";
            Text3 = "";
        }
        #endregion

        #region public methods
        public string Render()
        {
            string ret = "";
            ret += renderlong(OurType);
            ret += renderlong(OurEvent);
            
            ret += render__int16(OnOff);
            ret += render__int16(Value);
            ret += render__int16(Node);
            ret += render__int16(Zone);
            ret += render__int16(Op);
            ret += render__int16(ControlType);
            ret += renderlong(Dat1);
            ret += renderlong(Dat2);
            ret += renderlong(Dat3);
            ret += renderlong(Dat4);
            ret += renderlong(Dat5);
            ret += renderlong(Dat6);
            ret += rendertime();
            foreach (int s in Spare)
            {
                ret += renderlong(s);
            }
            ret += renderchar(Text.Length > 40 ? Text.Substring(0, 40) : Text, 64);
            ret += renderchar(Text2, 40);
            ret += renderchar(Text3, 40);
            return ret;
        }
        #endregion

        #region private methods
        private string renderlong(int ourint)
        {
            long ourlong = (long)ourint;
            return renderlong(ourlong);
        }

        private string renderlong(long ourlong)
        {
            byte[] bytevals = BitConverter.GetBytes((int)ourlong);
            string ret = Encoding.ASCII.GetString(bytevals, 0, bytevals.Length);
            return ret;
        }

        private string rendertime()
        {
            long secondsSince1970 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return renderlong(secondsSince1970);
        }

        private string rendertimeNEW()
        {
            DateTimeOffset fixedTime = new DateTimeOffset(2025, 7, 8, 9, 4, 00, TimeSpan.Zero);
            //DateTimeOffset fixedTime = new DateTimeOffset(System.DateTime.Now.Year, 7, 8, 15, 24, 00, TimeSpan.Zero);
            uint unixSeconds = (uint)fixedTime.ToUnixTimeSeconds(); // ensures it's unsigned 32-bit
            byte[] bytes = BitConverter.GetBytes(unixSeconds);      // little-endian 4 bytes
            //return Encoding.GetEncoding("ISO-8859-1").GetString(bytes); // safely converts raw bytes to string
            return Encoding.ASCII.GetString(bytes);
        }


        private string render__int16(int ourval)
        {
            byte[] buffer = new byte[2];

            buffer[0] = (byte)ourval;
            buffer[1] = (byte)(ourval >> 8);

            string ret = Encoding.ASCII.GetString(buffer, 0, buffer.Length);
            return ret;

        }
        private string renderchar(string ourstr, int ourwidth)
        {
            return ourstr.PadRight(ourwidth, (char)0);
        }
        #endregion
    }
}
