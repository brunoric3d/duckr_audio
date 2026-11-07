/*
 *  Duckr - setup window.
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Drawn by hand in the same skin as the main window. Edits the three app
 *  lists (click a row to type, with live search of running apps), the force
 *  hotkey (recorded by pressing it) and how long sound must play before
 *  ducking. Every change applies at once and is saved shortly after.
 */

using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Duckr.Skin;

namespace Duckr
{
    public sealed class SetupForm : Form
    {
        // ---- logical layout (96 dpi pixels) -------------------------------------
        private const int W = 460, H = 430;

        private static readonly RectangleF TitleBar = new(0, 0, W, 20);
        private static readonly RectangleF BtnClose = new(W - 21, 5, 13, 11);

        private const float TabTop = 30, TabH = 24, TabW = 140, TabGap = 8, TabX0 = 12;
        private static readonly RectangleF HintLine = new(14, 59, W - 28, 14);

        private static readonly RectangleF ListWell = new(12, 78, 436, 186);
        private const float RowH = 18;

        private static readonly RectangleF HelpLine = new(14, 268, W - 28, 12);

        private const float SectionTop = 292;
        private static readonly RectangleF HotkeyBox = new(150, SectionTop, 150, 20);
        private static readonly RectangleF HotkeyClear = new(306, SectionTop + 2, 16, 16);

        private const float AttackTop = SectionTop + 32;
        private const float GrooveX1 = 150, GrooveX2 = 360, ReadoutX = 372, ReadoutW = 76;

        private static readonly RectangleF BtnReset = new(12, 384, 120, 28);
        private static readonly RectangleF BtnDone = new(W - 132, 384, 120, 28);

        private enum ListKind { Music, Never, FocusOnly }

        private static readonly (ListKind Kind, string Title, string Hint, Color Led)[] Tabs =
        {
            (ListKind.Music, "MUSIC APPS", "Apps that play your music. These are what get lowered in MUSIC ONLY mode.", SkinColors.LedYellow),
            (ListKind.Never, "NEVER TOUCH", "Never lowered and never trigger lowering: calls, streaming, OBS.", SkinColors.LedRed),
            (ListKind.FocusOnly, "WHILE FOCUSED", "For DAWs on ASIO: other apps are lowered the whole time these are focused.", SkinColors.LcdCyan),
        };

        // ---- state ----------------------------------------------------------------
        private readonly DuckEngine m_engine;
        private readonly System.Windows.Forms.Timer m_anim = new() { Interval = 50 };
        private readonly TextBox m_edit;

        private readonly DotMatrix m_dots = new(new Font("Tahoma", 8f, FontStyle.Regular, GraphicsUnit.Pixel));
        private readonly Font m_labelFont = new("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font m_tinyFont = new("Tahoma", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font m_listFont = new("Tahoma", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font m_hintFont = new("Tahoma", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
        private Font? m_editFont;

        private Bitmap? m_background;
        private float m_scale = 1f;
        private int m_frame;

        private ListKind m_tab = ListKind.Music;
        private int m_scroll;
        private object? m_hot;
        private bool m_draggingAttack;
        private bool m_capturingHotkey;

        private int m_editIndex = -1;            // row being edited; == list count means "new"
        private readonly List<RunningApp> m_suggestions = new();
        private int m_suggestSel = -1;

        private Dictionary<string, AppStatus> m_running = new(StringComparer.OrdinalIgnoreCase);
        private long m_runningAtMs = -100_000;
        private readonly Stopwatch m_clock = Stopwatch.StartNew();

        private enum AppStatus { Running, Audio, Playing }

        private sealed record RunningApp(string Name, AppStatus Status);

        private sealed record RowHit(int Index, string Part); // Part: "name", "x", "add"
        private sealed record SuggestHit(int Index);
        private sealed record TabHit(ListKind Kind);

        public SetupForm(DuckEngine engine)
        {
            m_engine = engine;

            Text = "Duckr setup";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            BackColor = SkinColors.MetalBottom;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            m_edit = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(0, 0, 90),
                ForeColor = Color.White,
                Visible = false,
            };
            m_edit.TextChanged += (_, _) => UpdateSuggestions();
            m_edit.KeyDown += EditKeyDown;
            Controls.Add(m_edit);

            ApplyScale();
            RefreshRunning(force: true);

            m_anim.Tick += (_, _) =>
            {
                m_frame++;
                if (m_clock.ElapsedMilliseconds - m_runningAtMs > 2000) RefreshRunning();
                Invalidate();
            };
            VisibleChanged += (_, _) => { if (Visible) m_anim.Start(); else m_anim.Stop(); };
            DpiChanged += (_, _) => ApplyScale();
            Deactivate += (_, _) => { if (m_editIndex >= 0) CommitEdit(m_edit.Text); };
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

        // ---- data helpers ---------------------------------------------------------------

        private List<string> CurrentList()
        {
            var s = m_engine.Settings;
            return m_tab switch
            {
                ListKind.Music => s.MusicApps,
                ListKind.Never => s.NeverTouchApps,
                _ => s.FocusOnlyApps,
            };
        }

        private static List<string> DefaultList(ListKind kind)
        {
            var d = new DuckrSettings();
            return kind switch
            {
                ListKind.Music => d.MusicApps,
                ListKind.Never => d.NeverTouchApps,
                _ => d.FocusOnlyApps,
            };
        }

        /// <summary>Which apps are running, using audio, or playing right now.</summary>
        private void RefreshRunning(bool force = false)
        {
            m_runningAtMs = m_clock.ElapsedMilliseconds;
            var map = new Dictionary<string, AppStatus>(StringComparer.OrdinalIgnoreCase);

            // apps with a window
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId)
                        map[p.ProcessName] = AppStatus.Running;
                }
                catch { /* access denied for some system processes */ }
                finally { p.Dispose(); }
            }

