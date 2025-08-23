using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drax360Service
{
    public class EventLogger
    {
        public static void WriteToEventLog(string message, EventLogEntryType type)
        {

            if (!Elements.isService) return;
            string source = "Drax360";
            string logName = "Service";

            // Check if the source exists; if not, create it
            if (!EventLog.SourceExists(source))
            {
                EventLog.CreateEventSource(source, logName);
            }

            // Write the entry to the event log
            EventLog.WriteEntry(source, message, type);
        }
    }
}
