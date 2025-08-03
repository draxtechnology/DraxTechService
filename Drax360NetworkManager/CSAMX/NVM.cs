
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace Drax360Service
{
    internal class NVM
    // NWM DATA STRUCT
    {
        #region public variables
        public int OurType; // Type of event - see below [RPJ - long]
        public int OurEvent;    // Event number - must be 0 if Node/Zone/Op/Dat1,2,3,4,5 used [RPJ - long]
        public int On;   // On off. 0=Off <>0=On [RPJ - __int16]
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


        public Byte[] RenderBytes()
        {
            List<Byte> ret = new List<Byte>();
            ret.AddRange(renderlongb(OurType));

            long ourevent = OurEvent | 0x80000000;
            ret.AddRange(renderlongb(ourevent));

            ret.AddRange(render__int16b(On));
            ret.AddRange(render__int16b(Value));
            ret.AddRange(render__int16b(Node));
            ret.AddRange(render__int16b(Zone));
            ret.AddRange(render__int16b(Op));
            ret.AddRange(render__int16b(ControlType));
            ret.AddRange(renderlongb(Dat1));
            ret.AddRange(renderlongb(Dat2));
            ret.AddRange(renderlongb(Dat3));
            ret.AddRange(renderlongb(Dat4));
            ret.AddRange(renderlongb(Dat5));
            ret.AddRange(renderlongb(Dat6));
            ret.AddRange(rendertimeb());
            foreach (int s in Spare)
            {
                ret.AddRange(renderlongb(s));
            }
            ret.AddRange(rendercharb(Text, 64));
            ret.AddRange(rendercharb(Text2, 40));
            ret.AddRange(rendercharb(Text3, 40));
            return ret.ToArray();
        }

        #endregion

        #region private methods

        private byte[] renderlongb(int ourint)
        {
            long ourlong = (long)ourint;
            return renderlongb(ourlong);
        }

        private byte[] renderlongb(long ourlong)
        {
            byte[] buffer = new byte[4];

            buffer[0] = (byte)(ourlong & 0xFF);         // Least significant byte
            buffer[1] = (byte)(ourlong >> 8 & 0xFF);
            buffer[2] = (byte)(ourlong >> 16 & 0xFF);
            buffer[3] = (byte)(ourlong >> 24 & 0xFF); // Most significant byte
            return buffer;

        }





        private byte[] rendertimeb()
        {
            long secondsSince1970 = DateTimeOffset.Now.ToUnixTimeSeconds();




            return renderlongb(secondsSince1970);
        }


        private byte[] render__int16b(bool ourval)
        {
            return render__int16b(ourval ? 1 : 0);
        }
        private byte[] render__int16b(int ourval)
        {
            byte[] buffer = new byte[2];

            buffer[0] = (byte)(ourval & 0xFF);
            buffer[1] = (byte)(ourval >> 8 & 0xFF);
            return buffer;

        }
        private byte[] rendercharb(string ourstr, int ourwidth)
        {
            string ret = ourstr.PadRight(ourwidth, (char)0);
            return Encoding.ASCII.GetBytes(ret);
        }
        #endregion
    }
}