            // apps with an audio session (works only while Duckr is on)
            foreach (var (name, peak) in m_engine.SnapshotLevels())
            {
                if (string.IsNullOrEmpty(name)) continue;
                map[name] = peak > 0.001f ? AppStatus.Playing : AppStatus.Audio;
            }

            m_running = map;
            if (m_editIndex >= 0 && !force) UpdateSuggestions();
        }

        private AppStatus? StatusOf(string entry)
        {
            AppStatus? best = null;
            foreach (var (name, st) in m_running)
            {
                if (!DuckrSettings.Matches(new[] { entry }, name)) continue;
                if (best == null || st > best) best = st;
            }
            return best;
        }

        // ---- scale / background ---------------------------------------------------------

        private void ApplyScale()
        {
            m_scale = DeviceDpi / 96f;
            ClientSize = new Size((int)Math.Round(W * m_scale), (int)Math.Round(H * m_scale));
            m_background?.Dispose();
            m_background = null;
            m_editFont?.Dispose();
            m_editFont = new Font("Tahoma", 11f * m_scale, FontStyle.Regular, GraphicsUnit.Pixel);
            m_edit.Font = m_editFont;
            if (m_editIndex >= 0) PlaceEditBox();
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

            SkinPainter.Metal(g, new RectangleF(0, 0, W, H), seed: 11);
            using (var o = new Pen(SkinColors.Outline)) g.DrawRectangle(o, 0, 0, W - 1, H - 1);
            SkinPainter.Bevel(g, new RectangleF(1, 1, W - 2, H - 2), raised: true);

            using (var tb = new LinearGradientBrush(TitleBar, Color.FromArgb(40, 42, 64), Color.FromArgb(18, 18, 30), LinearGradientMode.Vertical))
                g.FillRectangle(tb, RectangleF.Inflate(TitleBar, -2, -2));
            const string title = "D U C K R   ·   SETUP";
            var ts = g.MeasureString(title, m_labelFont);
            float tx = (W - ts.Width) / 2f;
            SkinPainter.TitleRidges(g, 10, tx - 8, 10);
            SkinPainter.TitleRidges(g, tx + ts.Width + 8, BtnClose.Left - 8, 10);
            SkinPainter.DrawLabel(g, title, m_labelFont, tx, 4);

            SkinPainter.Well(g, ListWell, SkinColors.ListBack);

            // hotkey + attack labels and wells
            SkinPainter.DrawLabel(g, "FORCE HOTKEY", m_labelFont, 16, SectionTop + 4);
            DrawLcdWell(g, HotkeyBox);
            SkinPainter.DrawLabel(g, "MUST PLAY FOR", m_labelFont, 16, AttackTop + 3);
            var groove = AttackGroove;
            SkinPainter.Well(g, groove, Color.FromArgb(10, 10, 16));
            using (var tick = new Pen(Color.FromArgb(90, SkinColors.Gold), 1f))
                for (int i = 0; i <= 10; i++)
                {
                    float x = groove.Left + groove.Width * i / 10f;
                    g.DrawLine(tick, x, groove.Bottom + 3, x, groove.Bottom + (i % 5 == 0 ? 6 : 4));
                }
            DrawLcdWell(g, new RectangleF(ReadoutX, AttackTop + 1, ReadoutW, 17));

            using (var sepD = new Pen(SkinColors.BevelDark)) g.DrawLine(sepD, 10, SectionTop - 8, W - 10, SectionTop - 8);
            using (var sepL = new Pen(SkinColors.BevelLight)) g.DrawLine(sepL, 10, SectionTop - 7, W - 10, SectionTop - 7);
            using (var sepD2 = new Pen(SkinColors.BevelDark)) g.DrawLine(sepD2, 10, BtnReset.Top - 10, W - 10, BtnReset.Top - 10);
            using (var sepL2 = new Pen(SkinColors.BevelLight)) g.DrawLine(sepL2, 10, BtnReset.Top - 9, W - 10, BtnReset.Top - 9);

            m_background = bmp;
            return bmp;
        }

        private static void DrawLcdWell(Graphics g, RectangleF r)
        {
            SkinPainter.Well(g, r, SkinColors.LcdBack);
            using var scan = new Pen(Color.FromArgb(14, 0, 255, 120), 1f);
            for (float y = r.Top + 1.5f; y < r.Bottom - 1; y += 2f)
                g.DrawLine(scan, r.Left + 1, y, r.Right - 1, y);
        }

        private static RectangleF AttackGroove => new(GrooveX1, AttackTop + 6, GrooveX2 - GrooveX1, 7);
        private static RectangleF AttackRow => new(GrooveX1 - 6, AttackTop, GrooveX2 - GrooveX1 + 12, 20);

        private static RectangleF TabRect(int i) => new(TabX0 + i * (TabW + TabGap), TabTop, TabW, TabH);

        private int VisibleRows => (int)((ListWell.Height - 4) / RowH);

        private RectangleF RowRect(int visibleIndex) =>
            new(ListWell.Left + 2, ListWell.Top + 2 + visibleIndex * RowH, ListWell.Width - 4, RowH);

        private static RectangleF RemoveRect(RectangleF row) => new(row.Right - 22, row.Top + 2, 16, RowH - 4);

        // ---- painting -----------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(Background(), 0, 0);

            var state = g.Save();
            g.ScaleTransform(m_scale, m_scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawClose(g);
            DrawTabs(g);
            DrawList(g);
            DrawHotkey(g);
            DrawAttack(g);
            DrawBottomButtons(g);
            DrawSuggestions(g); // on top of everything

            g.Restore(state);
        }

        private void DrawClose(Graphics g)
        {
            bool hot = Equals(m_hot, "close");
            SkinPainter.DrawButton(g, BtnClose, pressed: false, hot: hot);
            using var b = new SolidBrush(hot ? Color.White : SkinColors.Gold);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("x", m_tinyFont, b, new RectangleF(BtnClose.X, BtnClose.Y - 1, BtnClose.Width, BtnClose.Height), sf);
        }

        private void DrawTabs(Graphics g)
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (int i = 0; i < Tabs.Length; i++)
            {
                var (kind, title, _, led) = Tabs[i];
                var r = TabRect(i);
                bool active = kind == m_tab;
                bool hot = m_hot is TabHit th && th.Kind == kind;
                SkinPainter.DrawButton(g, r, pressed: active, hot: hot && !active);
                SkinPainter.Led(g, r.Left + 11, r.Top + r.Height / 2, 3.2f, led, active);

                int count = kind switch
                {
                    ListKind.Music => m_engine.Settings.MusicApps.Count,
                    ListKind.Never => m_engine.Settings.NeverTouchApps.Count,
                    _ => m_engine.Settings.FocusOnlyApps.Count,
                };
                var textRect = new RectangleF(r.Left + 18, r.Top, r.Width - 22, r.Height);
                using var tb = new SolidBrush(active ? Color.White : SkinColors.Gold);
                g.DrawString($"{title}  {count}", m_labelFont, tb, textRect, sf);
            }

            SkinPainter.DrawLabel(g, Tabs[(int)m_tab].Hint, m_hintFont, HintLine.Left, HintLine.Top, Color.FromArgb(214, 204, 170));
        }

        private void DrawList(Graphics g)
        {
            var list = CurrentList();
            var clip = g.Clip;
            g.SetClip(RectangleF.Inflate(ListWell, -2, -2));

            int total = list.Count + 1; // + the "add" row
            for (int vi = 0; vi < VisibleRows; vi++)
            {
                int i = vi + m_scroll;
                if (i >= total) break;
                var row = RowRect(vi);
                bool editing = i == m_editIndex;
                bool hotRow = m_hot is RowHit rh && rh.Index == i;

                if (i == list.Count)
                {
                    // "+ add app" row
                    if (!editing)
                    {
                        using var addBrush = new SolidBrush(hotRow ? SkinColors.ListText : Color.FromArgb(0, 150, 0));
                        g.DrawString("+  add app...", m_listFont, addBrush, row.Left + 6, row.Top + 2);
                    }
                    else DrawEditRowBack(g, row, i);
                    continue;
                }

                string entry = list[i];
                if (editing) { DrawEditRowBack(g, row, i); continue; }

                if (hotRow)
                {
                    using var hb = new SolidBrush(Color.FromArgb(0, 40, 0));
                    g.FillRectangle(hb, row);
                }

                using (var tb = new SolidBrush(SkinColors.ListText))
                {
                    g.DrawString($"{i + 1}.", m_listFont, tb, row.Left + 4, row.Top + 2);
                    g.DrawString(entry, m_listFont, tb, new RectangleF(row.Left + 28, row.Top + 2, 250, RowH));
                }

                // status: playing / audio / running / not running
                var st = StatusOf(entry);
                (string label, Color color) = st switch
                {
                    AppStatus.Playing => ("PLAYING", SkinColors.LcdAmber),
                    AppStatus.Audio => ("AUDIO", SkinColors.LcdGreen),
                    AppStatus.Running => ("RUNNING", Color.FromArgb(0, 150, 70)),
                    _ => ("-", Color.FromArgb(0, 70, 30)),
                };
                if (st == AppStatus.Playing && (m_frame / 8) % 2 == 0) color = Color.FromArgb(180, color);
                SkinPainter.Led(g, row.Right - 110, row.Top + RowH / 2, 2.6f, color, st != null);
                m_dots.Draw(g, label, row.Right - 102, row.Top + 5, 1.3f, color);

                if (entry.EndsWith('*'))
                {
                    using var wb = new SolidBrush(Color.FromArgb(0, 160, 0));
                    g.DrawString("matches start", m_tinyFont, wb, row.Right - 190, row.Top + 4);
                }

                // remove button
                var x = RemoveRect(row);
                bool hotX = m_hot is RowHit rx && rx.Index == i && rx.Part == "x";
                using (var xb = new SolidBrush(hotX ? Color.FromArgb(200, SkinColors.LedRed) : Color.FromArgb(40, SkinColors.LedRed)))
                    g.FillRectangle(xb, x);
                using (var xp = new Pen(hotX ? SkinColors.LedRed : Color.FromArgb(120, SkinColors.LedRed))) g.DrawRectangle(xp, x.X, x.Y, x.Width, x.Height);
                using (var xt = new SolidBrush(hotX ? Color.Black : SkinColors.LedRed))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("x", m_tinyFont, xt, x, sf);
            }

            if (total > VisibleRows)
            {
                using var sb = new SolidBrush(Color.FromArgb(0, 160, 0));
                g.DrawString($"{m_scroll + 1}-{Math.Min(total, m_scroll + VisibleRows)} / {total}  (scroll)", m_tinyFont, sb,
                    ListWell.Right - 6, ListWell.Bottom - 13, new StringFormat { Alignment = StringAlignment.Far });
            }
            g.Clip = clip;

            string help = m_editIndex >= 0
                ? "Type to search running apps.  Up/Down + Enter to pick.  End with * to match the start.  Esc cancels."
                : "Click a name to edit it.  Click + add app to add one.";
            SkinPainter.DrawLabel(g, help, m_tinyFont, HelpLine.Left, HelpLine.Top, Color.FromArgb(190, 180, 150));
        }

        private void DrawEditRowBack(Graphics g, RectangleF row, int i)
        {
            using var sel = new SolidBrush(SkinColors.ListSelBack);
            g.FillRectangle(sel, row);
            using var tb = new SolidBrush(SkinColors.ListSelText);
            g.DrawString(i < CurrentList().Count ? $"{i + 1}." : "+", m_listFont, tb, row.Left + 4, row.Top + 2);
            // blinking caret block at the right edge so the row reads as "typing"
            if ((m_frame / 10) % 2 == 0)
            {
                using var cb = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
                g.FillRectangle(cb, row.Right - 10, row.Top + 4, 5, RowH - 8);
            }
        }

        private void DrawSuggestions(Graphics g)
        {
            if (m_editIndex < 0 || m_suggestions.Count == 0) return;
            var r = SuggestRect();

            using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(shadow, r.X + 3, r.Y + 3, r.Width, r.Height);
            using (var bg = new SolidBrush(Color.FromArgb(6, 14, 8))) g.FillRectangle(bg, r);
            using (var border = new Pen(SkinColors.Gold)) g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);

            for (int i = 0; i < m_suggestions.Count; i++)
            {
                var row = new RectangleF(r.X + 1, r.Y + 1 + i * RowH, r.Width - 2, RowH);
                bool sel = i == m_suggestSel || m_hot is SuggestHit sh && sh.Index == i;
                if (sel)
                {
                    using var sb = new SolidBrush(SkinColors.ListSelBack);
                    g.FillRectangle(sb, row);
                }
                var app = m_suggestions[i];
                using (var tb = new SolidBrush(sel ? Color.White : SkinColors.ListText))
                    g.DrawString(app.Name, m_listFont, tb, new RectangleF(row.Left + 8, row.Top + 2, row.Width - 90, RowH));
                (string label, Color color) = app.Status switch
                {
                    AppStatus.Playing => ("PLAYING", SkinColors.LcdAmber),
                    AppStatus.Audio => ("AUDIO", SkinColors.LcdGreen),
                    _ => ("RUNNING", Color.FromArgb(0, 150, 70)),
                };
                m_dots.Draw(g, label, row.Right - 64, row.Top + 5, 1.3f, color);
            }
        }

        private void DrawHotkey(Graphics g)
        {
            string hk = m_engine.Settings.ForceDuckHotkey;
            if (m_capturingHotkey)
            {
                if ((m_frame / 8) % 2 == 0)
                    m_dots.Draw(g, "PRESS KEYS...", HotkeyBox.Left + 8, HotkeyBox.Top + 5, 1.6f, SkinColors.LcdAmber);
            }
            else
            {
                m_dots.Draw(g, hk.Length > 0 ? hk.ToUpperInvariant() : "NONE", HotkeyBox.Left + 8, HotkeyBox.Top + 5, 1.6f,
                    hk.Length > 0 ? SkinColors.LcdGreen : SkinColors.LcdGrey);
            }
            if (Equals(m_hot, "hotkey") && !m_capturingHotkey)
            {
                using var p = new Pen(Color.FromArgb(160, SkinColors.LcdGreen));
                g.DrawRectangle(p, HotkeyBox.X, HotkeyBox.Y, HotkeyBox.Width, HotkeyBox.Height);
            }

            // clear button
            bool hot = Equals(m_hot, "hkclear");
            SkinPainter.DrawButton(g, HotkeyClear, pressed: false, hot: hot);
            using (var b = new SolidBrush(hot ? Color.White : SkinColors.Gold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("x", m_tinyFont, b, HotkeyClear, sf);

            string note = m_capturingHotkey ? "Esc cancels, Backspace sets none" : "click the screen, then press the keys";
            SkinPainter.DrawLabel(g, note, m_tinyFont, HotkeyClear.Right + 8, SectionTop + 5, Color.FromArgb(190, 180, 150));
        }

        private void DrawAttack(Graphics g)
        {
            int v = Math.Clamp(m_engine.Settings.AttackMs, 0, 1000);
            float t = v / 1000f;
            var groove = AttackGroove;
            var fill = new RectangleF(groove.Left + 1, groove.Top + 1, (groove.Width - 2) * t, groove.Height - 2);
            if (fill.Width > 0.5f)
            {
                using var fb = new LinearGradientBrush(new RectangleF(groove.Left, groove.Top, groove.Width, groove.Height),
                    Color.FromArgb(0, 170, 70), Color.FromArgb(230, 200, 40), LinearGradientMode.Horizontal);
                g.FillRectangle(fb, fill);
            }
            float tx = groove.Left + groove.Width * t;
            var thumb = new RectangleF(tx - 7, groove.Top - 4, 14, groove.Height + 8);
            SkinPainter.DrawButton(g, thumb, pressed: false, hot: Equals(m_hot, "attack") || m_draggingAttack);
            using (var grip = new Pen(Color.FromArgb(150, SkinColors.BevelDark)))
                for (int i = -2; i <= 2; i += 2) g.DrawLine(grip, tx + i, thumb.Top + 4, tx + i, thumb.Bottom - 4);

            m_dots.Draw(g, $"{v}ms", ReadoutX + 6, AttackTop + 5, 1.5f, SkinColors.LcdGreen);
            SkinPainter.DrawLabel(g, "before lowering (ignores clicks and UI sounds)", m_tinyFont, GrooveX1, AttackTop + 22,
                Color.FromArgb(190, 180, 150));
        }

        private void DrawBottomButtons(Graphics g)
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            foreach (var (r, text, id) in new[] { (BtnReset, "RESET LIST", "reset"), (BtnDone, "DONE", "done") })
            {
                bool hot = Equals(m_hot, id);
                SkinPainter.DrawButton(g, r, pressed: false, hot: hot);
                using var tb = new SolidBrush(hot ? Color.White : SkinColors.Gold);
                g.DrawString(text, m_labelFont, tb, r, sf);
            }
        }

        // ---- editing -------------------------------------------------------------------------

        private void BeginEdit(int index)
        {
            var list = CurrentList();
            m_editIndex = index;
            m_edit.Text = index < list.Count ? list[index] : "";
            EnsureRowVisible(index);
            PlaceEditBox();
            m_edit.Visible = true;
            m_edit.Focus();
            m_edit.SelectAll();
            RefreshRunning();
            UpdateSuggestions();
            Invalidate();
        }

        private void PlaceEditBox()
        {
            int vi = m_editIndex - m_scroll;
            var row = RowRect(Math.Max(0, vi));
            var r = new RectangleF(row.Left + 28, row.Top + 3, 300, RowH - 5);
            m_edit.Bounds = Rectangle.Round(new RectangleF(r.X * m_scale, r.Y * m_scale, r.Width * m_scale, r.Height * m_scale));
        }

        private void EnsureRowVisible(int index)
        {
            if (index < m_scroll) m_scroll = index;
            else if (index >= m_scroll + VisibleRows) m_scroll = index - VisibleRows + 1;
        }

        private void EndEdit()
        {
            m_editIndex = -1;
            m_suggestions.Clear();
            m_suggestSel = -1;
            m_edit.Visible = false;
            Focus();
            Invalidate();
        }

        private void CommitEdit(string text)
        {
            if (m_editIndex < 0) return;
            var list = CurrentList();
            string name = text.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4].Trim();

            bool duplicate = list.Where((e, i) => i != m_editIndex)
                                 .Any(e => e.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

            if (m_editIndex < list.Count)
            {
                if (name.Length == 0 || duplicate) list.RemoveAt(m_editIndex);
                else list[m_editIndex] = name;
            }
            else if (name.Length > 0 && !duplicate)
            {
                list.Add(name);
            }
            m_engine.SaveSettingsSoon();
            EndEdit();
        }

        private void UpdateSuggestions()
        {
            if (m_editIndex < 0) return;
            string q = m_edit.Text.Trim().TrimEnd('*');
            var list = CurrentList();

            m_suggestions.Clear();
            m_suggestions.AddRange(m_running
                .Where(kv => q.Length == 0 || kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(kv => !list.Any(e => e.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => q.Length > 0 && !kv.Key.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(kv => new RunningApp(kv.Key, kv.Value)));
            m_suggestSel = m_suggestions.Count > 0 && q.Length > 0 ? 0 : -1;
            Invalidate();
        }

        private RectangleF SuggestRect()
        {
            int vi = Math.Max(0, m_editIndex - m_scroll);
            var row = RowRect(vi);
            float h = m_suggestions.Count * RowH + 2;
            float y = row.Bottom + 1;
            if (y + h > H - 6) y = row.Top - h - 1; // not enough room below: open upward
            return new RectangleF(row.Left + 22, y, 300, h);
        }

        private void EditKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    CommitEdit(m_suggestSel >= 0 && m_suggestSel < m_suggestions.Count ? m_suggestions[m_suggestSel].Name : m_edit.Text);
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    EndEdit();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Down:
                    if (m_suggestions.Count > 0) m_suggestSel = Math.Min(m_suggestions.Count - 1, m_suggestSel + 1);
                    Invalidate();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Up:
                    if (m_suggestions.Count > 0) m_suggestSel = Math.Max(-1, m_suggestSel - 1);
                    Invalidate();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Tab:
                    if (m_suggestSel >= 0 && m_suggestSel < m_suggestions.Count)
                    {
                        m_edit.Text = m_suggestions[m_suggestSel].Name;
                        m_edit.SelectionStart = m_edit.Text.Length;
                    }
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // keep Tab inside the edit box (it accepts the suggestion)
            if (m_editIndex >= 0 && (keyData & Keys.KeyCode) == Keys.Tab) return true;
            return base.ProcessDialogKey(keyData);
        }

        // ---- hotkey capture ----------------------------------------------------------------

        private void BeginHotkeyCapture()
        {
            if (m_editIndex >= 0) CommitEdit(m_edit.Text);
            m_capturingHotkey = true;
            m_engine.SuspendHotkey(); // otherwise Windows eats the current hotkey before we see it
            Focus();
            Invalidate();
        }

        private void EndHotkeyCapture(string? newHotkey)
        {
            if (newHotkey != null)
            {
                m_engine.Settings.ForceDuckHotkey = newHotkey;
                m_engine.SaveSettingsSoon();
            }
            m_capturingHotkey = false;
            m_engine.ResumeHotkey();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (m_capturingHotkey)
            {
                e.Handled = e.SuppressKeyPress = true;
                switch (e.KeyCode)
                {
                    case Keys.Escape: EndHotkeyCapture(null); return;
                    case Keys.Back:
                    case Keys.Delete: EndHotkeyCapture(""); return;
                    case Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin: return; // wait for the real key
                }
                var parts = new List<string>();
                if (e.Control) parts.Add("Ctrl");
                if (e.Alt) parts.Add("Alt");
                if (e.Shift) parts.Add("Shift");
                if (parts.Count == 0) return; // needs at least one modifier
                parts.Add(KeyName(e.KeyCode));
                string text = string.Join("+", parts);
                if (HotkeyWindow.TryParse(text, out _, out _)) EndHotkeyCapture(text);
                return;
            }
            if (m_editIndex < 0 && e.KeyCode == Keys.Escape) Close();
        }

        private static string KeyName(Keys k) =>
            k is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (k - Keys.D0))).ToString() : k.ToString();

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Alt combos would otherwise open the (invisible) system menu
            if (m_capturingHotkey)
            {
                OnKeyDown(new KeyEventArgs(keyData));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- mouse -------------------------------------------------------------------------

        private PointF Logical(Point p) => new(p.X / m_scale, p.Y / m_scale);

        private object? HitTest(PointF p)
        {
            if (m_editIndex >= 0 && m_suggestions.Count > 0)
            {
                var sr = SuggestRect();
                if (sr.Contains(p))
                {
                    int i = (int)((p.Y - sr.Top - 1) / RowH);
                    return i >= 0 && i < m_suggestions.Count ? new SuggestHit(i) : null;
                }
            }
            if (BtnClose.Contains(p)) return "close";
            for (int i = 0; i < Tabs.Length; i++) if (TabRect(i).Contains(p)) return new TabHit(Tabs[i].Kind);

            var inner = RectangleF.Inflate(ListWell, -2, -2);
            if (inner.Contains(p))
            {
                int vi = (int)((p.Y - inner.Top) / RowH);
                int idx = vi + m_scroll;
                var list = CurrentList();
                if (vi < VisibleRows && idx <= list.Count)
                {
                    if (idx == list.Count) return new RowHit(idx, "add");
                    return new RowHit(idx, RemoveRect(RowRect(vi)).Contains(p) ? "x" : "name");
                }
                return null;
            }
            if (HotkeyBox.Contains(p)) return "hotkey";
            if (HotkeyClear.Contains(p)) return "hkclear";
            if (AttackRow.Contains(p)) return "attack";
            if (BtnReset.Contains(p)) return "reset";
            if (BtnDone.Contains(p)) return "done";
            if (TitleBar.Contains(p)) return "title";
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = Logical(e.Location);
            if (m_draggingAttack) { SetAttackFromX(p.X); return; }
            var hot = HitTest(p);
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
            if (!m_draggingAttack) { m_hot = null; Invalidate(); }
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var p = Logical(e.Location);
            var hit = HitTest(p);

            // clicking anywhere while typing: a suggestion picks it, anything else keeps what was typed
            if (m_editIndex >= 0)
            {
                if (hit is SuggestHit sh) { CommitEdit(m_suggestions[sh.Index].Name); return; }
                CommitEdit(m_edit.Text);
                hit = HitTest(p); // rows may have moved
            }
            if (m_capturingHotkey && !Equals(hit, "hotkey")) EndHotkeyCapture(null);

            switch (hit)
            {
                case "title":
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
                    break;
                case "attack":
                    m_draggingAttack = true;
                    Capture = true;
                    SetAttackFromX(p.X);
                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (m_draggingAttack)
            {
                m_draggingAttack = false;
                Capture = false;
                m_engine.SaveSettingsSoon();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            var hit = HitTest(Logical(e.Location));
            switch (hit)
            {
                case "close":
                case "done":
                    Close();
                    break;
                case TabHit th:
                    if (m_tab != th.Kind) { m_tab = th.Kind; m_scroll = 0; }
                    Invalidate();
                    break;
                case RowHit { Part: "x" } rx:
                    CurrentList().RemoveAt(rx.Index);
                    m_engine.SaveSettingsSoon();
                    Invalidate();
                    break;
                case RowHit rh:
                    BeginEdit(rh.Index);
                    break;
                case "hotkey":
                    if (!m_capturingHotkey) BeginHotkeyCapture();
                    break;
                case "hkclear":
                    m_engine.Settings.ForceDuckHotkey = "";
                    m_engine.SaveSettingsSoon();
                    m_engine.ResumeHotkey();
                    Invalidate();
                    break;
                case "reset":
                    var list = CurrentList();
                    list.Clear();
                    list.AddRange(DefaultList(m_tab));
                    m_scroll = 0;
                    m_engine.SaveSettingsSoon();
                    Invalidate();
                    break;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var p = Logical(e.Location);
            if (AttackRow.Contains(p))
            {
                var s = m_engine.Settings;
                s.AttackMs = Math.Clamp(s.AttackMs + Math.Sign(e.Delta) * 10, 0, 1000);
                m_engine.SaveSettingsSoon();
                Invalidate();
                return;
            }
            if (ListWell.Contains(p) && m_editIndex < 0)
            {
                int total = CurrentList().Count + 1;
                m_scroll = Math.Clamp(m_scroll - Math.Sign(e.Delta), 0, Math.Max(0, total - VisibleRows));
                Invalidate();
            }
        }

        private void SetAttackFromX(float x)
        {
            float t = Math.Clamp((x - GrooveX1) / (GrooveX2 - GrooveX1), 0, 1);
            int v = (int)Math.Round(t * 100) * 10;
            if (m_engine.Settings.AttackMs != v)
            {
                m_engine.Settings.AttackMs = v;
                Invalidate();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (m_editIndex >= 0) CommitEdit(m_edit.Text);
            if (m_capturingHotkey) EndHotkeyCapture(null);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_anim.Dispose();
                m_background?.Dispose();
                m_dots.Dispose();
                m_labelFont.Dispose();
                m_tinyFont.Dispose();
                m_listFont.Dispose();
                m_hintFont.Dispose();
                m_editFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
