using System;
using System.Diagnostics;
using System.Threading;

namespace ExportCdr
{
    internal static class Logger
    {
        public static void WriteInfo(string text)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));
            Program.Service.EventLog.WriteEntry(text, EventLogEntryType.Information);
        }

        public static void Write(string text)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));
            Program.Service.EventLog.WriteEntry(text, EventLogEntryType.Error);
        }
    }
}