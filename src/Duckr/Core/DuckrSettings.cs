/*
 *  Duckr
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Settings for Duckr, saved as JSON in %APPDATA%\Duckr\settings.json.
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duckr
{
    public enum DuckScope
    {
        /// <summary>Lower every app except the focused one (true "isolate" mode).</summary>
        AllOtherApps,
        /// <summary>Lower only the apps in MusicApps.</summary>
        MusicAppsOnly,
    }

    public sealed class DuckrSettings
    {
        // ---- main switches -------------------------------------------------

        /// <summary>When on, Duckr is listening and lowering other apps.</summary>
        public bool Enabled { get; set; } = true;

        public DuckScope Scope { get; set; } = DuckScope.AllOtherApps;

        // ---- levels and timing ---------------------------------------------

        /// <summary>How loud other apps stay while ducked. 0 = muted.</summary>
        public int DuckLevelPercent { get; set; } = 15;

        /// <summary>Fade time going down.</summary>
        public int DuckFadeMs { get; set; } = 250;

        /// <summary>Fade time coming back up.</summary>
        public int RestoreFadeMs { get; set; } = 900;

        /// <summary>How long the focused app must play before ducking starts.</summary>
        public int AttackMs { get; set; } = 120;

        /// <summary>How long to wait after the focused app goes quiet before restoring.</summary>
        public int ReleaseMs { get; set; } = 1500;

        /// <summary>Sound level (dBFS peak) that counts as "playing". -50 dB is very quiet.</summary>
        public int ThresholdDb { get; set; } = -50;

        // ---- app lists -------------------------------------------------------
        // Names are process names without ".exe", not case sensitive.
        // End a name with * to match anything that starts with it ("Ableton Live*").

        /// <summary>Apps that play your background music. Only used by the MusicAppsOnly scope.</summary>
        public List<string> MusicApps { get; set; } = new()
        {
            "Spotify", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
            "AppleMusic", "iTunes", "TIDAL", "Deezer", "MusicBee", "foobar2000",
            "AIMP", "Winamp", "YouTube Music*", "Microsoft.Media.Player",
        };

        /// <summary>Apps that are never lowered and never trigger (calls, streaming software).</summary>
        public List<string> NeverTouchApps { get; set; } = new()
        {
            "Discord", "Teams", "ms-teams", "Zoom", "Slack", "obs64", "Streamlabs*",
        };

        /// <summary>
        /// Never-touch apps whose sound lowers the music even when they are not
        /// focused: a friend talking on a call. Per app, toggled in Setup.
        /// </summary>
        public List<string> VoiceApps { get; set; } = new()
        {
            "Discord", "Teams", "ms-teams", "Zoom", "Slack",
        };

        /// <summary>
        /// Apps that duck others just by being focused, no sound needed. For DAWs
        /// that use ASIO drivers, which Windows can't hear. Empty by default because
        /// it also ducks while you're not playing anything.
        /// </summary>
        public List<string> FocusOnlyApps { get; set; } = new();

        // ---- hotkey ----------------------------------------------------------

        /// <summary>Global hotkey to force ducking on/off. Format like "Ctrl+Alt+D". Empty = off.</summary>
        public string ForceDuckHotkey { get; set; } = "Ctrl+Alt+D";

        // ---- derived ---------------------------------------------------------

        [JsonIgnore]
        public float ThresholdLinear => (float)Math.Pow(10.0, Math.Clamp(ThresholdDb, -90, 0) / 20.0);

        // ---- matching ---------------------------------------------------------

        public static bool Matches(IEnumerable<string> list, string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            foreach (var raw in list)
            {
                var entry = raw?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry.EndsWith('*'))
                {
                    if (processName.StartsWith(entry[..^1], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (string.Equals(processName, entry, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // ---- load / save -----------------------------------------------------

        private static readonly JsonSerializerOptions s_json = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duckr");

        public static string DefaultPath => Path.Combine(DefaultFolder, "settings.json");

        public static DuckrSettings Load(string? path = null)
        {
            path ??= DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<DuckrSettings>(File.ReadAllText(path), s_json);
                    if (loaded != null)
                        return loaded.Sanitized();
                }
            }
            catch (Exception ex)
            {
                DuckrLog.Write($"[!] Duckr settings could not be read, using defaults: {ex.Message}");
            }
            return new DuckrSettings();
        }

        public void Save(string? path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // write to temp then move, so a crash never leaves a half file
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, s_json));
            File.Move(tmp, path, overwrite: true);
        }

        public DuckrSettings Clone() =>
            JsonSerializer.Deserialize<DuckrSettings>(JsonSerializer.Serialize(this, s_json), s_json)!;

        /// <summary>Keep numbers in safe ranges in case the JSON was edited by hand.</summary>
        public DuckrSettings Sanitized()
        {
            DuckLevelPercent = Math.Clamp(DuckLevelPercent, 0, 100);
            DuckFadeMs = Math.Clamp(DuckFadeMs, 0, 10000);
            RestoreFadeMs = Math.Clamp(RestoreFadeMs, 0, 10000);
            AttackMs = Math.Clamp(AttackMs, 0, 5000);
            ReleaseMs = Math.Clamp(ReleaseMs, 0, 30000);
            ThresholdDb = Math.Clamp(ThresholdDb, -90, 0);
            MusicApps ??= new();
            NeverTouchApps ??= new();
            VoiceApps ??= new();
            FocusOnlyApps ??= new();
            ForceDuckHotkey ??= "";
            return this;
        }
    }
}
