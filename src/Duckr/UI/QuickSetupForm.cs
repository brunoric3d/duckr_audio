/*
 *  Duckr - quick setup, shown the first time Duckr runs.
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  One page in the skin: a welcome screen and four rows of LED choices.
 *  START DUCKR saves them. Closing keeps whatever is selected.
 */

using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using Duckr.Skin;

namespace Duckr
{
    public sealed class QuickSetupForm : Form
    {
        private const int W = 460, H = 430;

        private static readonly RectangleF TitleBar = new(0, 0, W, 20);
        private static readonly RectangleF BtnClose = new(W - 21, 5, 13, 11);
        private static readonly RectangleF Lcd = new(12, 30, W - 24, 76);
        private static readonly RectangleF BtnStart = new((W - 180) / 2f, 384, 180, 32);

        private const float RowTop = 122, RowH = 62, ChoiceX0 = 16, ChoiceH = 26;

        private sealed record Choice(string Label, string Hint);

        private sealed class Row
        {
            public string Title = "";
            public Choice[] Choices = Array.Empty<Choice>();
            public int Selected;
            public Color Led;
        }

        private readonly DuckEngine m_engine;
        private readonly Row[] m_rows;
        private readonly DotMatrix m_bigDots = new(new Font("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Pixel));
        private readonly DotMatrix m_smallDots = new(new Font("Tahoma", 8f, FontStyle.Regular, GraphicsUnit.Pixel));
        private readonly Font m_labelFont = new("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font m_tinyFont = new("Tahoma", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font m_hintFont = new("Tahoma", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font m_bigFont = new("Tahoma", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Bitmap? m_duck;
        private readonly System.Windows.Forms.Timer m_anim = new() { Interval = 60 };

        private Bitmap? m_background;
        private float m_scale = 1f;
        private int m_frame;
        private object? m_hot;
        private object? m_pressed;
        private bool m_applied;

        private sealed record ChoiceHit(int Row, int Index);

        /// <summary>Raised after the choices are saved (START DUCKR or close).</summary>
        public event EventHandler? Finished;

        public QuickSetupForm(DuckEngine engine)
        {
            m_engine = engine;
            var s = engine.Settings;

            m_rows = new[]
            {
                new Row
                {
                    Title = "WHEN YOUR WORK APP PLAYS, LOWER", Led = SkinColors.LedGreen,
                    Choices = new[]
                    {
                        new Choice("ALL OTHER APPS", "Everything else steps back: music, videos, games."),
                        new Choice("MUSIC APPS ONLY", "Only Spotify, browsers and other music apps step back."),
                    },
                    Selected = s.Scope == DuckScope.MusicAppsOnly ? 1 : 0,
                },
                new Row
                {
                    Title = "HOW LOW", Led = SkinColors.LedYellow,
                    Choices = new[]
                    {
                        new Choice("MUTE", "Other apps go silent while you play."),
                        new Choice("LOW  15%", "Music stays faintly in the background."),
                        new Choice("SOFT  35%", "Music stays easy to hear, just quieter."),
                    },
                    Selected = s.DuckLevelPercent <= 5 ? 0 : s.DuckLevelPercent >= 25 ? 2 : 1,
                },
                new Row
                {
                    Title = "VOICE CALLS LOWER THE MUSIC", Led = SkinColors.LcdCyan,
                    Choices = new[]
                    {
                        new Choice("OFF", "Calls don't change your music. Turn on later with VOICE CALL."),
                        new Choice("ON", "When a friend talks on Discord, Teams, Zoom or Slack, music steps back."),
                    },
                    Selected = s.VoiceCallEnabled ? 1 : 0,
                },
                new Row
                {
                    Title = "START WITH WINDOWS", Led = SkinColors.LedRed,
                    Choices = new[]
                    {
                        new Choice("NO", "Open Duckr yourself when you need it."),
                        new Choice("YES", "Duckr waits quietly in the tray after you log in."),
                    },
                    Selected = Autostart.IsEnabled ? 1 : 0,
                },
            };

            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("duckr_on.ico");
                if (stream != null)
                {
                    using var icon = new Icon(stream, new Size(256, 256));
                    m_duck = icon.ToBitmap();
                }
            }
            catch { /* the welcome screen works without the picture */ }

            Text = "Welcome to Duckr";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = false;
            BackColor = SkinColors.MetalBottom;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            ApplyScale();
            m_anim.Tick += (_, _) => { m_frame++; Invalidate(); };
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

        // ---- apply ----------------------------------------------------------------------

        private void Apply()
        {
            if (m_applied) return;
            m_applied = true;

            var s = m_engine.Settings;
            s.Scope = m_rows[0].Selected == 1 ? DuckScope.MusicAppsOnly : DuckScope.AllOtherApps;
            s.DuckLevelPercent = m_rows[1].Selected switch { 0 => 0, 2 => 35, _ => 15 };
            s.VoiceCallEnabled = m_rows[2].Selected == 1;
            s.FirstRunDone = true;
            try { s.Save(); }
            catch (Exception ex) { DuckrLog.Write($"[!] Could not save quick setup: {ex.Message}"); }

            bool autostart = m_rows[3].Selected == 1;
            if (autostart != Autostart.IsEnabled) Autostart.Set(autostart);

            DuckrLog.Write($"[+] Quick setup: scope={s.Scope} level={s.DuckLevelPercent}% voice={s.VoiceCallEnabled} autostart={autostart}");
            Finished?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Apply(); // closing keeps the selected choices
            base.OnFormClosing(e);
        }

        // ---- layout helpers -------------------------------------------------------------

        private static RectangleF RowRect(int r) => new(12, RowTop + r * RowH, W - 24, RowH - 6);

        private RectangleF ChoiceRect(int r, int i)
        {
            int n = m_rows[r].Choices.Length;
            float gap = 8, width = (W - 32 - gap * (n - 1)) / n;
            return new RectangleF(ChoiceX0 + i * (width + gap), RowRect(r).Top + 15, width, ChoiceH);
        }

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

            SkinPainter.Metal(g, new RectangleF(0, 0, W, H), seed: 23);
            using (var o = new Pen(SkinColors.Outline)) g.DrawRectangle(o, 0, 0, W - 1, H - 1);
            SkinPainter.Bevel(g, new RectangleF(1, 1, W - 2, H - 2), raised: true);

            using (var tb = new LinearGradientBrush(TitleBar, Color.FromArgb(40, 42, 64), Color.FromArgb(18, 18, 30), LinearGradientMode.Vertical))
                g.FillRectangle(tb, RectangleF.Inflate(TitleBar, -2, -2));
            const string title = "D U C K R   ·   QUICK SETUP";
            var ts = g.MeasureString(title, m_labelFont);
            float tx = (W - ts.Width) / 2f;
            SkinPainter.TitleRidges(g, 10, tx - 8, 10);
            SkinPainter.TitleRidges(g, tx + ts.Width + 8, BtnClose.Left - 8, 10);
            SkinPainter.DrawLabel(g, title, m_labelFont, tx, 4);

            SkinPainter.Well(g, Lcd, SkinColors.LcdBack);
            using (var scan = new Pen(Color.FromArgb(14, 0, 255, 120), 1f))
                for (float y = Lcd.Top + 1.5f; y < Lcd.Bottom - 1; y += 2f)
                    g.DrawLine(scan, Lcd.Left + 1, y, Lcd.Right - 1, y);
            if (m_duck != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(m_duck, new RectangleF(Lcd.Left + 6, Lcd.Top + 6, Lcd.Height - 12, Lcd.Height - 12));
            }

            for (int r = 0; r < m_rows.Length; r++)
            {
                var rr = RowRect(r);
                SkinPainter.Led(g, rr.Left + 8, rr.Top + 6, 3f, m_rows[r].Led, true);
                SkinPainter.DrawLabel(g, m_rows[r].Title, m_labelFont, rr.Left + 16, rr.Top);
            }

            using (var sepD = new Pen(SkinColors.BevelDark)) g.DrawLine(sepD, 10, BtnStart.Top - 10, W - 10, BtnStart.Top - 10);
            using (var sepL = new Pen(SkinColors.BevelLight)) g.DrawLine(sepL, 10, BtnStart.Top - 9, W - 10, BtnStart.Top - 9);

            m_background = bmp;
            return bmp;
        }

        // ---- painting ----------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(Background(), 0, 0);
            var state = g.Save();
            g.ScaleTransform(m_scale, m_scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // close
            bool hotX = Equals(m_hot, "close");
            SkinPainter.DrawButton(g, BtnClose, pressed: false, hot: hotX);
            using (var xb = new SolidBrush(hotX ? Color.White : SkinColors.Gold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("x", m_tinyFont, xb, new RectangleF(BtnClose.X, BtnClose.Y - 1, BtnClose.Width, BtnClose.Height), sf);

            // welcome screen
            float textX = Lcd.Left + Lcd.Height + 4;
            m_bigDots.Draw(g, "WELCOME TO DUCKR", textX, Lcd.Top + 10, 2.4f, SkinColors.LcdGreen);
            m_smallDots.Draw(g, "YOUR MUSIC STEPS BACK", textX, Lcd.Top + 40, 1.5f, SkinColors.LcdAmber);
            m_smallDots.Draw(g, "WHEN YOUR WORK PLAYS", textX, Lcd.Top + 55, 1.5f, SkinColors.LcdAmber);
            if ((m_frame / 8) % 2 == 0)
            {
                using var cur = new SolidBrush(SkinColors.LcdGreen);
                g.FillRectangle(cur, Lcd.Right - 16, Lcd.Bottom - 14, 6, 6);
            }

            // choice rows
            using var csf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (int r = 0; r < m_rows.Length; r++)
            {
                var row = m_rows[r];
                for (int i = 0; i < row.Choices.Length; i++)
                {
                    var cr = ChoiceRect(r, i);
                    bool sel = row.Selected == i;
                    bool hot = m_hot is ChoiceHit h && h.Row == r && h.Index == i;
                    SkinPainter.DrawButton(g, cr, pressed: sel, hot: hot && !sel);
                    SkinPainter.Led(g, cr.Left + 11, cr.Top + cr.Height / 2, 3.2f, row.Led, sel);
                    using var tb = new SolidBrush(sel ? Color.White : SkinColors.Gold);
                    g.DrawString(row.Choices[i].Label, m_labelFont, tb, new RectangleF(cr.Left + 18, cr.Top, cr.Width - 22, cr.Height), csf);
                }
                var rr = RowRect(r);
                SkinPainter.DrawLabel(g, row.Choices[row.Selected].Hint, m_hintFont, rr.Left + 4, rr.Top + 15 + ChoiceH + 3,
                    Color.FromArgb(214, 204, 170));
            }

            // start button, a bit bigger and glowing
            bool hotStart = Equals(m_hot, "start");
            bool pressedStart = Equals(m_pressed, "start") && hotStart;
            if ((m_frame / 10) % 2 == 0 || hotStart)
            {
                using var glow = new SolidBrush(Color.FromArgb(50, SkinColors.LedGreen));
                g.FillRectangle(glow, RectangleF.Inflate(BtnStart, 4, 4));
            }
            SkinPainter.DrawButton(g, BtnStart, pressedStart, hotStart);
            SkinPainter.Led(g, BtnStart.Left + 16, BtnStart.Top + BtnStart.Height / 2, 4f, SkinColors.LedGreen, true);
            using (var sb = new SolidBrush(SkinColors.LabelShadow))
                g.DrawString("START DUCKR", m_bigFont, sb, new RectangleF(BtnStart.X + 13, BtnStart.Y + 1, BtnStart.Width - 10, BtnStart.Height), csf);
            using (var sb2 = new SolidBrush(hotStart ? Color.White : SkinColors.Gold))
                g.DrawString("START DUCKR", m_bigFont, sb2, new RectangleF(BtnStart.X + 12, BtnStart.Y, BtnStart.Width - 10, BtnStart.Height), csf);

            g.Restore(state);
        }

        // ---- input -------------------------------------------------------------------------

        private PointF Logical(Point p) => new(p.X / m_scale, p.Y / m_scale);

        private object? HitTest(PointF p)
        {
            if (BtnClose.Contains(p)) return "close";
            if (BtnStart.Contains(p)) return "start";
            for (int r = 0; r < m_rows.Length; r++)
                for (int i = 0; i < m_rows[r].Choices.Length; i++)
                    if (ChoiceRect(r, i).Contains(p)) return new ChoiceHit(r, i);
            if (TitleBar.Contains(p)) return "title";
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hot = HitTest(Logical(e.Location));
            if (!Equals(hot, m_hot))
            {
                m_hot = hot;
                Cursor = hot is null or "title" ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            m_hot = null;
            Invalidate();
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var hit = HitTest(Logical(e.Location));
            if (Equals(hit, "title"))
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
                return;
            }
            m_pressed = hit;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var hit = HitTest(Logical(e.Location));
            bool same = Equals(hit, m_pressed);
            m_pressed = null;
            if (!same) { Invalidate(); return; }

            switch (hit)
            {
                case ChoiceHit c:
                    m_rows[c.Row].Selected = c.Index;
                    Invalidate();
                    break;
                case "start":
                case "close":
                    Close(); // OnFormClosing saves
                    break;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Escape) Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_anim.Dispose();
                m_background?.Dispose();
                m_duck?.Dispose();
                m_bigDots.Dispose();
                m_smallDots.Dispose();
                m_labelFont.Dispose();
                m_tinyFont.Dispose();
                m_hintFont.Dispose();
                m_bigFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
