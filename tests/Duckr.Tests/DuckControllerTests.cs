using Duckr;
using Xunit;

namespace Duckr.Tests
{
    /// <summary>
    /// Tests for the Duckr duck logic. No audio hardware needed.
    /// </summary>
    public class DuckControllerTests
    {
        private const float Loud = 0.3f;

        private static DuckrSettings Cfg() => new()
        {
            DuckLevelPercent = 15, DuckFadeMs = 250, RestoreFadeMs = 900,
            AttackMs = 120, ReleaseMs = 1500, ThresholdDb = -50,
        };

        /// <summary>Feed one sample every 30 ms for <paramref name="ms"/> milliseconds.</summary>
        private static float Run(DuckController c, ref long t, long ms, float peak,
            bool focusOnly = false, bool ignored = false, bool force = false, string app = "Premiere", float voice = 0f)
        {
            float g = c.Gain;
            for (long end = t + ms; t < end; t += 30)
                g = c.Update(new DuckInput { NowMs = t, FocusedPeak = peak, FocusedIsFocusOnly = focusOnly, FocusedIsIgnored = ignored, ForceDuck = force, FocusKey = app, VoicePeak = voice });
            return g;
        }

        /// <summary>New controller whose focused app starts out silent (the normal case).</summary>
        private static DuckController Fresh(DuckrSettings cfg, out long t)
        {
            var c = new DuckController(cfg);
            t = 0;
            Run(c, ref t, 30, 0f);
            return c;
        }

        [Fact]
        public void Silence_keeps_other_apps_at_full_volume()
        {
            var c = Fresh(Cfg(), out long t);
            Assert.Equal(1f, Run(c, ref t, 2000, 0f));
            Assert.Equal(DuckState.Idle, c.State);
        }

