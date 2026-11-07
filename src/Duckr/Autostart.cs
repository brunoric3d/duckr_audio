/*
 *  Duckr - start with Windows.
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Uses the per-user Run key (HKCU), so no admin rights are needed and only
 *  the current user is affected. Starts with --tray so no window pops up.
 */

using Microsoft.Win32;

namespace Duckr
{
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Duckr";

        private static string Command => $"\"{Application.ExecutablePath}\" --tray";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                    return key?.GetValue(ValueName) is string v &&
                           v.Equals(Command, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (enabled) key.SetValue(ValueName, Command);
                else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, throwOnMissingValue: false);
                DuckrLog.Write(enabled ? "[+] Duckr will start with Windows" : "[+] Duckr will not start with Windows");
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Could not change start with Windows: {ex.Message}");
            }
        }
    }
}
