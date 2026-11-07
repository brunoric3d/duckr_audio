/*
 *  Duckr
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Pure decision logic for Duckr. No Windows calls here, so it can be
 *  unit tested on any machine. DuckEngine feeds it one sample per tick and
 *  applies the returned gain to the "other" apps.
 */

namespace Duckr
{
    /// <summary>
    /// What the focused app is doing right now. Built by DuckEngine every tick.
    /// </summary>
    public readonly struct DuckInput
    {
        /// <summary>Monotonic clock in milliseconds.</summary>
        public long NowMs { get; init; }

        /// <summary>Loudest peak (0..1) of all audio sessions owned by the focused app.</summary>
        public float FocusedPeak { get; init; }

        /// <summary>Focused app is in the "focus only" list (DAWs on ASIO that Windows can't hear).</summary>
        public bool FocusedIsFocusOnly { get; init; }

        /// <summary>Focused app should never trigger ducking (a "never touch" app, or this app).</summary>
        public bool FocusedIsIgnored { get; init; }

        /// <summary>User pressed the hotkey to force ducking on.</summary>
        public bool ForceDuck { get; init; }

        /// <summary>Identifies the focused app (process name). A change means focus moved.</summary>
        public string? FocusKey { get; init; }
    }

    public enum DuckState
    {
        /// <summary>Other apps at full volume.</summary>
        Idle,
        /// <summary>Focused app is making sound, fading other apps down or holding them down.</summary>
        Ducking,
        /// <summary>Focused app went quiet, waiting the release delay before restoring.</summary>
        Holding,
        /// <summary>Fading other apps back up.</summary>
        Restoring,
    }

    /// <summary>
    /// Turns "is the focused app playing?" into a smooth gain for every other app.
    ///
    /// Rules:
    ///  * Sound must stay above the threshold for AttackMs before ducking starts,
    ///    so clicks and UI blips don't pump the music.
    ///  * After the sound stops, wait ReleaseMs before restoring, so short pauses
    ///    between plays (scrubbing, replaying a cut) keep the music down.
    ///  * Gain moves in decibels, so the fade sounds even to the ear.
    /// </summary>
    public sealed class DuckController
    {
        /// <summary>Gain we treat as silence when the duck level is 0%.</summary>
        private const double FloorDb = -60.0;

        /// <summary>"Long ago" time. Not long.MinValue: now - MinValue overflows to a negative number.</summary>
        private const long NeverMs = -1_000_000_000L;

        private readonly DuckrSettings m_settings;

        private long m_lastNowMs = -1;
        private long m_aboveSinceMs = -1;
        private long m_lastActiveMs = NeverMs;
        private bool m_wantDuck;

        private string? m_focusKey;
        /// <summary>Focused app was already playing when it got focus (e.g. you clicked Spotify). Doesn't count until it goes quiet.</summary>
        private bool m_alreadyPlaying;
        private long m_quietSinceMs = -1;

        /// <summary>How long an "already playing" app must be quiet before its sound counts again.</summary>
        private const long RearmQuietMs = 300;

        public DuckController(DuckrSettings settings)
        {
            m_settings = settings;
        }

        /// <summary>Current gain for "other" apps, 1 = untouched, DuckLevel = fully ducked.</summary>
        public float Gain { get; private set; } = 1f;

        public DuckState State { get; private set; } = DuckState.Idle;

        /// <summary>True when any gain below 1 is applied (other apps need their volume managed).</summary>
        public bool IsAffectingOthers => Gain < 0.9999f;

        /// <summary>Jump straight back to full volume (used when Duckr is turned off).</summary>
        public void Reset()
        {
            Gain = 1f;
            State = DuckState.Idle;
            m_wantDuck = false;
            m_aboveSinceMs = -1;
            m_lastActiveMs = NeverMs;
            m_lastNowMs = -1;
            m_focusKey = null;
            m_alreadyPlaying = false;
            m_quietSinceMs = -1;
        }

