using System;

namespace FactionTactics.Util
{
    /// <summary>Optional debug sink so shared intent sync works in server + client assemblies.</summary>
    public static class FactionTacticsLog
    {
        public static Action<string>? DebugSink { get; set; }

        public static void Debug(string message)
        {
            try { DebugSink?.Invoke(message); }
            catch { /* never throw from logging */ }
        }
    }
}
