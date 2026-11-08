/*
 *  Duckr - updates from GitHub releases (Velopack).
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Only works when Duckr was installed with the installer (not when run from
 *  the build folder). Checks quietly at startup and on demand from the tray.
 */

using Velopack;
using Velopack.Sources;

namespace Duckr
{
    internal sealed class Updater
    {
        public const string RepoUrl = "https://github.com/brunoric3d/duckr_audio";

        private readonly UpdateManager? m_manager;
        private UpdateInfo? m_pending;
        private bool m_busy;

        public Updater()
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                if (mgr.IsInstalled) m_manager = mgr;
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Updater unavailable: {ex.Message}");
            }
        }

        /// <summary>False when running from a build folder instead of an installed copy.</summary>
        public bool IsInstalled => m_manager != null;

        public string CurrentVersion => m_manager?.CurrentVersion?.ToString() ?? "dev build";

        /// <summary>Version waiting to be installed, or null.</summary>
        public string? PendingVersion => m_pending?.TargetFullRelease.Version.ToString();

        public bool Busy => m_busy;

        /// <summary>Returns the new version, or null when up to date (or not installed / offline).</summary>
        public async Task<string?> CheckAsync()
        {
            if (m_manager == null || m_busy) return null;
            m_busy = true;
            try
            {
                m_pending = await m_manager.CheckForUpdatesAsync();
                if (m_pending != null)
                    DuckrLog.Write($"[+] Update available: {PendingVersion} (installed {CurrentVersion})");
                return PendingVersion;
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Update check failed: {ex.Message}");
                return null;
            }
            finally { m_busy = false; }
        }

        /// <summary>Downloads the pending update, runs beforeRestart, then installs and restarts Duckr.</summary>
        public async Task<bool> DownloadAndRestartAsync(Action beforeRestart)
        {
            if (m_manager == null || m_pending == null || m_busy) return false;
            m_busy = true;
            try
            {
                await m_manager.DownloadUpdatesAsync(m_pending);
                DuckrLog.Write($"[+] Update {PendingVersion} downloaded, restarting");
                beforeRestart();
                m_manager.ApplyUpdatesAndRestart(m_pending);
                return true;
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Update failed: {ex.Message}");
                return false;
            }
            finally { m_busy = false; }
        }
    }
}
