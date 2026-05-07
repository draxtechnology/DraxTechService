using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DraxTechnology
{
    public class EventLogger
    {
        public static void WriteToEventLog(string message, EventLogEntryType type)
        {
            if (!Elements.isService) return;
            const string source = "Drax360";

            // SourceExists enumerates every event log under HKLM and reads each
            // one's Sources list; LocalService can't read them all (Security log
            // in particular) and the call throws SecurityException — which used
            // to take the service down with Error 1064. The MSI registers the
            // source at install time, so we just write directly. Swallow on
            // failure: a logging miss must never crash the service.
            try
            {
                EventLog.WriteEntry(source, message, type);
            }
            catch
            {
            }
        }
    }
}
