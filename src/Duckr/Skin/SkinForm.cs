/*
 *  Duckr - skin
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  The Duckr window, drawn by hand in the style of old-school media player
 *  skins: metal body, dot-matrix LCD with scrolling text, LED meters, metal
 *  sliders, LED buttons and a "playlist" of apps with live levels.
 *
 *  Layout is fixed in 96 dpi logical pixels and scaled to the screen DPI.
 */

using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Duckr.Skin
{
    public sealed class SkinForm : Form
    {
        // ---- logical layout (96 dpi pixels) -------------------------------------
        private const int W = 470, H = 440;

        private static readonly RectangleF TitleBar = new(0, 0, W, 20);
        private static readonly RectangleF BtnMin = new(W - 38, 5, 13, 11);
        private static readonly RectangleF BtnClose = new(W - 21, 5, 13, 11);

        private static readonly RectangleF Lcd = new(12, 30, 300, 84);
        private static readonly RectangleF MeterBox = new(322, 30, 136, 84);

        private const float SliderTop = 126, SliderRow = 22;
        private const float SliderLabelX = 16, GrooveX1 = 112, GrooveX2 = 378, ReadoutX = 388, ReadoutW = 70;

        private const float ButtonTop = 244, ButtonH = 28, ButtonW = 84, ButtonGap = 7.5f, ButtonX0 = 12;

        private static readonly RectangleF ListHeader = new(12, 284, 446, 16);
        private static readonly RectangleF AppList = new(12, 302, 446, 126);
        private const float ListRow = 15.5f;

        // ---- state ----------------------------------------------------------------
        private readonly DuckEngine m_engine;
        private readonly Func<bool> m_isOn;
        private readonly Action<bool> m_setOn;
        private readonly Action m_openSetup;
        private readonly Func<bool> m_isAutostart;
        private readonly Action<bool> m_setAutostart;

        private readonly System.Windows.Forms.Timer m_anim = new() { Interval = 33 };
        private readonly List<SliderDef> m_sliders = new();
        private readonly List<ButtonDef> m_buttons = new();

        private readonly DotMatrix m_bigDots = new(new Font("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Pixel));
        private readonly DotMatrix m_smallDots = new(new Font("Tahoma", 8f, FontStyle.Regular, GraphicsUnit.Pixel));
        private readonly Font m_labelFont = new("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font m_tinyFont = new("Tahoma", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font m_listFont = new("Tahoma", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font m_titleFont = new("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Pixel);

        private Bitmap? m_background;
        private float m_scale = 1f;

        private int m_marqueeOffset;
        private int m_frame;
        private float m_focusShown, m_focusPeakHold, m_gainShown = 1f;
        private int m_peakHoldFrames;

        private SliderDef? m_dragSlider;
        private ButtonDef? m_pressedButton;
        private object? m_hot;
        private int m_listScroll;
        private List<(string Name, float Peak)> m_rows = new();
        private int m_rowsRefreshFrame = -100;

        private sealed class SliderDef
        {
            public string Label = "";
            public int Min, Max, Step;
            public Func<DuckrSettings, int> Get = _ => 0;
            public Action<DuckrSettings, int> Set = (_, _) => { };
            public Func<int, string> Format = v => v.ToString();
            public int Index;
            public RectangleF Groove => new(GrooveX1, SliderTop + Index * SliderRow + 6, GrooveX2 - GrooveX1, 7);
            public RectangleF Row => new(GrooveX1 - 6, SliderTop + Index * SliderRow, GrooveX2 - GrooveX1 + 12, SliderRow - 2);
        }

        private sealed class ButtonDef
        {
            public string Text = "";
            public Func<string>? DynamicText;
            public Func<bool> Lit = () => false;
            public Color LedColor = SkinColors.LedGreen;
            public Action Click = () => { };
            public RectangleF Rect;
        }

        public SkinForm(DuckEngine engine, Func<bool> isOn, Action<bool> setOn, Action openSetup,
                        Func<bool> isAutostart, Action<bool> setAutostart)
        {
            m_engine = engine;
            m_isOn = isOn;
            m_setOn = setOn;
            m_openSetup = openSetup;
            m_isAutostart = isAutostart;
            m_setAutostart = setAutostart;

            Text = "Duckr";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            BackColor = SkinColors.MetalBottom;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            BuildSliders();
            BuildButtons();
            ApplyScale();

            m_anim.Tick += (_, _) => Animate();
            VisibleChanged += (_, _) => { if (Visible) m_anim.Start(); else m_anim.Stop(); };
            DpiChanged += (_, _) => ApplyScale();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        // ---- definitions ---------------------------------------------------------------

        private void BuildSliders()
        {
            void Add(string label, int min, int max, int step, Func<DuckrSettings, int> get,
                     Action<DuckrSettings, int> set, Func<int, string> fmt)
            {
                m_sliders.Add(new SliderDef
                {
                    Label = label, Min = min, Max = max, Step = step, Get = get, Set = set, Format = fmt,
                    Index = m_sliders.Count,
                });
            }

            Add("DUCK LEVEL", 0, 100, 1, s => s.DuckLevelPercent, (s, v) => s.DuckLevelPercent = v,
                v => v == 0 ? "MUTE" : $"{v}%");
            Add("FADE DOWN", 0, 2000, 10, s => s.DuckFadeMs, (s, v) => s.DuckFadeMs = v, Ms);
            Add("FADE UP", 0, 4000, 50, s => s.RestoreFadeMs, (s, v) => s.RestoreFadeMs = v, Ms);
            Add("HOLD", 0, 5000, 100, s => s.ReleaseMs, (s, v) => s.ReleaseMs = v, Ms);
            // left = less sensitive (needs louder sound), right = more sensitive
            Add("SENSITIVITY", 20, 80, 1, s => -s.ThresholdDb, (s, v) => s.ThresholdDb = -v, v => $"-{v}dB");
        }

        private static string Ms(int v) => v >= 1000 ? $"{v / 1000.0:0.0}s" : $"{v}ms";

        private void BuildButtons()
        {
            void Add(ButtonDef b)
            {
                int i = m_buttons.Count;
                b.Rect = new RectangleF(ButtonX0 + i * (ButtonW + ButtonGap), ButtonTop, ButtonW, ButtonH);
                m_buttons.Add(b);
            }

            Add(new ButtonDef
            {
                Text = "POWER", Lit = () => m_isOn(), LedColor = SkinColors.LedGreen,
                Click = () => m_setOn(!m_isOn()),
            });
            Add(new ButtonDef
            {
                Text = "FORCE", Lit = () => m_isOn() && m_engine.ForceDuck, LedColor = SkinColors.LedRed,
                Click = () => { if (m_isOn()) m_engine.ForceDuck = !m_engine.ForceDuck; },
            });
            Add(new ButtonDef
            {
                DynamicText = () => m_engine.Settings.Scope == DuckScope.AllOtherApps ? "ALL APPS" : "MUSIC ONLY",
                Lit = () => m_engine.Settings.Scope == DuckScope.MusicAppsOnly, LedColor = SkinColors.LedYellow,
                Click = () =>
                {
                    var s = m_engine.Settings;
                    s.Scope = s.Scope == DuckScope.AllOtherApps ? DuckScope.MusicAppsOnly : DuckScope.AllOtherApps;
                    m_engine.SaveSettingsSoon();
                },
            });
            Add(new ButtonDef { Text = "SETUP", Click = () => m_openSetup(), LedColor = SkinColors.LcdCyan });
            Add(new ButtonDef
            {
                Text = "STARTUP", Lit = () => m_isAutostart(), LedColor = SkinColors.LcdCyan,
                Click = () => m_setAutostart(!m_isAutostart()),
            });
        }

        // ---- scale / background cache ---------------------------------------------------

        private void ApplyScale()
        {
            m_scale = DeviceDpi / 96f;
            ClientSize = new Size((int)Math.Round(W * m_scale), (int)Math.Round(H * m_scale));
            m_background?.Dispose();
            m_background = null;
            Invalidate();
        }

        private Bitmap Background()
        {
            if (m_background != null) return m_background;
            var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            using var g = Graphics.FromImage(bmp);
            g.ScaleTransform(m_scale, m_scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var body = new RectangleF(0, 0, W, H);
            SkinPainter.Metal(g, body);
            using (var o = new Pen(SkinColors.Outline)) g.DrawRectangle(o, 0, 0, W - 1, H - 1);
            SkinPainter.Bevel(g, new RectangleF(1, 1, W - 2, H - 2), raised: true);

            // title bar
            using (var tb = new LinearGradientBrush(TitleBar, Color.FromArgb(40, 42, 64), Color.FromArgb(18, 18, 30), LinearGradientMode.Vertical))
                g.FillRectangle(tb, RectangleF.Inflate(TitleBar, -2, -2));
            const string title = "D U C K R   ·   FOR CREATORS";
            var tsize = g.MeasureString(title, m_titleFont);
            float tx = (W - tsize.Width) / 2f;
            SkinPainter.TitleRidges(g, 10, tx - 8, 10);
            SkinPainter.TitleRidges(g, tx + tsize.Width + 8, BtnMin.Left - 8, 10);
            SkinPainter.DrawLabel(g, title, m_titleFont, tx, 4);

            // wells
            DrawLcdWell(g, Lcd);
            SkinPainter.Well(g, MeterBox, Color.FromArgb(8, 8, 12));

            // slider labels + grooves + readout wells
            foreach (var s in m_sliders)
            {
                float y = SliderTop + s.Index * SliderRow;
                SkinPainter.DrawLabel(g, s.Label, m_labelFont, SliderLabelX, y + 3);
                var groove = s.Groove;
                SkinPainter.Well(g, groove, Color.FromArgb(10, 10, 16));
                // tick marks under the groove
                using var tick = new Pen(Color.FromArgb(90, SkinColors.Gold), 1f);
                for (int i = 0; i <= 10; i++)
                {
                    float x = groove.Left + groove.Width * i / 10f;
                    g.DrawLine(tick, x, groove.Bottom + 3, x, groove.Bottom + (i % 5 == 0 ? 6 : 4));
                }
                DrawLcdWell(g, new RectangleF(ReadoutX, y + 1, ReadoutW, SliderRow - 5));
            }

            // separator line between sliders and buttons
            using (var sepD = new Pen(SkinColors.BevelDark)) g.DrawLine(sepD, 10, ButtonTop - 9, W - 10, ButtonTop - 9);
            using (var sepL = new Pen(SkinColors.BevelLight)) g.DrawLine(sepL, 10, ButtonTop - 8, W - 10, ButtonTop - 8);

            // list header strip + list well
            using (var hb = new LinearGradientBrush(ListHeader, Color.FromArgb(52, 54, 78), Color.FromArgb(28, 28, 42), LinearGradientMode.Vertical))
                g.FillRectangle(hb, ListHeader);
            SkinPainter.Bevel(g, ListHeader, raised: true);
            SkinPainter.TitleRidges(g, ListHeader.Left + 6, ListHeader.Left + 120, ListHeader.Top + 7);
            SkinPainter.DrawLabel(g, "APPS  ·  LIVE", m_tinyFont, ListHeader.Left + 128, ListHeader.Top + 3);
            SkinPainter.TitleRidges(g, ListHeader.Left + 196, ListHeader.Right - 118, ListHeader.Top + 7);
            SkinPainter.DrawLabel(g, "M  music   N  never   F  focus", m_tinyFont, ListHeader.Right - 6, ListHeader.Top + 3,
                SkinColors.Gold, StringAlignment.Far);
            SkinPainter.Well(g, AppList, SkinColors.ListBack);

            m_background = bmp;
            return bmp;
        }

        private static void DrawLcdWell(Graphics g, RectangleF r)
        {
            SkinPainter.Well(g, r, SkinColors.LcdBack);
            // faint scanlines
            using var scan = new Pen(Color.FromArgb(14, 0, 255, 120), 1f);
            for (float y = r.Top + 1.5f; y < r.Bottom - 1; y += 2f)
                g.DrawLine(scan, r.Left + 1, y, r.Right - 1, y);
        }

        // ---- animation --------------------------------------------------------------------

        private void Animate()
        {
            m_frame++;
            if (m_frame % 4 == 0) m_marqueeOffset++;

            bool on = m_isOn();
            float focus = on ? PeakToMeter(m_engine.FocusedPeak) : 0f;
            // fast attack, slow fall, like a real VU
            m_focusShown = focus > m_focusShown ? focus : Math.Max(focus, m_focusShown - 0.035f);
            if (m_focusShown >= m_focusPeakHold) { m_focusPeakHold = m_focusShown; m_peakHoldFrames = 30; }
            else if (--m_peakHoldFrames <= 0) m_focusPeakHold = Math.Max(0, m_focusPeakHold - 0.02f);

            float gain = on ? m_engine.CurrentGain : 1f;
            m_gainShown += (gain - m_gainShown) * 0.35f;

            if (m_frame - m_rowsRefreshFrame >= 3)
            {
                m_rowsRefreshFrame = m_frame;
                m_rows = on ? m_engine.SnapshotLevels().Where(r => !string.IsNullOrEmpty(r.Name)).ToList() : new List<(string Name, float Peak)>();
                m_listScroll = Math.Clamp(m_listScroll, 0, Math.Max(0, m_rows.Count - VisibleRows));
            }
            Invalidate();
        }

        private static float PeakToMeter(float peak)
        {
            if (peak <= 0) return 0;
            double db = 20 * Math.Log10(peak);
            return (float)Math.Clamp((db + 60) / 60, 0, 1);
        }

        private int VisibleRows => (int)((AppList.Height - 4) / ListRow);

        // ---- painting ---------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(Background(), 0, 0);

            var state = g.Save();
            g.ScaleTransform(m_scale, m_scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawTitleButtons(g);
            DrawLcd(g);
            DrawMeters(g);
            DrawSliders(g);
            DrawButtons(g);
            DrawList(g);

            g.Restore(state);
        }

        private void DrawTitleButtons(Graphics g)
        {
            foreach (var (r, glyph) in new[] { (BtnMin, "_"), (BtnClose, "x") })
            {
                bool hot = Equals(m_hot, glyph);
                SkinPainter.DrawButton(g, r, pressed: false, hot: hot);
                using var b = new SolidBrush(hot ? Color.White : SkinColors.Gold);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(glyph, m_tinyFont, b, new RectangleF(r.X, r.Y - (glyph == "_" ? 3 : 1), r.Width, r.Height), sf);
            }
        }

        private void DrawLcd(Graphics g)
        {
            var r = Lcd;
            bool on = m_isOn();

            (string status, Color color) = !on ? ("POWER OFF", SkinColors.LcdGrey)
                : m_engine.ForceDuck ? ("FORCED", SkinColors.LcdRed)
                : m_engine.State switch
                {
                    DuckState.Ducking => ("DUCKING", SkinColors.LcdAmber),
                    DuckState.Holding => ("HOLD", SkinColors.LcdCyan),
                    DuckState.Restoring => ("RESTORE", SkinColors.LcdGreen),
                    _ => ("LISTENING", SkinColors.LcdGreen),
                };

            // blinking cursor-like block while ducking
            bool blink = on && (m_engine.State == DuckState.Ducking || m_engine.ForceDuck) && (m_frame / 12) % 2 == 0;
            if (blink)
            {
                using var bb = new SolidBrush(color);
                g.FillRectangle(bb, r.Left + 9, r.Top + 12, 5, 5);
            }

            // big status, dot pitch 3
            m_bigDots.Draw(g, status, r.Left + 20, r.Top + 8, 3f, color, maxCols: 80);

            // right side: what other apps are at
            string others = !on ? "--" : m_gainShown >= 0.995f ? "100%" : m_gainShown <= 0.005f ? "MUTE" : $"{m_gainShown * 100:0}%";
            m_smallDots.Draw(g, "OTHERS", r.Right - 70, r.Top + 9, 1.6f, SkinColors.LcdDim);
            m_smallDots.Draw(g, others, r.Right - 70, r.Top + 22, 2f, on ? SkinColors.LcdGreen : SkinColors.LcdGrey);

            // marquee
            string marquee;
            if (!on)
            {
                marquee = "PRESS POWER TO START  ***  OTHER APPS FADE DOWN WHILE YOUR WORK APP PLAYS  ***  ";
            }
            else
            {
                string focus = m_engine.FocusedApp.Length > 0 ? m_engine.FocusedApp : "(desktop)";
                string lvl = m_engine.FocusedPeak > 0 ? $"{20 * Math.Log10(m_engine.FocusedPeak):0} dB" : "silent";
                string hk = m_engine.Settings.ForceDuckHotkey;
                marquee = $"FOCUS: {focus}  ***  LEVEL {lvl}  ***  OTHERS AT {others}" +
                          (hk.Length > 0 ? $"  ***  {hk.ToUpperInvariant()} = FORCE" : "") + "  ***  ";
            }
            var marqueeColor = on ? SkinColors.LcdGreen : SkinColors.LcdGrey;
            if (on && m_engine.ConflictWarning.Length > 0)
            {
                marquee = $"WARNING: {m_engine.ConflictWarning}  ***  ";
                marqueeColor = SkinColors.LcdRed;
            }
            const float pitch = 2f;
            int cols = (int)((r.Width - 16) / pitch);
            m_smallDots.Draw(g, marquee, r.Left + 8, r.Top + 44, pitch, marqueeColor,
                maxCols: cols, offsetCols: m_marqueeOffset, wrap: true);

            // bottom: segmented bar of the other apps' volume
            SkinPainter.HorizontalMeter(g, new RectangleF(r.Left + 8, r.Bottom - 12, r.Width - 16, 5), 40,
                on ? m_gainShown : 0f, on ? SkinColors.LcdGreen : SkinColors.LcdGrey);
        }

        private void DrawMeters(Graphics g)
        {
            var r = MeterBox;
            float mw = 22, top = r.Top + 8, bottom = r.Bottom - 18;
            var focusRect = new RectangleF(r.Left + 16, top, mw, bottom - top);
            var othersRect = new RectangleF(r.Left + 56, top, mw, bottom - top);
            SkinPainter.VerticalMeter(g, focusRect, 16, m_focusShown, m_focusPeakHold);
            SkinPainter.VerticalMeter(g, othersRect, 16, m_isOn() ? m_gainShown : 0f, 0f);

            using var sf = new StringFormat { Alignment = StringAlignment.Center };
            using var lb = new SolidBrush(SkinColors.Gold);
            g.DrawString("FOCUS", m_tinyFont, lb, new RectangleF(focusRect.X - 10, r.Bottom - 15, mw + 20, 12), sf);
            g.DrawString("OTHERS", m_tinyFont, lb, new RectangleF(othersRect.X - 10, r.Bottom - 15, mw + 20, 12), sf);

            // dB scale on the right
            using var scale = new SolidBrush(Color.FromArgb(150, SkinColors.Gold));
            using var tick = new Pen(Color.FromArgb(110, SkinColors.Gold));
            foreach (var (db, label) in new[] { (0, "0"), (-12, "-12"), (-24, "-24"), (-36, "-36"), (-48, "-48"), (-60, "-60") })
            {
                float t = (db + 60) / 60f;
                float y = bottom - t * (bottom - top);
                g.DrawLine(tick, r.Left + 84, y, r.Left + 89, y);
                g.DrawString(label, m_tinyFont, scale, r.Left + 92, y - 5);
            }
        }

        private void DrawSliders(Graphics g)
        {
            var s = m_engine.Settings;
            foreach (var d in m_sliders)
            {
                int v = Math.Clamp(d.Get(s), d.Min, d.Max);
                float t = (v - d.Min) / (float)(d.Max - d.Min);
                var groove = d.Groove;

                // filled part of the groove
                var fill = new RectangleF(groove.Left + 1, groove.Top + 1, (groove.Width - 2) * t, groove.Height - 2);
                if (fill.Width > 0.5f)
                {
                    using var fb = new LinearGradientBrush(new RectangleF(groove.Left, groove.Top, groove.Width, groove.Height),
                        Color.FromArgb(0, 170, 70), Color.FromArgb(230, 200, 40), LinearGradientMode.Horizontal);
                    g.FillRectangle(fb, fill);
                    using var shine = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
                    g.FillRectangle(shine, fill.X, fill.Y, fill.Width, fill.Height / 2.5f);
                }

                // thumb
                float tx = groove.Left + groove.Width * t;
                var thumb = new RectangleF(tx - 7, groove.Top - 4, 14, groove.Height + 8);
                bool hot = ReferenceEquals(m_hot, d) || ReferenceEquals(m_dragSlider, d);
                SkinPainter.DrawButton(g, thumb, pressed: false, hot: hot);
                using (var grip = new Pen(Color.FromArgb(150, SkinColors.BevelDark)))
                    for (int i = -2; i <= 2; i += 2) g.DrawLine(grip, tx + i, thumb.Top + 4, tx + i, thumb.Bottom - 4);

                // readout
                float y = SliderTop + d.Index * SliderRow;
                m_smallDots.Draw(g, d.Format(v), ReadoutX + 6, y + 5, 1.5f, SkinColors.LcdGreen);
            }
        }

        private void DrawButtons(Graphics g)
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            foreach (var b in m_buttons)
            {
                bool pressed = ReferenceEquals(m_pressedButton, b) && ReferenceEquals(m_hot, b);
                SkinPainter.DrawButton(g, b.Rect, pressed, ReferenceEquals(m_hot, b));
                float o = pressed ? 1 : 0;
                SkinPainter.Led(g, b.Rect.Left + 11 + o, b.Rect.Top + b.Rect.Height / 2 + o, 3.2f, b.LedColor, b.Lit());
                var textRect = new RectangleF(b.Rect.Left + 18 + o, b.Rect.Top + o, b.Rect.Width - 22, b.Rect.Height);
                using var sh = new SolidBrush(SkinColors.LabelShadow);
                g.DrawString(b.DynamicText?.Invoke() ?? b.Text, m_labelFont, sh, new RectangleF(textRect.X + 1, textRect.Y + 1, textRect.Width, textRect.Height), sf);
                using var tb = new SolidBrush(b.Lit() ? Color.White : SkinColors.Gold);
                g.DrawString(b.DynamicText?.Invoke() ?? b.Text, m_labelFont, tb, textRect, sf);
            }
        }

        private void DrawList(Graphics g)
        {
            var r = RectangleF.Inflate(AppList, -2, -2);
            var clip = g.Clip;
            g.SetClip(r);

            if (m_rows.Count == 0)
            {
                using var dim = new SolidBrush(Color.FromArgb(0, 140, 0));
                g.DrawString(m_isOn() ? "No app is using audio right now." : "Power is off.", m_listFont, dim, r.Left + 6, r.Top + 4);
            }

            var s = m_engine.Settings;
            for (int i = 0; i < VisibleRows && i + m_listScroll < m_rows.Count; i++)
            {
                var (name, peak) = m_rows[i + m_listScroll];
                var row = new RectangleF(r.Left, r.Top + i * ListRow, r.Width, ListRow);
                bool focused = name.Equals(m_engine.FocusedApp, StringComparison.OrdinalIgnoreCase);
                bool lowered = m_isOn() && !focused && m_gainShown < 0.995f && WouldLower(s, name);

                if (focused)
                {
                    using var sel = new SolidBrush(SkinColors.ListSelBack);
                    g.FillRectangle(sel, row);
                }

                var textColor = focused ? SkinColors.ListSelText : lowered ? SkinColors.LcdAmber : SkinColors.ListText;
                using (var tb = new SolidBrush(textColor))
                {
                    g.DrawString($"{i + m_listScroll + 1}.", m_listFont, tb, row.Left + 4, row.Top + 1);
                    string label = name + (focused ? "   [focus]" : lowered ? "   [lowered]" : "");
                    g.DrawString(label, m_listFont, tb, new RectangleF(row.Left + 24, row.Top + 1, 210, ListRow));
                }

                // mini level meter
                SkinPainter.HorizontalMeter(g, new RectangleF(row.Left + 240, row.Top + 4.5f, 110, 6), 22, PeakToMeter(peak));

                // M / N / F tags
                DrawTag(g, TagRect(row, 0), "M", DuckrSettings.Matches(s.MusicApps, name), SkinColors.LedYellow, name);
                DrawTag(g, TagRect(row, 1), "N", DuckrSettings.Matches(s.NeverTouchApps, name), SkinColors.LedRed, name);
                DrawTag(g, TagRect(row, 2), "F", DuckrSettings.Matches(s.FocusOnlyApps, name), SkinColors.LcdCyan, name);
            }

            // scroll hint
            if (m_rows.Count > VisibleRows)
            {
                using var sb = new SolidBrush(Color.FromArgb(0, 160, 0));
                g.DrawString($"{m_listScroll + 1}-{Math.Min(m_rows.Count, m_listScroll + VisibleRows)} / {m_rows.Count}  (scroll)",
                    m_tinyFont, sb, r.Right - 4, r.Bottom - 11, new StringFormat { Alignment = StringAlignment.Far });
            }

            g.Clip = clip;
        }

        private bool WouldLower(DuckrSettings s, string name)
        {
            if (DuckrSettings.Matches(s.NeverTouchApps, name)) return false;
            return s.Scope == DuckScope.AllOtherApps || DuckrSettings.Matches(s.MusicApps, name);
        }

        private static RectangleF TagRect(RectangleF row, int i) => new(row.Left + 362 + i * 26, row.Top + 1.5f, 22, ListRow - 3);

        private void DrawTag(Graphics g, RectangleF r, string letter, bool on, Color color, string app)
        {
            bool hot = Equals(m_hot, (app, letter));
            using (var b = new SolidBrush(on ? Color.FromArgb(200, color) : Color.FromArgb(hot ? 60 : 28, color)))
                g.FillRectangle(b, r);
            using (var p = new Pen(on ? color : Color.FromArgb(hot ? 200 : 90, color))) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(on ? Color.Black : color);
            g.DrawString(letter, m_tinyFont, tb, r, sf);
        }

        // ---- input ---------------------------------------------------------------------------

        private PointF Logical(Point p) => new(p.X / m_scale, p.Y / m_scale);

        private object? HitTest(PointF p)
        {
            if (BtnMin.Contains(p)) return "_";
            if (BtnClose.Contains(p)) return "x";
            foreach (var b in m_buttons) if (b.Rect.Contains(p)) return b;
            foreach (var s in m_sliders) if (s.Row.Contains(p)) return s;

            var r = RectangleF.Inflate(AppList, -2, -2);
            if (r.Contains(p))
            {
                int i = (int)((p.Y - r.Top) / ListRow);
                int idx = i + m_listScroll;
                if (i < VisibleRows && idx < m_rows.Count)
                {
                    var row = new RectangleF(r.Left, r.Top + i * ListRow, r.Width, ListRow);
                    string[] letters = { "M", "N", "F" };
                    for (int t = 0; t < 3; t++)
                        if (TagRect(row, t).Contains(p)) return (m_rows[idx].Name, letters[t]);
                }
            }
            if (TitleBar.Contains(p)) return "title";
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = Logical(e.Location);
            if (m_dragSlider != null)
            {
                SetSliderFromX(m_dragSlider, p.X);
                return;
            }
            var hot = HitTest(p);
            if (!Equals(hot, m_hot))
            {
                m_hot = hot;
                Cursor = hot is SliderDef || hot is ButtonDef || hot is ValueTuple<string, string> || hot is "_" || hot is "x"
                    ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (m_dragSlider == null) { m_hot = null; Invalidate(); }
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var p = Logical(e.Location);
            var hit = HitTest(p);

            switch (hit)
            {
                case SliderDef s:
                    m_dragSlider = s;
                    Capture = true;
                    SetSliderFromX(s, p.X);
                    break;
                case ButtonDef b:
                    m_pressedButton = b;
                    Capture = true;
                    Invalidate();
                    break;
                case "title":
                    // drag the borderless window
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var p = Logical(e.Location);
            var hit = HitTest(p);

            if (m_dragSlider != null)
            {
                m_dragSlider = null;
                Capture = false;
                m_engine.SaveSettingsSoon();
                return;
            }
            if (m_pressedButton != null)
            {
                var b = m_pressedButton;
                m_pressedButton = null;
                Capture = false;
                if (ReferenceEquals(hit, b)) b.Click();
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            switch (hit)
            {
                case "_":
                    Hide();
                    break;
                case "x":
                    Hide();
                    break;
                case ValueTuple<string, string> tag:
                    ToggleTag(tag.Item1, tag.Item2);
                    break;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var p = Logical(e.Location);
            var hit = HitTest(p);
            if (hit is SliderDef s)
            {
                var st = m_engine.Settings;
                int v = Math.Clamp(s.Get(st) + Math.Sign(e.Delta) * s.Step, s.Min, s.Max);
                s.Set(st, v);
                m_engine.SaveSettingsSoon();
                Invalidate();
                return;
            }
            if (AppList.Contains(p))
            {
                m_listScroll = Math.Clamp(m_listScroll - Math.Sign(e.Delta), 0, Math.Max(0, m_rows.Count - VisibleRows));
                Invalidate();
            }
        }

        private void SetSliderFromX(SliderDef s, float x)
        {
            float t = Math.Clamp((x - GrooveX1) / (GrooveX2 - GrooveX1), 0, 1);
            int raw = s.Min + (int)Math.Round(t * (s.Max - s.Min));
            int v = Math.Clamp((int)Math.Round(raw / (double)s.Step) * s.Step, s.Min, s.Max);
            var st = m_engine.Settings;
            if (s.Get(st) != v)
            {
                s.Set(st, v);
                Invalidate();
            }
        }

        private void ToggleTag(string app, string letter)
        {
            var s = m_engine.Settings;
            var list = letter switch { "M" => s.MusicApps, "N" => s.NeverTouchApps, _ => s.FocusOnlyApps };
            if (DuckrSettings.Matches(list, app))
            {
                // remove exact entries and wildcard entries that match this app
                list.RemoveAll(entry =>
                {
                    var e = entry.Trim();
                    return e.EndsWith('*') ? app.StartsWith(e[..^1], StringComparison.OrdinalIgnoreCase)
                                           : app.Equals(e, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                list.Add(app);
            }
            m_engine.SaveSettingsSoon();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // closing the skin just hides it to the tray; the tray menu has Close
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_anim.Dispose();
                m_background?.Dispose();
                m_bigDots.Dispose();
                m_smallDots.Dispose();
                m_labelFont.Dispose();
                m_tinyFont.Dispose();
                m_listFont.Dispose();
                m_titleFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
