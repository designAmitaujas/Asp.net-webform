using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ProcurePay
{
    // Vector icons drawn with GDI+ on a 24 x 24 grid. No image files, so they stay sharp at any size and any colour.
    public static class Icons
    {
        public static void Draw(Graphics g, string name, Rectangle r, Color color, float stroke = 1.9f)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var pen = new Pen(color, stroke))
            using (var fill = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                Shape(g, pen, fill, name);
            }
            g.Restore(state);
        }

        private static GraphicsPath Box(float x, float y, float w, float h, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void Rr(Graphics g, Pen pen, float x, float y, float w, float h, float rad)
        {
            using (var p = Box(x, y, w, h, rad)) g.DrawPath(pen, p);
        }

        private static void Ln(Graphics g, Pen pen, params float[] pts)
        {
            var list = new List<PointF>();
            for (int i = 0; i + 1 < pts.Length; i += 2) list.Add(new PointF(pts[i], pts[i + 1]));
            g.DrawLines(pen, list.ToArray());
        }

        private static void Shape(Graphics g, Pen pen, Brush fill, string name)
        {
            switch (name)
            {
                case "overview":
                    Rr(g, pen, 3.5f, 3.5f, 7, 7, 2); Rr(g, pen, 13.5f, 3.5f, 7, 7, 2);
                    Rr(g, pen, 3.5f, 13.5f, 7, 7, 2); Rr(g, pen, 13.5f, 13.5f, 7, 7, 2);
                    break;
                case "agreements":
                    Ln(g, pen, 14, 3, 7, 3, 5, 5, 5, 20, 7, 22, 17, 22, 19, 20, 19, 8, 14, 3, 14, 8, 19, 8);
                    Ln(g, pen, 8.5f, 13, 15.5f, 13); Ln(g, pen, 8.5f, 17, 13, 17);
                    break;
                case "po":      // shopping bag
                    Ln(g, pen, 5, 8, 19, 8, 20, 21, 4, 21, 5, 8);
                    g.DrawArc(pen, 8, 3, 8, 10, 180, 180);
                    break;
                case "invoices": // receipt with a torn edge
                    Ln(g, pen, 5, 3, 19, 3, 19, 21, 16.5f, 19, 14, 21, 11.5f, 19, 9, 21, 7, 19.5f, 5, 21, 5, 3);
                    Ln(g, pen, 8.5f, 8, 15.5f, 8); Ln(g, pen, 8.5f, 12, 15.5f, 12);
                    break;
                case "vouchers": // card
                    Rr(g, pen, 2.5f, 5, 19, 14, 3);
                    Ln(g, pen, 2.5f, 10, 21.5f, 10); Ln(g, pen, 6.5f, 15, 10.5f, 15);
                    break;
                case "budgets":  // pie
                    g.DrawEllipse(pen, 3, 3, 18, 18);
                    Ln(g, pen, 12, 3, 12, 12, 20.2f, 15.5f);
                    break;
                case "reports":  // bars
                    Ln(g, pen, 4, 20, 20, 20);
                    Rr(g, pen, 5.5f, 11, 3.5f, 9, 1); Rr(g, pen, 10.25f, 5, 3.5f, 15, 1); Rr(g, pen, 15, 14, 3.5f, 6, 1);
                    break;
                case "tally":    // export arrow leaving a tray
                    Ln(g, pen, 4, 14, 4, 20, 20, 20, 20, 14);
                    Ln(g, pen, 12, 15, 12, 3.5f); Ln(g, pen, 7.5f, 8, 12, 3.5f, 16.5f, 8);
                    break;
                case "notifications":
                    Ln(g, pen, 5.5f, 17, 6.5f, 15, 6.5f, 10);
                    g.DrawArc(pen, 6.5f, 4, 11, 11, 180, 180);
                    Ln(g, pen, 17.5f, 10, 17.5f, 15, 18.5f, 17, 5.5f, 17);
                    g.DrawArc(pen, 9.5f, 17.5f, 5, 4, 0, 180);
                    break;
                case "masters":  // shield with a check
                    Ln(g, pen, 12, 2.5f, 20, 5.5f, 20, 12);
                    Ln(g, pen, 20, 12, 18.5f, 16.5f, 12, 21.5f, 5.5f, 16.5f, 4, 12, 4, 5.5f, 12, 2.5f);
                    Ln(g, pen, 8.5f, 12, 11, 14.5f, 15.5f, 9.5f);
                    break;
                case "audit":    // clock
                    g.DrawEllipse(pen, 3, 3, 18, 18);
                    Ln(g, pen, 12, 7, 12, 12, 15.5f, 14);
                    break;
                case "plus":
                    Ln(g, pen, 12, 5, 12, 19); Ln(g, pen, 5, 12, 19, 12);
                    break;
                case "back":
                    Ln(g, pen, 20, 12, 5, 12); Ln(g, pen, 11, 6, 5, 12, 11, 18);
                    break;
                case "arrow":
                    Ln(g, pen, 4, 12, 19, 12); Ln(g, pen, 13, 6, 19, 12, 13, 18);
                    break;
                case "print":
                    Ln(g, pen, 7, 9, 7, 3.5f, 17, 3.5f, 17, 9);
                    Rr(g, pen, 3.5f, 9, 17, 8, 2);
                    Ln(g, pen, 7, 14, 7, 20.5f, 17, 20.5f, 17, 14, 7, 14);
                    break;
                case "check":
                    Ln(g, pen, 4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 6.5f);
                    break;
                case "close":
                    Ln(g, pen, 6, 6, 18, 18); Ln(g, pen, 18, 6, 6, 18);
                    break;
                case "send":     // paper plane
                    Ln(g, pen, 21, 3, 3, 10.5f, 10, 13.5f, 21, 3, 13.5f, 21, 10, 13.5f);
                    break;
                case "sign":     // pen nib on a line
                    Ln(g, pen, 14.5f, 5, 19, 9.5f, 9, 19.5f, 4, 20.5f, 5, 15.5f, 14.5f, 5);
                    Ln(g, pen, 13, 21, 21, 21);
                    break;
                case "reopen":   // circular arrow
                    g.DrawArc(pen, 4, 4, 16, 16, 40, 280);
                    Ln(g, pen, 19.5f, 3.5f, 18.5f, 7.5f, 14.5f, 7);
                    break;
                case "edit":     // pencil
                    Ln(g, pen, 4, 20, 4.8f, 15.8f, 16, 4.6f, 19.4f, 8, 8.2f, 19.2f, 4, 20);
                    Ln(g, pen, 13.5f, 7, 17, 10.5f);
                    break;
                case "attach":   // document with a plus
                    Ln(g, pen, 14, 3, 7, 3, 5, 5, 5, 20, 7, 22, 17, 22, 19, 20, 19, 8, 14, 3, 14, 8, 19, 8);
                    Ln(g, pen, 12, 12.5f, 12, 18.5f); Ln(g, pen, 9, 15.5f, 15, 15.5f);
                    break;
                case "eye":
                    Ln(g, pen, 2.5f, 12, 7, 6.8f, 12, 5.5f, 17, 6.8f, 21.5f, 12, 17, 17.2f, 12, 18.5f, 7, 17.2f, 2.5f, 12);
                    g.DrawEllipse(pen, 9, 9, 6, 6);
                    break;
                case "user":
                    g.DrawEllipse(pen, 8, 3.5f, 8, 8);
                    g.DrawArc(pen, 4, 14, 16, 14, 180, 180);
                    break;
                case "lock":
                    Rr(g, pen, 5, 10.5f, 14, 10, 2.5f);
                    g.DrawArc(pen, 8, 3.5f, 8, 10, 180, 180);
                    Ln(g, pen, 12, 14.5f, 12, 17);
                    break;
                case "building":
                    Ln(g, pen, 4.5f, 21, 4.5f, 4.5f, 14, 4.5f, 14, 21);
                    Ln(g, pen, 14, 10, 19.5f, 10, 19.5f, 21); Ln(g, pen, 3, 21, 21, 21);
                    Ln(g, pen, 8, 9, 10.5f, 9); Ln(g, pen, 8, 13, 10.5f, 13); Ln(g, pen, 8, 17, 10.5f, 17);
                    break;
                case "signout":
                    Ln(g, pen, 10, 4, 5, 4, 5, 20, 10, 20);
                    Ln(g, pen, 9, 12, 20, 12); Ln(g, pen, 15.5f, 7.5f, 20, 12, 15.5f, 16.5f);
                    break;
                case "clock":
                    g.DrawEllipse(pen, 3, 3, 18, 18);
                    Ln(g, pen, 12, 7, 12, 12, 15.5f, 14);
                    break;
                case "info":
                    g.DrawEllipse(pen, 3, 3, 18, 18);
                    Ln(g, pen, 12, 11, 12, 16.5f); g.FillEllipse(fill, 10.9f, 6.9f, 2.2f, 2.2f);
                    break;
                case "warn":
                    Ln(g, pen, 12, 3.5f, 21.5f, 20, 2.5f, 20, 12, 3.5f);
                    Ln(g, pen, 12, 9.5f, 12, 14); g.FillEllipse(fill, 11, 16, 2, 2);
                    break;
                default:
                    g.DrawEllipse(pen, 5, 5, 14, 14);
                    break;
            }
        }
    }

    // Drop-in replacement for Button: rounded, icon + label, clear hover / pressed / keyboard-focus states.
    // Kinds: primary (teal), good (green), bad (red), ghost (outlined), soft (tinted)
    public class AppButton : Button
    {
        private string kind = "primary";
        private string icon;
        private bool iconRight;
        private bool hot, down;

        public int Radius = 8;

        public AppButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Font = Theme.F(9, FontStyle.Bold);
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
        }

        public string Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public string IconName { get { return icon; } set { icon = value; Invalidate(); } }
        public bool IconRight { get { return iconRight; } set { iconRight = value; Invalidate(); } }

        // Sets size from the label (and icon) so buttons never clip their text
        public AppButton Fit(int height, int minWidth = 0)
        {
            int w = TextRenderer.MeasureText(Text, Font, new Size(1000, 100), TextFormatFlags.NoPadding).Width + 34;
            if (icon != null) w += 24;
            Size = new Size(Math.Max(minWidth, w), height);
            return this;
        }

        public static AppButton Make(string text, string kind, string icon, int height, bool iconRight = false, int minWidth = 0)
        {
            var b = new AppButton { Text = text, kind = kind, icon = icon, iconRight = iconRight };
            return b.Fit(height, minWidth);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        private void Colors(out Color back, out Color fore, out Color border)
        {
            Color baseBack, hoverBack;
            switch (kind)
            {
                case "good": baseBack = Theme.Good; hoverBack = Theme.GoodDark; break;
                case "bad": baseBack = Theme.Bad; hoverBack = Theme.BadDark; break;
                case "ghost":
                    baseBack = Color.White; hoverBack = Theme.BrandSoft; break;
                case "soft":
                    baseBack = Theme.BrandSoft; hoverBack = ControlPaint.Light(Theme.Brand2, 0.85f); break;
                default: baseBack = Theme.Brand2; hoverBack = Theme.AccentDark; break;
            }
            bool solid = kind != "ghost" && kind != "soft";
            back = down ? (solid ? ControlPaint.Dark(hoverBack, 0.1f) : ControlPaint.Light(Theme.Brand2, 0.75f)) : (hot ? hoverBack : baseBack);
            fore = solid ? Color.White : (kind == "soft" ? Theme.AccentDark : Theme.Brand);
            border = kind == "ghost" ? (hot ? Theme.Brand2 : Theme.Line) : back;
            if (!Enabled)
            {
                back = solid ? Theme.Line : Theme.Bg;
                fore = Theme.Ink3;
                border = Theme.Line;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // the parent colour shows in the corners
            Color corner = Parent != null ? Parent.BackColor : Theme.Bg;
            if (corner == Color.Transparent) corner = Theme.Bg;
            g.Clear(corner);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back, fore, border;
            Colors(out back, out fore, out border);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Gfx.RoundRect(r, Radius))
            using (var br = new SolidBrush(back))
            using (var pen = new Pen(border))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            // keyboard focus ring (inside the button so it never clips)
            if (Focused && ShowFocusCues && Enabled)
            {
                var fr = new Rectangle(2, 2, Width - 5, Height - 5);
                using (var path = Gfx.RoundRect(fr, Math.Max(2, Radius - 2)))
                using (var pen = new Pen(Color.FromArgb(kind == "ghost" || kind == "soft" ? 150 : 200, kind == "ghost" || kind == "soft" ? Theme.Brand2 : Color.White), 1.5f))
                    g.DrawPath(pen, path);
            }

            int isz = 16;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
            Size ts = TextRenderer.MeasureText(g, Text, Font, new Size(1000, 100), flags);
            int gap = icon != null ? 7 : 0;
            int total = ts.Width + (icon != null ? isz + gap : 0);
            int x = (Width - total) / 2;
            int cy = Height / 2;

            if (icon != null && !iconRight)
            {
                Icons.Draw(g, icon, new Rectangle(x, cy - isz / 2, isz, isz), fore);
                x += isz + gap;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), fore, flags);
            if (icon != null && iconRight)
                Icons.Draw(g, icon, new Rectangle(x + ts.Width + gap, cy - isz / 2, isz, isz), fore);
        }
    }

    // One row of the sidebar: icon, label, marigold marker on the active page
    public class NavItem : Control
    {
        public string Key, Icon;
        public bool Active, Soon;
        public int Badge;   // unread count drawn as a red pill on the right (0 = none)
        private bool hot;

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.F(10);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && !Soon) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Brand);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fore;
            if (Active) fore = Color.White;
            else if (Soon) fore = Theme.SoonText;
            else fore = hot ? Color.White : Theme.NavText;

            if (Active || (hot && !Soon))
            {
                using (var path = Gfx.RoundRect(r, 8))
                using (var br = new SolidBrush(Active ? Theme.NavActive : Theme.NavHover))
                    g.FillPath(br, path);
            }
            if (Active)
            {
                using (var br = new SolidBrush(Theme.Gold))
                using (var path = Gfx.RoundRect(new Rectangle(0, 9, 4, Height - 19), 2))
                    g.FillPath(br, path);
            }
            if (Focused && ShowFocusCues)
            {
                using (var path = Gfx.RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), 7))
                using (var pen = new Pen(Theme.Gold, 1.5f))
                    g.DrawPath(pen, path);
            }

            Icons.Draw(g, Icon, new Rectangle(16, (Height - 18) / 2, 18, 18), Active ? Theme.Gold : fore, 1.8f);

            var font = Active ? Theme.F(9.5f, FontStyle.Bold) : Theme.F(9.5f);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int tx = 46;
            int tw = Width - tx - (Soon ? 44 : (Badge > 0 ? 44 : 10));
            TextRenderer.DrawText(g, Text, font, new Rectangle(tx, 0, tw, Height), fore, flags);

            if (Badge > 0 && !Soon)
            {
                string txt = Badge > 99 ? "99+" : Badge.ToString();
                var bf = Theme.F(8, FontStyle.Bold);
                int bw = Math.Max(22, TextRenderer.MeasureText(txt, bf, new Size(100, 20), TextFormatFlags.NoPadding).Width + 12);
                var pill = new Rectangle(Width - 12 - bw, (Height - 18) / 2, bw, 18);
                using (var path = Gfx.RoundRect(pill, 9))
                using (var br = new SolidBrush(Theme.Bad))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, txt, bf, pill, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (Soon)
            {
                var chip = new Rectangle(Width - 42, (Height - 18) / 2, 34, 18);
                using (var path = Gfx.RoundRect(chip, 9))
                using (var pen = new Pen(Theme.SideLine))
                    g.DrawPath(pen, path);
                TextRenderer.DrawText(g, "soon", Theme.F(7.5f), chip, Theme.SoonText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
