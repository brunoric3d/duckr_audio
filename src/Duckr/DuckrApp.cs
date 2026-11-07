/*
 *  Duckr - the tray app that owns the engine and the windows.
 *  Licensed under GNU GPL v3. See LICENSE.
 */

using System.Reflection;
using Duckr.Skin;

namespace Duckr
{
    internal sealed class DuckrApp : ApplicationContext
    {
        private readonly DuckEngine m_engine;
        private readonly NotifyIcon m_tray;
        private readonly ToolStripMenuItem m_powerItem;
        private readonly ToolStripMenuItem m_forceItem;
        private readonly ToolStripMenuItem m_startupItem;
        private readonly Control m_invoker = new();
        private readonly RegisteredWaitHandle m_showWait;

        private readonly Icon m_iconOn = LoadIcon("duckr_on.ico");
        private readonly Icon m_iconDucking = LoadIcon("duckr_ducking.ico");
        private readonly Icon m_iconOff = LoadIcon("duckr_off.ico");

        private SkinForm? m_skin;
        private SetupForm? m_setup;
        private bool m_exiting;

        public DuckrApp(bool startInTray, EventWaitHandle showEvent)
        {
            _ = m_invoker.Handle; // create the handle so BeginInvoke works from other threads

            DuckrLog.Sink = DebugLog.Write;
            DebugLog.Write("==== Duckr started ====");

            var settings = DuckrSettings.Load();
            m_engine = new DuckEngine(settings);
            m_engine.StateChanged += (_, _) => UpdateTray();
            m_engine.ForceDuckChanged += (_, _) => UpdateTray();

            // tray
            m_powerItem = new ToolStripMenuItem("Duckr on", null, (_, _) => SetOn(!IsOn));
            m_forceItem = new ToolStripMenuItem("Force lower other apps", null, (_, _) =>
            {
                if (IsOn) m_engine.ForceDuck = !m_engine.ForceDuck;
            });
            m_startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetAutostart(!Autostart.IsEnabled));

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Open Duckr", null, (_, _) => ShowSkin()) { Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(m_powerItem);
            menu.Items.Add(m_forceItem);
            menu.Items.Add(new ToolStripMenuItem("Setup...", null, (_, _) => ShowSetup()));
            menu.Items.Add(m_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitDuckr()));
            menu.Opening += (_, _) => UpdateTray();

            m_tray = new NotifyIcon
            {
                Icon = m_iconOff,
                Text = "Duckr",
                ContextMenuStrip = menu,
                Visible = true,
            };
            m_tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSkin(); };

            // a second Duckr.exe start asks us to show the window
            m_showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
                (_, _) => { if (!m_exiting) m_invoker.BeginInvoke(ShowSkin); }, null, Timeout.Infinite, executeOnlyOnce: false);

            if (settings.Enabled)
                SetOn(true, save: false);
            UpdateTray();

            if (!startInTray)
                ShowSkin();
        }

        private bool IsOn => m_engine.IsRunning;

        private void SetOn(bool on, bool save = true)
        {
            if (on) m_engine.Start();
            else m_engine.Stop();

            if (save && m_engine.Settings.Enabled != on)
            {
                m_engine.Settings.Enabled = on;
                try { m_engine.Settings.Save(); }
                catch (Exception ex) { DuckrLog.Write($"[!] Could not save settings: {ex.Message}"); }
            }
            UpdateTray();
        }

        private static void SetAutostart(bool enabled) => Autostart.Set(enabled);

        private void ShowSkin()
        {
            if (m_exiting) return;
            if (m_skin == null || m_skin.IsDisposed)
            {
                m_skin = new SkinForm(m_engine,
                    isOn: () => IsOn,
                    setOn: on => SetOn(on),
                    openSetup: ShowSetup,
                    isAutostart: () => Autostart.IsEnabled,
                    setAutostart: SetAutostart);
            }
            m_skin.Show();
            if (m_skin.WindowState == FormWindowState.Minimized) m_skin.WindowState = FormWindowState.Normal;
            m_skin.Activate();
        }

        private void ShowSetup()
        {
            if (m_setup != null && !m_setup.IsDisposed)
            {
                m_setup.Activate();
                return;
            }
            m_setup = new SetupForm(m_engine, dark: true);
            m_setup.SettingsSaved += (_, s) =>
            {
                bool wasOn = IsOn;
                m_engine.ApplySettings(s);
                if (s.Enabled != wasOn)
                    SetOn(s.Enabled, save: false);
                UpdateTray();
            };
            m_setup.FormClosed += (_, _) => m_setup = null;
            m_setup.Show();
        }

        private void UpdateTray()
        {
            bool on = IsOn;
            m_powerItem.Checked = on;
            m_forceItem.Enabled = on;
            m_forceItem.Checked = on && m_engine.ForceDuck;
            string hk = m_engine.Settings.ForceDuckHotkey;
            m_forceItem.Text = hk.Length > 0 ? $"Force lower other apps ({hk})" : "Force lower other apps";
            m_startupItem.Checked = Autostart.IsEnabled;

            bool lowering = on && (m_engine.ForceDuck || m_engine.State != DuckState.Idle);
            m_tray.Icon = !on ? m_iconOff : lowering ? m_iconDucking : m_iconOn;

            string text = !on ? "Duckr: off"
                : m_engine.ForceDuck ? "Duckr: forced (hotkey)"
                : m_engine.State switch
                {
                    DuckState.Ducking => "Duckr: lowering other apps",
                    DuckState.Holding => "Duckr: holding",
                    DuckState.Restoring => "Duckr: restoring",
                    _ => "Duckr: listening",
                };
            m_tray.Text = text.Length > 63 ? text[..63] : text;
        }

        private void ExitDuckr()
        {
            if (m_exiting) return;
            m_exiting = true;
            // Dispose stops the engine, which puts every lowered volume back
            m_engine.Dispose();
            m_tray.Visible = false;
            m_skin?.Dispose();
            m_setup?.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!m_exiting) { m_exiting = true; m_engine.Dispose(); }
                m_showWait.Unregister(null);
                m_tray.Visible = false;
                m_tray.Dispose();
                m_invoker.Dispose();
                m_iconOn.Dispose();
                m_iconDucking.Dispose();
                m_iconOff.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Icon LoadIcon(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) return (Icon)SystemIcons.Application.Clone();
            return new Icon(stream, SystemInformation.SmallIconSize);
        }
    }
}
