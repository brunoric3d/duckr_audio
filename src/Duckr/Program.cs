/*
 *  Duckr - lowers your music while your work app plays.
 *  Licensed under GNU GPL v3. See LICENSE.
 */

namespace Duckr
{
    internal static class Program
    {
        private const string MutexName = "Duckr.SingleInstance.6E4F2C1A";
        private const string ShowEventName = "Duckr.ShowWindow.6E4F2C1A";

        [STAThread]
        private static void Main(string[] args)
        {
            bool startInTray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            // one Duckr at a time; starting it again just brings the window up
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool firstInstance);
            if (!firstInstance)
            {
                try
                {
                    using var show = EventWaitHandle.OpenExisting(ShowEventName);
                    show.Set();
                }
                catch { /* first instance is still starting or already gone */ }
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => DebugLog.Write("[UNHANDLED UI] " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => DebugLog.Write("[UNHANDLED] " + e.ExceptionObject);

            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            using var app = new DuckrApp(startInTray, showEvent);
            Application.Run(app);
        }
    }
}
