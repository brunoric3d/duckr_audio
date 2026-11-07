/*
 *  Duckr - skin
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Drawing helpers for the old-school "player skin" look: brushed metal,
 *  bevels, dot-matrix LCD text, LED segments.
 *  Everything is drawn in a 96 dpi "logical" space; SkinForm scales it.
 */

using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Duckr.Skin
{
    internal static class SkinColors
    {
        public static readonly Color MetalTop = Color.FromArgb(70, 74, 98);
        public static readonly Color MetalBottom = Color.FromArgb(34, 36, 52);
        public static readonly Color BevelLight = Color.FromArgb(128, 134, 168);
        public static readonly Color BevelDark = Color.FromArgb(12, 12, 20);
        public static readonly Color Outline = Color.FromArgb(4, 4, 8);

        public static readonly Color Gold = Color.FromArgb(232, 196, 104);
        public static readonly Color GoldDark = Color.FromArgb(120, 92, 36);
        public static readonly Color LabelShadow = Color.FromArgb(10, 10, 16);

        public static readonly Color LcdBack = Color.FromArgb(4, 10, 6);
        public static readonly Color LcdGreen = Color.FromArgb(0, 236, 96);
        public static readonly Color LcdDim = Color.FromArgb(0, 60, 26);
        public static readonly Color LcdAmber = Color.FromArgb(255, 200, 48);
        public static readonly Color LcdRed = Color.FromArgb(255, 72, 56);
        public static readonly Color LcdCyan = Color.FromArgb(70, 220, 255);
        public static readonly Color LcdGrey = Color.FromArgb(70, 96, 80);

        public static readonly Color LedGreen = Color.FromArgb(40, 255, 90);
        public static readonly Color LedYellow = Color.FromArgb(255, 220, 40);
        public static readonly Color LedRed = Color.FromArgb(255, 50, 40);

        public static readonly Color ListBack = Color.FromArgb(0, 0, 0);
        public static readonly Color ListText = Color.FromArgb(0, 255, 0);
        public static readonly Color ListSelBack = Color.FromArgb(0, 0, 198);
        public static readonly Color ListSelText = Color.FromArgb(255, 255, 255);
    }

    internal static class SkinPainter
    {
        // ---- metal and bevels -------------------------------------------------

        public static void Metal(Graphics g, RectangleF r, int seed = 7)
        {
            using (var lg = new LinearGradientBrush(r, SkinColors.MetalTop, SkinColors.MetalBottom, LinearGradientMode.Vertical))
                g.FillRectangle(lg, r);

            // brushed lines
            var rnd = new Random(seed);
            using var pen = new Pen(Color.Black, 1f);
            for (float y = r.Top; y < r.Bottom; y += 1f)
            {
                int a = rnd.Next(0, 18);
                bool light = rnd.Next(2) == 0;
                pen.Color = light ? Color.FromArgb(a, 255, 255, 255) : Color.FromArgb(a, 0, 0, 0);
                g.DrawLine(pen, r.Left, y + 0.5f, r.Right, y + 0.5f);
            }
        }

        public static void Bevel(Graphics g, RectangleF r, bool raised, float width = 1f)
        {
            var tl = raised ? SkinColors.BevelLight : SkinColors.BevelDark;
            var br = raised ? SkinColors.BevelDark : SkinColors.BevelLight;
            using var p1 = new Pen(tl, width);
            using var p2 = new Pen(br, width);
            float h = width / 2f;
            g.DrawLine(p1, r.Left + h, r.Bottom - h, r.Left + h, r.Top + h);
            g.DrawLine(p1, r.Left + h, r.Top + h, r.Right - h, r.Top + h);
            g.DrawLine(p2, r.Right - h, r.Top + h, r.Right - h, r.Bottom - h);
            g.DrawLine(p2, r.Right - h, r.Bottom - h, r.Left + h, r.Bottom - h);
        }

        /// <summary>Sunken black well (LCD, list, meters) with a double bevel.</summary>
        public static void Well(Graphics g, RectangleF r, Color fill)
        {
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            Bevel(g, r, raised: false);
            Bevel(g, RectangleF.Inflate(r, 1, 1), raised: false);
        }

        /// <summary>Raised metal button.</summary>
        public static void DrawButton(Graphics g, RectangleF r, bool pressed, bool hot)
        {
            var top = pressed ? Color.FromArgb(40, 42, 60) : hot ? Color.FromArgb(104, 110, 142) : Color.FromArgb(88, 93, 122);
            var bottom = pressed ? Color.FromArgb(64, 68, 92) : Color.FromArgb(44, 46, 66);
            using (var lg = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(lg, r);
            using (var o = new Pen(SkinColors.Outline)) g.DrawRectangle(o, r.X, r.Y, r.Width, r.Height);
            Bevel(g, RectangleF.Inflate(r, -1, -1), raised: !pressed);
        }

        /// <summary>Small engraved label in gold with a dark shadow.</summary>
        public static void DrawLabel(Graphics g, string text, Font font, float x, float y, Color? color = null, StringAlignment align = StringAlignment.Near)
        {
            using var sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Near };
            using (var sh = new SolidBrush(SkinColors.LabelShadow))
                g.DrawString(text, font, sh, x + 1, y + 1, sf);
            using (var b = new SolidBrush(color ?? SkinColors.Gold))
                g.DrawString(text, font, b, x, y, sf);
        }

        /// <summary>Round LED with glow.</summary>
        public static void Led(Graphics g, float cx, float cy, float radius, Color color, bool on)
        {
            var body = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            if (on)
            {
                using var glow = new SolidBrush(Color.FromArgb(70, color));
                g.FillEllipse(glow, RectangleF.Inflate(body, radius * 0.8f, radius * 0.8f));
            }
            var c = on ? color : Color.FromArgb(255, color.R / 5, color.G / 5, color.B / 5);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, body);
            using (var hi = new SolidBrush(Color.FromArgb(on ? 170 : 50, 255, 255, 255)))
                g.FillEllipse(hi, cx - radius * 0.55f, cy - radius * 0.65f, radius * 0.7f, radius * 0.5f);
            using (var o = new Pen(SkinColors.Outline, 0.8f)) g.DrawEllipse(o, body);
        }

        /// <summary>Gold ridged lines used either side of the title (old player title bars).</summary>
        public static void TitleRidges(Graphics g, float x1, float x2, float yCenter)
        {
            if (x2 - x1 < 4) return;
            using var light = new Pen(SkinColors.Gold, 1f);
            using var dark = new Pen(SkinColors.GoldDark, 1f);
            for (int i = -3; i <= 3; i += 2)
            {
                float y = yCenter + i;
                g.DrawLine(light, x1, y, x2, y);
                g.DrawLine(dark, x1, y + 1, x2, y + 1);
            }
        }

        // ---- LED meters -------------------------------------------------------

        /// <summary>Vertical segmented LED meter. value and peak are 0..1.</summary>
        public static void VerticalMeter(Graphics g, RectangleF r, int segments, float value, float peak)
        {
            float gap = 1.5f;
            float segH = (r.Height - gap * (segments - 1)) / segments;
            int lit = (int)Math.Round(Math.Clamp(value, 0, 1) * segments);
            int peakSeg = (int)Math.Round(Math.Clamp(peak, 0, 1) * segments) - 1;

            for (int i = 0; i < segments; i++)
            {
                float y = r.Bottom - (i + 1) * segH - i * gap;
                var seg = new RectangleF(r.X, y, r.Width, segH);
                Color c = SegmentColor(i, segments);
                bool on = i < lit || i == peakSeg && peakSeg > 0;
                FillSegment(g, seg, c, on);
            }
        }

        /// <summary>Horizontal segmented LED bar. value is 0..1.</summary>
        public static void HorizontalMeter(Graphics g, RectangleF r, int segments, float value, Color? solid = null)
        {
            float gap = 1.5f;
            float segW = (r.Width - gap * (segments - 1)) / segments;
            int lit = (int)Math.Round(Math.Clamp(value, 0, 1) * segments);
            for (int i = 0; i < segments; i++)
            {
                var seg = new RectangleF(r.X + i * (segW + gap), r.Y, segW, r.Height);
                FillSegment(g, seg, solid ?? SegmentColor(i, segments), i < lit);
            }
        }

        private static Color SegmentColor(int i, int segments)
        {
            float t = (i + 0.5f) / segments;
            return t > 0.86f ? SkinColors.LedRed : t > 0.66f ? SkinColors.LedYellow : SkinColors.LedGreen;
        }

        private static void FillSegment(Graphics g, RectangleF seg, Color c, bool on)
        {
            if (on)
            {
                using var glow = new SolidBrush(Color.FromArgb(60, c));
                g.FillRectangle(glow, RectangleF.Inflate(seg, 1.2f, 1.2f));
                using var b = new SolidBrush(c);
                g.FillRectangle(b, seg);
                using var hi = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
                g.FillRectangle(hi, seg.X, seg.Y, seg.Width, Math.Max(0.8f, seg.Height * 0.3f));
            }
            else
            {
                using var b = new SolidBrush(Color.FromArgb(255, c.R / 7, c.G / 7, c.B / 7));
                g.FillRectangle(b, seg);
            }
        }
    }

    /// <summary>
    /// Dot-matrix text. A string is rendered once with a tiny 1-bit font into a
    /// bitmap, and every lit pixel is drawn as a glowing LCD dot. Works for any
    /// text (app names with lower case, accents) without a hand-made font table.
    /// </summary>
    internal sealed class DotMatrix : IDisposable
    {
        private readonly Font m_font;
        private readonly Dictionary<string, bool[,]> m_cache = new();

        public DotMatrix(Font font) => m_font = font;

        /// <summary>Lit pixels of the text, [col, row].</summary>
        public bool[,] Glyphs(string text)
        {
            if (m_cache.TryGetValue(text, out var hit)) return hit;
            if (m_cache.Count > 256) m_cache.Clear();

            Size size;
            using (var probe = new Bitmap(1, 1))
            using (var pg = Graphics.FromImage(probe))
            {
                pg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                var s = pg.MeasureString(text.Length == 0 ? " " : text, m_font, PointF.Empty, StringFormat.GenericTypographic);
                size = new Size(Math.Max(1, (int)Math.Ceiling(s.Width) + 2), Math.Max(1, (int)Math.Ceiling(m_font.GetHeight(pg)) + 1));
            }

            var grid = new bool[size.Width, size.Height];
            using (var bmp = new Bitmap(size.Width, size.Height))
            {
                using (var bg = Graphics.FromImage(bmp))
                {
                    bg.Clear(Color.Black);
                    bg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    bg.DrawString(text, m_font, Brushes.White, 0, 0, StringFormat.GenericTypographic);
                }
                for (int x = 0; x < size.Width; x++)
                    for (int y = 0; y < size.Height; y++)
                        grid[x, y] = bmp.GetPixel(x, y).G > 128;
            }

            // trim empty rows at top and bottom so text sits tight
            m_cache[text] = Trim(grid);
            return m_cache[text];
        }

        private static bool[,] Trim(bool[,] g)
        {
            int w = g.GetLength(0), h = g.GetLength(1);
            int top = 0, bottom = h - 1, right = 0;
            bool RowEmpty(int y) { for (int x = 0; x < w; x++) if (g[x, y]) return false; return true; }
            while (top < bottom && RowEmpty(top)) top++;
            while (bottom > top && RowEmpty(bottom)) bottom--;
            for (int x = 0; x < w; x++) for (int y = top; y <= bottom; y++) if (g[x, y]) right = Math.Max(right, x);
            int nw = right + 2, nh = bottom - top + 1;
            var o = new bool[nw, nh];
            for (int x = 0; x < nw && x < w; x++) for (int y = 0; y < nh; y++) o[x, y] = g[x, y + top];
            return o;
        }

        /// <summary>
        /// Draw text as dots. maxCols clips (and scrolls, with offsetCols) the text.
        /// When wrap is true the text repeats for a marquee effect.
        /// </summary>
        public void Draw(Graphics g, string text, float x, float y, float pitch, Color color,
                         int maxCols = int.MaxValue, int offsetCols = 0, bool wrap = false, int baselineRows = 0)
        {
            var grid = Glyphs(text);
            int w = grid.GetLength(0), h = grid.GetLength(1);
            if (w == 0) return;
            int cols = Math.Min(maxCols, wrap ? maxCols : w);
            float dot = pitch * 0.78f;
            float yOff = baselineRows > h ? (baselineRows - h) * pitch : 0;

            var lit = new List<RectangleF>(cols * h / 3);
            for (int c = 0; c < cols; c++)
            {
                int sx = wrap ? ((c + offsetCols) % w + w) % w : c + offsetCols;
                if (sx < 0 || sx >= w) continue;
                for (int r = 0; r < h; r++)
                    if (grid[sx, r])
                        lit.Add(new RectangleF(x + c * pitch, y + yOff + r * pitch, dot, dot));
            }
            if (lit.Count == 0) return;
            var arr = lit.ToArray();

            // soft glow under the dots, then the dots
            using (var glow = new SolidBrush(Color.FromArgb(55, color)))
            {
                var big = new RectangleF[arr.Length];
                float grow = pitch * 0.35f;
                for (int i = 0; i < arr.Length; i++) big[i] = RectangleF.Inflate(arr[i], grow, grow);
                g.FillRectangles(glow, big);
            }
            using (var b = new SolidBrush(color)) g.FillRectangles(b, arr);
        }

        public int Width(string text) => Glyphs(text).GetLength(0);

        public void Dispose() => m_font.Dispose();
    }
}