        [Fact]
        public void Short_click_shorter_than_attack_does_not_duck()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 60, Loud);
            Assert.Equal(1f, Run(c, ref t, 1000, 0f));
        }

        [Fact]
        public void Playback_ducks_to_level_with_a_gradual_fade()
        {
            var c = Fresh(Cfg(), out long t);
            float mid = Run(c, ref t, 150, Loud);
            Assert.InRange(mid, 0.16f, 0.99f);
            Assert.Equal(0.15f, Run(c, ref t, 400, Loud), 3);
            Assert.Equal(DuckState.Ducking, c.State);
        }

        [Fact]
        public void Short_pause_holds_then_restores_after_release_delay()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 600, Loud);
            Assert.Equal(0.15f, Run(c, ref t, 1000, 0f), 3);
            Assert.Equal(DuckState.Holding, c.State);

            Run(c, ref t, 600, 0f);
            Assert.Equal(DuckState.Restoring, c.State);
            Assert.Equal(1f, Run(c, ref t, 1000, 0f));
            Assert.Equal(DuckState.Idle, c.State);
        }

        [Fact]
        public void Replaying_during_hold_reducks_without_attack_wait()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 600, Loud);
            Run(c, ref t, 900, 0f);
            Run(c, ref t, 30, Loud);
            Assert.Equal(DuckState.Ducking, c.State);
        }

        [Fact]
        public void Focus_only_app_ducks_without_sound()
        {
            var c = Fresh(Cfg(), out long t);
            Assert.Equal(0.15f, Run(c, ref t, 400, 0f, focusOnly: true), 3);
        }

        [Fact]
        public void Never_touch_app_does_not_trigger()
        {
            var c = Fresh(Cfg(), out long t);
            Assert.Equal(1f, Run(c, ref t, 1000, Loud, ignored: true));
        }

        [Fact]
        public void Hotkey_force_duck_works_in_silence_and_releases()
        {
            var c = Fresh(Cfg(), out long t);
            Assert.Equal(0.15f, Run(c, ref t, 400, 0f, force: true), 3);
            Assert.Equal(1f, Run(c, ref t, 3000, 0f));
        }

        [Fact]
        public void Zero_percent_means_mute()
        {
            var c = Fresh(new DuckrSettings { DuckLevelPercent = 0, AttackMs = 120, DuckFadeMs = 250 }, out long t);
            Assert.Equal(0f, Run(c, ref t, 600, Loud));
        }

        [Fact]
        public void Noise_below_threshold_is_ignored()
        {
            var c = Fresh(Cfg(), out long t);
            Assert.Equal(1f, Run(c, ref t, 1000, 0.002f)); // about -54 dB
        }

        [Fact]
        public void Focusing_an_app_that_is_already_playing_does_not_duck()
        {
            // music plays in Spotify, you click Spotify
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 300, 0f, app: "Explorer");
            Assert.Equal(1f, Run(c, ref t, 2000, Loud, app: "Spotify"));
        }

        [Fact]
        public void Already_playing_app_counts_again_after_it_stops_and_restarts()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 500, Loud, app: "chrome");     // was playing when focused
            Run(c, ref t, 400, 0f, app: "chrome");       // you pause it
            Assert.Equal(0.15f, Run(c, ref t, 600, Loud, app: "chrome"), 3); // you play your reference video
        }

        [Fact]
        public void Switching_focus_away_restores_without_waiting_the_hold()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 600, Loud, app: "Premiere");
            Assert.Equal(DuckState.Ducking, c.State);
            Run(c, ref t, 30, 0f, app: "Explorer");
            Assert.Equal(DuckState.Restoring, c.State);
            Assert.Equal(1f, Run(c, ref t, 1000, 0f, app: "Explorer"));
        }

        [Fact]
        public void Focus_switch_does_not_cancel_force()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 400, 0f, force: true, app: "Premiere");
            Assert.Equal(0.15f, Run(c, ref t, 400, 0f, force: true, app: "Explorer"), 3);
        }

        [Fact]
        public void Voice_app_talking_in_background_ducks()
        {
            // you're in Premiere, silent; a friend talks on Discord
            var c = Fresh(Cfg(), out long t);
            Assert.Equal(0.15f, Run(c, ref t, 600, 0f, voice: Loud), 3);
            Assert.True(c.ByVoice);
        }

        [Fact]
        public void Voice_ping_shorter_than_attack_does_not_duck()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 60, 0f, voice: Loud);
            Assert.Equal(1f, Run(c, ref t, 1000, 0f));
        }

        [Fact]
        public void Voice_holds_between_words_then_restores()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 600, 0f, voice: Loud);
            Assert.Equal(0.15f, Run(c, ref t, 800, 0f), 3);        // pause between sentences
            Run(c, ref t, 1000, 0f);
            Assert.Equal(1f, Run(c, ref t, 1000, 0f));             // they stopped talking
        }

        [Fact]
        public void Voice_keeps_ducking_when_focus_changes()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 600, 0f, voice: Loud, app: "Premiere");
            Assert.Equal(0.15f, Run(c, ref t, 300, 0f, voice: Loud, app: "Explorer"), 3);
        }

        [Fact]
        public void Work_app_playing_is_not_reported_as_voice()
        {
            var c = Fresh(Cfg(), out long t);
            Run(c, ref t, 600, Loud, voice: Loud);
            Assert.False(c.ByVoice);
        }

        [Theory]
        [InlineData("Ableton Live*", "Ableton Live 12 Suite", true)]
        [InlineData("spotify", "Spotify", true)]
        [InlineData("FL64", "FL64Helper", false)]
        [InlineData("", "anything", false)]
        public void Name_matching(string entry, string process, bool expected)
        {
            Assert.Equal(expected, DuckrSettings.Matches(new[] { entry }, process));
        }

        [Fact]
        public void Settings_round_trip_and_clamp()
        {
            string p = Path.Combine(Path.GetTempPath(), $"cm-{Guid.NewGuid():N}.json");
            try
            {
                var s = new DuckrSettings { DuckLevelPercent = 30, Scope = DuckScope.MusicAppsOnly };
                s.FocusOnlyApps.Add("reaper");
                s.Save(p);
                var l = DuckrSettings.Load(p);
                Assert.Equal(30, l.DuckLevelPercent);
                Assert.Equal(DuckScope.MusicAppsOnly, l.Scope);
                Assert.Contains("reaper", l.FocusOnlyApps);

                File.WriteAllText(p, "{\"DuckLevelPercent\": 500, \"ReleaseMs\": -4}");
                l = DuckrSettings.Load(p);
                Assert.Equal(100, l.DuckLevelPercent);
                Assert.Equal(0, l.ReleaseMs);

                File.WriteAllText(p, "not json");
                Assert.Equal(15, DuckrSettings.Load(p).DuckLevelPercent);
            }
            finally
            {
                File.Delete(p);
            }
        }
    }
}
