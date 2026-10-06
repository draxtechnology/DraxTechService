using System;
using System.Diagnostics;

namespace DraxTechnology
{
    public class EventLogger
    {
        // Set by DraxService so every event-log line also lands in the
        // service's own log file (INSMan.log etc.), interactive or not.
        public static Action<string, EventLogEntryType> Mirror;

        public static void WriteToEventLog(string message, EventLogEntryType type)
        {
            try { Mirror?.Invoke(message, type); } catch { }

            if (!Elements.isService) return;
            const string source = "Drax360";
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
