using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    public static class Gfx
    {
        // rounded rectangle outline (used for badges, buttons, tabs)
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Round "pill" tab used for the status filter
    public class PillTab : Control
    {
        public bool On;
        public bool HasError;   // draws a red dot when the form tab has validation problems

        public PillTab(string text)
        {
            Text = text;
            Font = Theme.F(9);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 28, 30);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Gfx.RoundRect(r, Height / 2))
            using (var fill = new SolidBrush(On ? Theme.Brand2 : Color.White))
            using (var pen = new Pen(On ? Theme.Brand2 : Theme.Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, r, On ? Color.White : Theme.Ink2,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (HasError)
                using (var dot = new SolidBrush(Theme.Bad))
                    g.FillEllipse(dot, Width - 15, 4, 9, 9);
        }
    }

    // DataGridView without flicker
    public class BufferedGrid : DataGridView
    {
        public BufferedGrid() { DoubleBuffered = true; }
    }

    public class AgreementsPage : Panel
    {
        public event Action<Agreement> OpenRequested;
        public event EventHandler NewRequested;

        private static readonly Color Line2 = Theme.Line2;
        private static readonly Color HoverBg = Theme.Hover;
        private static readonly Color HeadBg = Theme.Hover;
        private static readonly Font BoldF = Theme.F(9.5f, FontStyle.Bold);
        private static readonly Font SmallF = Theme.F(9);
        private static readonly Font BadgeF = Theme.F(8.5f, FontStyle.Bold);
        private static readonly Font BtnF = Theme.F(8.5f, FontStyle.Bold);

        private readonly string[] statuses = { "", "Draft", "Pending Approval", "Approved", "Signed", "Rejected", "Cancelled" };
        private string filter = "";
        private List<Agreement> all;
        private Label lblEyebrow, lblEmpty;
        private BufferedGrid grid;
        private readonly List<PillTab> tabs = new List<PillTab>();

        // search + filters (all combine with the status tab)
        private TextBox tbSearch;
        private ComboBox cbSupplier;
        private DateTimePicker dtFrom, dtTo;
        private CheckBox chkMine;
        private Timer debounce;
        private const string AllSuppliers = "All suppliers";

        public AgreementsPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Reload();
        }

        // ---------- helpers
        private static Label L(string text, Font font, Color color, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(x, y)
            };
        }

        // Stacks controls top to bottom (WinForms docks the LAST added control highest, so we add in reverse).
        private static void Stack(Control parent, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                topToBottom[i].Dock = DockStyle.Top;
                parent.Controls.Add(topToBottom[i]);
            }
        }

        private static Panel Gap(int h)
        {
            return new Panel { Height = h, BackColor = Color.Transparent };
        }

        private bool NeedsYou(Agreement a)
        {
            return AgreementRules.CanApprove(a, Current.Role, AppSession.UserName)
                || AgreementRules.CanSign(a, Current.Role)
                || (a.Status == "Draft" && AgreementRules.CanSubmit(Current.Role)
                    && string.Equals(a.CreatedBy, AppSession.UserName, StringComparison.OrdinalIgnoreCase));
        }

        private static void BadgeColors(string status, out Color back, out Color fore)
        {
            switch (status)
            {
                case "Signed":
                case "Approved": back = Theme.GoodSoft; fore = Theme.Good; break;
                case "Pending Approval": back = Theme.WarnSoft; fore = Theme.Warn; break;
                case "Rejected":
                case "Cancelled": back = Theme.BadSoft; fore = Theme.Bad; break;
                default: back = ColorTranslator.FromHtml("#E9EEF0"); fore = Theme.Ink2; break;
            }
        }

        // ---------- build the screen
        private void Build()
        {
            all = AgreementStore.ForEntity(Current.Entity.Code).OrderByDescending(a => a.CreatedAt).ToList();

            // table card first (it fills the space left over)
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            BuildGrid();
            lblEmpty = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No agreements here yet.",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.F(10),
                ForeColor = Theme.Ink3,
                BackColor = Color.White,
                Visible = false
            };
            card.Controls.Add(grid);
            card.Controls.Add(lblEmpty);
            Controls.Add(card);

            // heading row
            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            lblEyebrow = L("", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2);
            head.Controls.Add(lblEyebrow);
            head.Controls.Add(L("Purchase agreements", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));
            if (AgreementRules.CanCreate(Current.Role))
            {
                var btn = AppButton.Make("New agreement", "primary", "plus", 36);
                btn.Font = Theme.F(9.5f, FontStyle.Bold);
                btn.Fit(36);
                btn.Click += (s, e) => { var h = NewRequested; if (h != null) h(this, EventArgs.Empty); };
                head.Controls.Add(btn);
                head.Resize += (s, e) => btn.Location = new Point(head.Width - btn.Width, 6);
            }

            // info notice
            var notice = new CardPanel { Height = 50, BackColor = Theme.BrandSoft, BorderColor = Theme.BrandLine, Padding = new Padding(1) };
            notice.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(46, 0, 14, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.F(9.5f),
                ForeColor = Theme.Brand,
                BackColor = Theme.BrandSoft,
                UseMnemonic = false,
                Text = "Agreements route through a value-based approval matrix, then execution by the Authorised Signatory. " +
                       "Open any row for details and the approval trail."
            });

            // the label fills the card, so the icon is drawn on the label
            notice.Controls[0].Paint += (s2, e2) => Icons.Draw(e2.Graphics, "info", new Rectangle(15, 15, 18, 18), Theme.Brand2);

            // status tabs
            var tabRow = new Panel { Height = 42, BackColor = Color.Transparent };
            int x = 0;
            foreach (var st in statuses)
            {
                var tab = new PillTab(st == "" ? "All" : st);
                tab.Tag = st;
                tab.Location = new Point(x, 6);
                tab.Click += (s, e) => { filter = (string)((PillTab)s).Tag; Reload(); };
                tabRow.Controls.Add(tab);
                tabs.Add(tab);
                x += tab.Width + 6;
            }

            Stack(this, head, notice, Gap(14), tabRow, BuildFilterBar(), Gap(8));
        }

        // ---------- search box + supplier / date / "needs me" filters
        private Control BuildFilterBar()
        {
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.Transparent, Padding = new Padding(0) };

            // search
            tbSearch = new TextBox { Width = 270, Font = Theme.F(10), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 10, 6) };
            tbSearch.HandleCreated += (s, e) => Cue.Set(tbSearch, "Search number, title, supplier, type");
            debounce = new Timer { Interval = 250 };
            debounce.Tick += (s, e) => { debounce.Stop(); Reload(); };
            tbSearch.TextChanged += (s, e) => { debounce.Stop(); debounce.Start(); };
            flow.Controls.Add(tbSearch);

            // supplier
            cbSupplier = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10), Margin = new Padding(0, 3, 10, 6) };
            cbSupplier.Items.Add(AllSuppliers);
            foreach (var name in all.Select(x => x.Supplier).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v))
                cbSupplier.Items.Add(name);
            cbSupplier.SelectedIndex = 0;
            cbSupplier.SelectedIndexChanged += (s, e) => Reload();
            flow.Controls.Add(cbSupplier);

            // created date range (an unticked box means "no limit")
            dtFrom = MakeDate();
            dtTo = MakeDate();
            flow.Controls.Add(Captioned("Created from", dtFrom));
            flow.Controls.Add(Captioned("to", dtTo));

            chkMine = new CheckBox { Text = "Needs my action", AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.Ink2, Margin = new Padding(0, 6, 10, 6), BackColor = Color.Transparent };
            chkMine.CheckedChanged += (s, e) => Reload();
            flow.Controls.Add(chkMine);

            var clear = AppButton.Make("Clear filters", "ghost", null, 28, false, 100);
            clear.Margin = new Padding(0, 1, 0, 6);
            clear.Click += (s, e) => ClearFilters();
            flow.Controls.Add(clear);
            return flow;
        }

        private DateTimePicker MakeDate()
        {
            var d = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Font = Theme.F(9.5f), Margin = new Padding(0) };
            d.ValueChanged += (s, e) => Reload();
            return d;
        }

        private static Control Captioned(string caption, Control inner)
        {
            var lbl = new Label { Text = caption, AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(9), BackColor = Color.Transparent, UseMnemonic = false, Location = new Point(0, 6) };
            var host = new Panel { BackColor = Color.Transparent, Margin = new Padding(0, 3, 10, 6) };
            inner.Location = new Point(lbl.PreferredWidth + 6, 0);
            host.Controls.Add(lbl);
            host.Controls.Add(inner);
            host.Size = new Size(inner.Right, inner.Height + 2);
            return host;
        }

        private void ClearFilters()
        {
            debounce.Stop();
            tbSearch.Text = ""; cbSupplier.SelectedIndex = 0; chkMine.Checked = false;
            dtFrom.Checked = false; dtTo.Checked = false;
            Reload();
        }

        private bool FiltersActive
        {
            get
            {
                return filter != "" || tbSearch.Text.Trim().Length > 0 || cbSupplier.SelectedIndex > 0
                    || chkMine.Checked || dtFrom.Checked || dtTo.Checked;
            }
        }

        // Does this agreement pass the search box, supplier, date range and "needs me" filters? (the status tab is checked separately)
        private bool Matches(Agreement a)
        {
            foreach (var term in tbSearch.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string hay = (a.No + " " + a.Title + " " + a.Supplier + " " + a.Type + " " + a.CreatedBy);
                if (hay.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            if (cbSupplier.SelectedIndex > 0 && !string.Equals(a.Supplier, (string)cbSupplier.SelectedItem, StringComparison.OrdinalIgnoreCase)) return false;
            if (dtFrom.Checked && a.CreatedAt.Date < dtFrom.Value.Date) return false;
            if (dtTo.Checked && a.CreatedAt.Date > dtTo.Value.Date) return false;
            if (chkMine.Checked && !NeedsYou(a)) return false;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && debounce != null) debounce.Dispose();
            base.Dispose(disposing);
        }

        private void BuildGrid()
        {
            grid = new BufferedGrid
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = Line2,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 38,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.RowTemplate.Height = 58;

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = HeadBg,
                ForeColor = Theme.Ink3,
                Font = Theme.F(8, FontStyle.Bold),
                SelectionBackColor = HeadBg,
                SelectionForeColor = Theme.Ink3,
                Padding = new Padding(10, 0, 10, 0),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Theme.Ink,
                Font = Theme.F(9.5f),
                SelectionBackColor = HoverBg,
                SelectionForeColor = Theme.Ink,
                Padding = new Padding(10, 0, 10, 0),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };

            AddCol("AGREEMENT", 30, 0);
            AddCol("SUPPLIER", 20, 0);
            AddCol("VALUE", 20, 0);
            AddCol("APPROVAL STAGE", 30, 0);
            AddCol("STATUS", 0, 150);
            AddCol("", 0, 96);

            grid.Columns[0].MinimumWidth = 140;
            grid.Columns[1].MinimumWidth = 90;
            grid.Columns[2].MinimumWidth = 130;
            grid.Columns[3].MinimumWidth = 90;
            grid.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[2].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[2].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[3].DefaultCellStyle.Font = SmallF;
            grid.Columns[3].DefaultCellStyle.ForeColor = Theme.Ink2;

            grid.CellPainting += Grid_CellPainting;
            grid.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var h = OpenRequested;
                if (h != null) h((Agreement)grid.Rows[e.RowIndex].Tag);
            };
            grid.CellMouseEnter += (s, e) => { if (e.RowIndex >= 0) grid.Cursor = Cursors.Hand; };
            grid.CellMouseLeave += (s, e) => grid.Cursor = Cursors.Default;
        }

        private void AddCol(string header, float weight, int fixedWidth)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            if (fixedWidth > 0)
            {
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Width = fixedWidth;
            }
            else
            {
                col.FillWeight = weight;
            }
            grid.Columns.Add(col);
        }

        // ---------- fill the table for the selected tab
        private void Reload()
        {
            var rows = all.Where(a => (filter == "" || a.Status == filter) && Matches(a)).ToList();
            int needs = rows.Count(NeedsYou);

            lblEyebrow.Text = ((FiltersActive ? rows.Count + " of " + all.Count : rows.Count.ToString()) + " in " + Current.Entity.Code +
                               (needs > 0 ? " · " + needs + " need you" : "")).ToUpper();

            foreach (var t in tabs)
            {
                t.On = (string)t.Tag == filter;
                t.Invalidate();
            }

            grid.Rows.Clear();
            foreach (var a in rows)
            {
                int i = grid.Rows.Add("", a.Supplier, Fmt.Money(a.Value, a.Currency),
                                      AgreementRules.Stage(a), a.Status, NeedsYou(a) ? "Action" : "View");
                grid.Rows[i].Tag = a;
            }
            grid.Visible = rows.Count > 0;
            lblEmpty.Text = FiltersActive ? "No agreements match these filters." : "No agreements here yet.";
            lblEmpty.Visible = rows.Count == 0;
            grid.ClearSelection();
        }

        // ---------- custom drawing: two-line agreement cell, status badge, Action/View button
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex != 0 && e.ColumnIndex != 4 && e.ColumnIndex != 5) return;

            var a = (Agreement)grid.Rows[e.RowIndex].Tag;
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);

            var g = e.Graphics;
            var b = e.CellBounds;
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

            if (e.ColumnIndex == 0)
            {
                int x = b.Left + 10;
                int w = b.Width - 20;
                Size no = TextRenderer.MeasureText(g, a.No, BoldF, new Size(int.MaxValue, 20), flags);
                TextRenderer.DrawText(g, a.No, BoldF, new Rectangle(x, b.Top + 8, w, 20), Theme.Ink, flags);
                TextRenderer.DrawText(g, "  · " + a.Type, SmallF,
                    new Rectangle(x + no.Width, b.Top + 8, Math.Max(0, w - no.Width), 20), Theme.Ink2, flags);
                TextRenderer.DrawText(g, a.Title, SmallF, new Rectangle(x, b.Top + 29, w, 20), Theme.Ink2, flags);
            }
            else if (e.ColumnIndex == 4)
            {
                Color back, fore;
                BadgeColors(a.Status, out back, out fore);
                Size ts = TextRenderer.MeasureText(g, a.Status, BadgeF, new Size(int.MaxValue, 20), flags);
                var r = new Rectangle(b.Left + 10, b.Top + (b.Height - 24) / 2, ts.Width + 34, 24);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Gfx.RoundRect(r, 12))
                using (var br = new SolidBrush(back))
                    g.FillPath(br, path);
                using (var dot = new SolidBrush(fore))
                    g.FillEllipse(dot, r.Left + 11, r.Top + 9, 6, 6);
                TextRenderer.DrawText(g, a.Status, BadgeF, new Rectangle(r.Left + 22, r.Top, r.Width - 22, r.Height), fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            else
            {
                bool action = Convert.ToString(grid.Rows[e.RowIndex].Cells[5].Value) == "Action";
                var r = new Rectangle(b.Right - 12 - 84, b.Top + (b.Height - 30) / 2, 84, 30);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Gfx.RoundRect(r, 8))
                using (var br = new SolidBrush(action ? Theme.Brand2 : Color.White))
                using (var pen = new Pen(action ? Theme.Brand2 : Theme.Line))
                {
                    g.FillPath(br, path);
                    g.DrawPath(pen, path);
                }
                Color fc = action ? Color.White : Theme.Brand;
                Icons.Draw(g, action ? "arrow" : "eye", new Rectangle(r.Left + 11, r.Top + 7, 16, 16), fc);
                TextRenderer.DrawText(g, action ? "Action" : "View", BtnF, new Rectangle(r.Left + 30, r.Top, r.Width - 32, r.Height), fc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            e.Handled = true;
        }
    }
}