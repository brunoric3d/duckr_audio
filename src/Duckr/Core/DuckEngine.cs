/*
 *  Duckr
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Runs Duckr: every ~30 ms it checks which app has focus and how loud
 *  that app is, asks DuckController for a gain, and fades every other app's
 *  volume by that gain. Original volumes are remembered (in memory and in a
 *  small safety file) and always put back.
 *
 *  Must be created and used on the UI thread (COM objects are STA here).
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Duckr
{
    public sealed class DuckEngine : IDisposable
    {
        /// <summary>Event context passed to SetMasterVolume, so our own changes are recognizable.</summary>
        public static readonly Guid EventContext = new("6E4F2C1A-8B3D-4F5E-9A7C-0D1E2F3A4B5C");

        private const int TickMs = 30;
        private const int RescanMs = 1000;

        /// <summary>"Long ago" time. Not long.MinValue: now - MinValue overflows to a negative number.</summary>
        private const long NeverMs = -1_000_000_000L;

        private readonly AudioSessionScanner m_scanner = new();
        private readonly System.Windows.Forms.Timer m_timer = new();
        private readonly Stopwatch m_clock = Stopwatch.StartNew();
        private readonly int m_ownPid = Environment.ProcessId;

        private DuckrSettings m_settings;
        private DuckController m_controller;
        private HotkeyWindow? m_hotkey;

        private long m_lastRescanMs = NeverMs;
        private string m_focusedName = "";
        private DuckState m_lastState = DuckState.Idle;
        private bool m_forceDuck;

        /// <summary>Sessions we have lowered: instance id -> (volume before we touched it, volume we last set).</summary>
        private readonly Dictionary<string, Tracked> m_tracked = new();

        private sealed class Tracked
        {
            public float Original;
            public float LastSet;
        }

        public DuckEngine(DuckrSettings settings)
        {
            m_settings = settings;
            m_controller = new DuckController(settings);
            m_timer.Interval = TickMs;
            m_timer.Tick += (_, _) => Tick();
        }

        // ---- public surface ---------------------------------------------------

        public bool IsRunning => m_timer.Enabled;

        public DuckrSettings Settings => m_settings;

        public DuckState State => m_controller.State;

        public string FocusedApp => m_focusedName;

        /// <summary>Loudest peak (0..1) of the focused app on the last tick.</summary>
        public float FocusedPeak { get; private set; }

        /// <summary>Gain applied to other apps right now (1 = untouched).</summary>
        public float CurrentGain => m_controller.Gain;

        /// <summary>Settings were changed in place (skin sliders); save them soon.</summary>
        public void SaveSettingsSoon()
        {
            m_saveDue = m_clock.ElapsedMilliseconds + 800;
            if (!m_saveTimerHooked)
            {
                m_saveTimerHooked = true;
                m_saveTimer.Interval = 250;
                m_saveTimer.Tick += (_, _) =>
                {
                    if (m_saveDue > 0 && m_clock.ElapsedMilliseconds >= m_saveDue)
                    {
                        m_saveDue = 0;
                        m_saveTimer.Stop();
                        try { m_settings.Save(); }
                        catch (Exception ex) { DuckrLog.Write($"[!] Duckr could not save settings: {ex.Message}"); }
                    }
                };
            }
            m_saveTimer.Start();
        }

        private readonly System.Windows.Forms.Timer m_saveTimer = new();
        private bool m_saveTimerHooked;
        private long m_saveDue;

        public bool ForceDuck
        {
            get => m_forceDuck;
            set
            {
                if (m_forceDuck == value) return;
                m_forceDuck = value;
                DuckrLog.Write(value ? "[+] Duckr: force duck ON" : "[+] Duckr: force duck OFF");
                ForceDuckChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Fired on the UI thread when the duck state changes (for the tray icon text).</summary>
        public event EventHandler? StateChanged;

        public event EventHandler? ForceDuckChanged;

        public void Start()
        {
            if (IsRunning) return;
            RecoverFromJournal();
            RegisterHotkey();
            m_controller.Reset();
            m_lastRescanMs = NeverMs;
            m_timer.Start();
            DuckrLog.Write("[+] Duckr started");
        }

        /// <summary>Stops and puts every touched volume back at once.</summary>
        public void Stop()
        {
            if (!IsRunning && m_tracked.Count == 0) return;
            m_timer.Stop();
            m_hotkey?.Dispose();
            m_hotkey = null;
            m_forceDuck = false;
            FocusedPeak = 0f;
            RestoreAll();
            m_controller.Reset();
            SetState(DuckState.Idle);
            DuckrLog.Write("[+] Duckr stopped, volumes restored");
        }

        /// <summary>Swap in new settings (from the settings window) without losing state.</summary>
        public void ApplySettings(DuckrSettings settings)
        {
            bool wasRunning = IsRunning;
            Stop();
            m_settings = settings;
            m_controller = new DuckController(settings);
            if (wasRunning && settings.Enabled)
                Start();
        }

        /// <summary>Live level of every app, for the settings window meter list.</summary>
        public IEnumerable<(string Name, float Peak)> SnapshotLevels()
        {
            return m_scanner.Sessions
                .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.Max(s => s.Peak)))
                .OrderByDescending(x => x.Item2);
        }

        public void Dispose()
        {
            Stop();
            if (m_saveDue > 0)
            {
                m_saveDue = 0;
                try { m_settings.Save(); } catch { }
            }
            m_saveTimer.Dispose();
            m_timer.Dispose();
            m_scanner.Dispose();
        }

        // ---- main loop ----------------------------------------------------------

        private void Tick()
        {
            long now = m_clock.ElapsedMilliseconds;
            try
            {
                m_tickCount++;
                if (m_tickCount <= 2) DebugLog.Write($"tick {m_tickCount}: start");

                if (now - m_lastRescanMs >= RescanMs)
                {
                    var sw = Stopwatch.StartNew();
                    m_scanner.Rescan();
                    m_lastRescanMs = now;
                    DropGoneSessions();
                    string scanInfo = $"devices={m_scanner.LastDeviceCount} sessions={m_scanner.Sessions.Count} err={m_scanner.LastError}";
                    if (m_scanCount++ < 3 || scanInfo != m_lastScanInfo)
                        DebugLog.Write($"scan: {scanInfo} took={sw.ElapsedMilliseconds}ms");
                    m_lastScanInfo = scanInfo;
                }

                int focusedPid = GetForegroundPid();
                string focused = focusedPid == m_ownPid ? "" : m_scanner.GetProcessName(focusedPid);
                m_focusedName = focused;

                bool ignored = focused.Length == 0 || DuckrSettings.Matches(m_settings.NeverTouchApps, focused);

                float peak = 0f;
                if (!ignored)
                {
                    foreach (var s in m_scanner.Sessions)
                        if (!s.IsSystemSounds && string.Equals(s.ProcessName, focused, StringComparison.OrdinalIgnoreCase))
                            peak = Math.Max(peak, s.Peak);
                }

                float gain = m_controller.Update(new DuckInput
                {
                    NowMs = now,
                    FocusedPeak = peak,
                    FocusedIsFocusOnly = !ignored && DuckrSettings.Matches(m_settings.FocusOnlyApps, focused),
                    FocusedIsIgnored = ignored,
                    ForceDuck = m_forceDuck,
                    FocusKey = focused,
                });

                FocusedPeak = peak;
                ApplyGain(gain, focused);
                WriteDebugSnapshot(now, focusedPid, focused, ignored, peak, gain);
                SetState(m_controller.State);
            }
            catch (Exception ex)
            {
                // never let a COM hiccup kill the loop; rescan next tick
                DuckrLog.Write($"[!] Duckr tick error: {ex}");
                m_lastRescanMs = NeverMs;
            }
        }

        private bool IsTarget(AudioSession s, string focused)
        {
            if (!s.IsSystemSounds && string.Equals(s.ProcessName, focused, StringComparison.OrdinalIgnoreCase))
                return false; // never lower the app you're working in
            if (s.Pid == m_ownPid)
                return false;
            if (DuckrSettings.Matches(m_settings.NeverTouchApps, s.ProcessName))
                return false;
            if (m_settings.Scope == DuckScope.MusicAppsOnly)
                return DuckrSettings.Matches(m_settings.MusicApps, s.ProcessName);
            return true;
        }

        private void ApplyGain(float gain, string focused)
        {
            bool journalDirty = false;

            foreach (var s in m_scanner.Sessions)
            {
                bool target = gain < 0.9999f && IsTarget(s, focused);
                m_tracked.TryGetValue(s.InstanceId, out var t);

                if (!target)
                {
                    if (t != null)
                    {
                        s.SetVolume(t.Original);
                        m_tracked.Remove(s.InstanceId);
                        journalDirty = true;
                    }
                    continue;
                }

                float? current = s.Volume;
                if (current == null) continue;

                if (t == null)
                {
                    // first time we touch this session: remember where the user had it
                    t = new Tracked { Original = current.Value, LastSet = current.Value };
                    m_tracked[s.InstanceId] = t;
                    journalDirty = true;
                }
                else if (Math.Abs(current.Value - t.LastSet) > 0.01f && gain > 0.05f)
                {
                    // user moved this app's slider while ducked: treat it as the new normal
                    t.Original = Math.Clamp(current.Value / gain, 0f, 1f);
                    journalDirty = true;
                }

                float wanted = t.Original * gain;
                if (Math.Abs(wanted - t.LastSet) > 0.001f)
                {
                    if (s.SetVolume(wanted))
                        t.LastSet = wanted;
                }
            }

            if (journalDirty)
                WriteJournal();
        }

        private void DropGoneSessions()
        {
            var alive = new HashSet<string>(m_scanner.Sessions.Select(s => s.InstanceId));
            var gone = m_tracked.Keys.Where(k => !alive.Contains(k)).ToList();
            if (gone.Count == 0) return;
            foreach (var k in gone) m_tracked.Remove(k);
            WriteJournal();
        }

        private void RestoreAll()
        {
            if (m_tracked.Count > 0)
            {
                try
                {
                    m_scanner.Rescan();
                    foreach (var s in m_scanner.Sessions)
                        if (m_tracked.TryGetValue(s.InstanceId, out var t))
                            s.SetVolume(t.Original);
                }
                catch (Exception ex)
                {
                    DuckrLog.Write($"[!] Duckr restore error: {ex.Message}");
                }
                m_tracked.Clear();
            }
            WriteJournal();
        }

        private void SetState(DuckState state)
        {
            if (state == m_lastState) return;
            m_lastState = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- safety journal -------------------------------------------------------
        // If the app crashes or the PC sleeps mid-duck, the next start reads this
        // file and puts volumes back for sessions that are still open.

        private static string JournalPath => Path.Combine(DuckrSettings.DefaultFolder, "ducked-volumes.json");

        private void WriteJournal()
        {
            try
            {
                if (m_tracked.Count == 0)
                {
                    if (File.Exists(JournalPath)) File.Delete(JournalPath);
                    return;
                }
                Directory.CreateDirectory(DuckrSettings.DefaultFolder);
                var data = m_tracked.ToDictionary(kv => kv.Key, kv => kv.Value.Original);
                File.WriteAllText(JournalPath, JsonSerializer.Serialize(data));
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Duckr could not write safety file: {ex.Message}");
            }
        }

        private void RecoverFromJournal()
        {
            try
            {
                if (!File.Exists(JournalPath)) return;
                var data = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(JournalPath));
                File.Delete(JournalPath);
                if (data == null || data.Count == 0) return;

                m_scanner.Rescan();
                int restored = 0;
                foreach (var s in m_scanner.Sessions)
                    if (data.TryGetValue(s.InstanceId, out float original) && s.SetVolume(original))
                        restored++;
                if (restored > 0)
                    DuckrLog.Write($"[+] Duckr put back {restored} app volume(s) left lowered by an earlier run");
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Duckr could not read safety file: {ex.Message}");
            }
        }

        // ---- hotkey -------------------------------------------------------------------

        private void RegisterHotkey()
        {
            m_hotkey?.Dispose();
            m_hotkey = null;
            if (!HotkeyWindow.TryParse(m_settings.ForceDuckHotkey, out uint mods, out uint vk))
                return;
            m_hotkey = new HotkeyWindow(() => ForceDuck = !ForceDuck);
            if (!m_hotkey.Register(mods, vk))
            {
                DuckrLog.Write($"[!] Duckr: hotkey {m_settings.ForceDuckHotkey} is taken by another app");
                m_hotkey.Dispose();
                m_hotkey = null;
            }
        }

        // ---- focus ----------------------------------------------------------------------

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

        private int GetForegroundPid()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out int pid);

            // Store apps (Media Player, Movies & TV) live inside a frame window owned by
            // ApplicationFrameHost. The real app owns one of its child windows.
            if (string.Equals(m_scanner.GetProcessName(pid), "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                int framePid = pid, inner = 0;
                EnumChildWindows(hwnd, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out int cpid);
                    if (cpid != framePid && cpid != 0) { inner = cpid; return false; }
                    return true;
                }, IntPtr.Zero);
                if (inner != 0) pid = inner;
            }
            return pid;
        }

        // ---- debug log ------------------------------------------------------------------
        // One line per second (and on every state change) in creator-debug.log next to
        // the exe, so problems can be diagnosed without watching the screen.

        private long m_lastDebugMs = NeverMs;
        private long m_tickCount;
        private int m_scanCount;
        private string m_lastScanInfo = "";
        private DuckState m_lastDebugState = DuckState.Idle;

        private void WriteDebugSnapshot(long now, int focusedPid, string focused, bool ignored, float peak, float gain)
        {
            var state = m_controller.State;
            if (now - m_lastDebugMs < 1000 && state == m_lastDebugState) return;
            m_lastDebugMs = now;
            m_lastDebugState = state;

            var sb = new System.Text.StringBuilder();
            sb.Append($"focus={(focused.Length > 0 ? focused : "?")}[{focusedPid}] ignored={ignored} focusPeak={Db(peak)} " +
                      $"gain={gain:0.000} state={state} force={m_forceDuck} scope={m_settings.Scope} tracked={m_tracked.Count} | ");
            foreach (var s in m_scanner.Sessions)
            {
                float? v = s.Volume;
                sb.Append($"{(s.IsSystemSounds ? "SYS" : s.ProcessName)}[{s.Pid}] pk={Db(s.Peak)} vol={(v.HasValue ? v.Value.ToString("0.00") : "?")} " +
                          $"tgt={(IsTarget(s, focused) ? 1 : 0)}; ");
            }
            DebugLog.Write(sb.ToString());
        }

        private static string Db(float p) => p > 0 ? $"{20 * Math.Log10(p):0}dB" : "-inf";
    }

    /// <summary>Hidden window that receives one global hotkey.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0xC0DE;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly Action m_onPressed;
        private bool m_registered;

        public HotkeyWindow(Action onPressed)
        {
            m_onPressed = onPressed;
            CreateHandle(new CreateParams());
        }

        public bool Register(uint mods, uint vk)
        {
            m_registered = RegisterHotKey(Handle, HotkeyId, mods | MOD_NOREPEAT, vk);
            return m_registered;
        }

        /// <summary>Parses text like "Ctrl+Alt+D" or "Ctrl+Shift+F9".</summary>
        public static bool TryParse(string? text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": mods |= MOD_WIN; break;
                    default:
                        // a bare digit would parse as a raw enum number, so map it to the D0..D9 key
                        string name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                        if (int.TryParse(name, out _)) return false;
                        if (!Enum.TryParse<Keys>(name, true, out var key)) return false;
                        vk = (uint)key;
                        break;
                }
            }
            return vk != 0 && mods != 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
                m_onPressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (m_registered) UnregisterHotKey(Handle, HotkeyId);
            m_registered = false;
            DestroyHandle();
        }
    }
}

namespace Duckr
{
    /// <summary>Plain text log next to the exe. Capped at about 2 MB (then starts over).</summary>
    public static class DebugLog
    {
        private static readonly object s_lock = new();
        private static readonly string s_path = Path.Combine(AppContext.BaseDirectory, "duckr-debug.log");

        public static void Write(string line)
        {
            try
            {
                lock (s_lock)
                {
                    var fi = new FileInfo(s_path);
                    if (fi.Exists && fi.Length > 2_000_000) fi.Delete();
                    File.AppendAllText(s_path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // file busy or read-only: skip this line, try again next time.
                // Never break the app over a log.
            }
        }
    }
}