        /// <summary>Advance one tick. Returns the gain to apply to other apps.</summary>
        public float Update(in DuckInput input)
        {
            long now = input.NowMs;
            long dt = m_lastNowMs < 0 ? 0 : Math.Max(0, now - m_lastNowMs);
            m_lastNowMs = now;

            bool loudNow = input.FocusedPeak >= m_settings.ThresholdLinear;

            // 0) Focus moved to another app?
            //    Only sound that STARTS while an app is focused should duck. Clicking
            //    into an app that is already playing (your music player, a browser with
            //    music) must not lower everything else.
            string key = input.FocusKey ?? "";
            if (key != m_focusKey)
            {
                bool first = m_focusKey == null;
                m_focusKey = key;
                m_alreadyPlaying = loudNow;
                m_quietSinceMs = -1;
                m_aboveSinceMs = -1;
                // the app that caused the duck lost focus: skip the hold, restore now
                if (!first && !input.ForceDuck)
                    m_wantDuck = false;
            }
            if (m_alreadyPlaying)
            {
                if (loudNow)
                {
                    m_quietSinceMs = -1;
                }
                else
                {
                    if (m_quietSinceMs < 0) m_quietSinceMs = now;
                    if (now - m_quietSinceMs >= RearmQuietMs) m_alreadyPlaying = false;
                }
            }

            // 1) Is the focused app "active" right now?
            bool active;
            if (input.ForceDuck)
            {
                active = true;
            }
            else if (input.FocusedIsIgnored)
            {
                active = false;
                m_aboveSinceMs = -1;
            }
            else if (input.FocusedIsFocusOnly)
            {
                active = true;
            }
            else
            {
                bool loud = loudNow && !m_alreadyPlaying;
                if (!loud)
                {
                    m_aboveSinceMs = -1;
                    active = false;
                }
                else
                {
                    if (m_aboveSinceMs < 0)
                        m_aboveSinceMs = now;
                    // Once ducked, any sound counts at once (no new attack wait).
                    active = m_wantDuck || now - m_aboveSinceMs >= m_settings.AttackMs;
                }
            }

            // 2) Decide the target, with release delay.
            if (active)
            {
                m_lastActiveMs = now;
                m_wantDuck = true;
            }
            else if (m_wantDuck)
            {
                // Wait the release delay, so short pauses between plays
                // (scrubbing, replaying a cut) keep the music down.
                if (now - m_lastActiveMs >= m_settings.ReleaseMs)
                    m_wantDuck = false;
            }

            // 3) Move gain toward target in dB.
            double targetDb = m_wantDuck ? DuckDb() : 0.0;
            double currentDb = ToDb(Gain);
            double fadeMs = m_wantDuck ? m_settings.DuckFadeMs : m_settings.RestoreFadeMs;
            double span = -FloorDb; // full 0 dB to -60 dB swing
            double step = fadeMs <= 0 ? double.MaxValue : span * dt / fadeMs;

            if (currentDb > targetDb)
                currentDb = Math.Max(targetDb, currentDb - step);
            else if (currentDb < targetDb)
                currentDb = Math.Min(targetDb, currentDb + step);

            Gain = currentDb >= -0.0001 ? 1f : (float)FromDb(currentDb);
            if (currentDb <= FloorDb + 0.0001 && m_settings.DuckLevelPercent <= 0)
                Gain = 0f;

            // 4) State for the UI and tray icon.
            if (m_wantDuck)
                State = active ? DuckState.Ducking : DuckState.Holding;
            else
                State = IsAffectingOthers ? DuckState.Restoring : DuckState.Idle;

            return Gain;
        }

        private double DuckDb()
        {
            int pct = Math.Clamp(m_settings.DuckLevelPercent, 0, 100);
            if (pct <= 0) return FloorDb;
            return Math.Max(FloorDb, ToDb(pct / 100.0));
        }

        private static double ToDb(double gain) => gain <= 0 ? FloorDb : Math.Max(FloorDb, 20.0 * Math.Log10(gain));

        private static double FromDb(double db) => db <= FloorDb ? 0.0 : Math.Pow(10.0, db / 20.0);
    }
}

namespace Duckr
{
    /// <summary>
    /// Tiny log hook so the core classes don't depend on any UI logger
    /// (keeps them testable). DuckEngine points this at LoggingEngine.
    /// </summary>
    public static class DuckrLog
    {
        public static Action<string>? Sink { get; set; }

        public static void Write(string line) => Sink?.Invoke(line);
    }
}
