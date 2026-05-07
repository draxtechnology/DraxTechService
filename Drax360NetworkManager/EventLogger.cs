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
            const string logName = "Service";

            // The MSI registers the source while elevated; this fallback covers
            // first-run boxes where it hasn't been created yet. LocalService
            // can't write to HKLM\...\EventLog, so a missing source must never
            // take the service down — swallow and move on.
            try
            {
                if (!EventLog.SourceExists(source))
                    EventLog.CreateEventSource(source, logName);

                EventLog.WriteEntry(source, message, type);
            }
            catch
            {
            }
        }
    }
}
