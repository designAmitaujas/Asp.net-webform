using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // Small helpers shared by the three purchase-order screens
    internal static class PoUi
    {
        public static Label L(string text, Font font, Color color, int x, int y)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = true, UseMnemonic = false, Location = new Point(x, y) };
        }

        // Stacks controls top to bottom (WinForms docks the LAST added control highest, so we add in reverse)
        public static void Stack(Control parent, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--) { topToBottom[i].Dock = DockStyle.Top; parent.Controls.Add(topToBottom[i]); }
        }

        public static Panel Gap(int h) { return new Panel { Height = h, BackColor = Color.Transparent }; }

        public static void StatusColors(string status, out Color back, out Color fore)
        {
            switch (status)
            {
                case "Approved": back = Theme.GoodSoft; fore = Theme.Good; break;
                case "Pending Approval": back = Theme.WarnSoft; fore = Theme.Warn; break;
                case "Rejected":
                case "Cancelled": back = Theme.BadSoft; fore = Theme.Bad; break;
                default: back = ColorTranslator.FromHtml("#E9EEF0"); fore = Theme.Ink2; break;
            }
        }

        // A grid in the same style as the other lists. editable = cells can be typed into.
        public static BufferedGrid MakeGrid(int rowHeight, bool editable)
        {
            var g = new BufferedGrid
            {
                Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                ReadOnly = !editable, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = editable ? DataGridViewSelectionMode.CellSelect : DataGridViewSelectionMode.FullRowSelect,
                EditMode = editable ? DataGridViewEditMode.EditOnEnter : DataGridViewEditMode.EditProgrammatically,
                EnableHeadersVisualStyles = false, GridColor = Theme.Line2, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 36,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            g.RowTemplate.Height = rowHeight;
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Hover, ForeColor = Theme.Ink3, Font = Theme.F(8, FontStyle.Bold),
                SelectionBackColor = Theme.Hover, SelectionForeColor = Theme.Ink3, Padding = new Padding(8, 0, 8, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            g.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White, ForeColor = Theme.Ink, Font = Theme.F(9.5f),
                SelectionBackColor = editable ? Color.White : Theme.Hover, SelectionForeColor = Theme.Ink, Padding = new Padding(8, 0, 8, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            return g;
        }

        public static DataGridViewTextBoxColumn Col(string header, float weight, int fixedWidth, bool right, bool readOnly)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable, ReadOnly = readOnly };
            if (fixedWidth > 0) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; c.Width = fixedWidth; } else c.FillWeight = weight;
            if (right)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            if (readOnly) c.DefaultCellStyle.BackColor = Theme.Hover;
            return c;
        }

        // "1,00,000.50" -> 100000.50 ; blank or not a number -> null
        public static decimal? Dec(object v)
        {
            string s = Convert.ToString(v);
            if (s == null) return null;
            s = s.Replace(",", "").Trim();
            decimal d;
            return s.Length > 0 && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d) ? (decimal?)d : null;
        }

        private static readonly string[] DateFormats = { "d MMM yyyy", "dd MMM yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd" };

        public static bool TryDate(string s, out DateTime d)
        {
            return DateTime.TryParseExact((s ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        public static string Date(DateTime? d) { return d.HasValue ? d.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : ""; }
        public static string N2(decimal v) { return v.ToString("N2", CultureInfo.InvariantCulture); }
    }

    // The list of purchase orders: status tabs, search, one row per PO
    public class PurchaseOrdersPage : Panel
    {
        public event Action<PurchaseOrder> OpenRequested;
        public event EventHandler NewRequested;

        private readonly string[] statuses = { "", "Draft", "Pending Approval", "Approved", "Rejected", "Cancelled", "Closed" };
        private string filter = "";
        private List<PurchaseOrder> all;
        private Label lblEyebrow, lblEmpty;
        private BufferedGrid grid;
        private TextBox tbSearch;
        private readonly List<PillTab> tabs = new List<PillTab>();

        public PurchaseOrdersPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Reload();
        }

        private void Build()
        {
            all = Db.LoadPos(Current.Entity.Code);

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            grid = PoUi.MakeGrid(48, false);
            grid.Columns.Add(PoUi.Col("PO NUMBER", 20, 0, false, true));
            grid.Columns.Add(PoUi.Col("TITLE / VENDOR", 38, 0, false, true));
            grid.Columns.Add(PoUi.Col("AGREEMENT", 20, 0, false, true));
            grid.Columns.Add(PoUi.Col("PO DATE", 0, 110, false, true));
            grid.Columns.Add(PoUi.Col("TOTAL", 0, 150, true, true));
            grid.Columns.Add(PoUi.Col("STATUS", 0, 140, false, true));
            foreach (DataGridViewColumn c in grid.Columns) c.DefaultCellStyle.BackColor = Color.White;
            grid.Columns[0].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[4].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[5].DefaultCellStyle.Font = Theme.F(9, FontStyle.Bold);
            grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && OpenRequested != null) OpenRequested((PurchaseOrder)grid.Rows[e.RowIndex].Tag); };
            grid.CellMouseEnter += (s, e) => { if (e.RowIndex >= 0) grid.Cursor = Cursors.Hand; };
            grid.CellMouseLeave += (s, e) => grid.Cursor = Cursors.Default;

            lblEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Ink3, BackColor = Color.White, Visible = false };
            card.Controls.Add(grid);
            card.Controls.Add(lblEmpty);
            Controls.Add(card);

            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            lblEyebrow = PoUi.L("", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2);
            head.Controls.Add(lblEyebrow);
            head.Controls.Add(PoUi.L("Purchase orders", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));
            if (Perm.Can("po.create"))
            {
                var btn = AppButton.Make("New purchase order", "primary", "plus", 36);
                btn.Font = Theme.F(9.5f, FontStyle.Bold);
                btn.Fit(36);
                btn.Click += (s, e) => { if (NewRequested != null) NewRequested(this, EventArgs.Empty); };
                head.Controls.Add(btn);
                head.Resize += (s, e) => btn.Location = new Point(head.Width - btn.Width, 6);
            }

            var notice = new CardPanel { Height = 50, BackColor = Theme.BrandSoft, BorderColor = Theme.BrandLine, Padding = new Padding(1) };
            var nl = new Label
            {
                Dock = DockStyle.Fill, Padding = new Padding(46, 0, 14, 0), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(9.5f), ForeColor = Theme.Brand,
                BackColor = Theme.BrandSoft, UseMnemonic = false,
                Text = "A purchase order is raised only against a signed agreement, and cannot go beyond what is left on it. Payment milestones must add up to the PO total."
            };
            nl.Paint += (s, e) => Icons.Draw(e.Graphics, "info", new Rectangle(15, 15, 18, 18), Theme.Brand2);
            notice.Controls.Add(nl);

            var tabRow = new Panel { Height = 42, BackColor = Color.Transparent };
            int x = 0;
            foreach (var st in statuses)
            {
                var tab = new PillTab(st == "" ? "All" : st) { Tag = st, Location = new Point(x, 6) };
                tab.Click += (s, e) => { filter = (string)((PillTab)s).Tag; Reload(); };
                tabRow.Controls.Add(tab);
                tabs.Add(tab);
                x += tab.Width + 6;
            }

            tbSearch = new TextBox { Width = 300, Font = Theme.F(10), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 6) };
            tbSearch.HandleCreated += (s, e) => Cue.Set(tbSearch, "Search PO number, title, vendor, agreement");
            tbSearch.TextChanged += (s, e) => Reload();
            var searchRow = new Panel { Height = 36, BackColor = Color.Transparent };
            tbSearch.Location = new Point(0, 3);
            searchRow.Controls.Add(tbSearch);

            PoUi.Stack(this, head, notice, PoUi.Gap(14), tabRow, searchRow, PoUi.Gap(6));
        }

        private bool Matches(PurchaseOrder p)
        {
            foreach (var term in tbSearch.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if ((p.No + " " + p.Title + " " + p.VendorName + " " + p.AgreementNo).IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void Reload()
        {
            var rows = all.Where(p => (filter == "" || p.Status == filter) && Matches(p)).ToList();
            bool filtered = filter != "" || tbSearch.Text.Trim().Length > 0;
            lblEyebrow.Text = ((filtered ? rows.Count + " of " + all.Count : rows.Count.ToString()) + " in " + Current.Entity.Code).ToUpper();

            foreach (var t in tabs) { t.On = (string)t.Tag == filter; t.Invalidate(); }

            grid.Rows.Clear();
            foreach (var p in rows)
            {
                int i = grid.Rows.Add(p.No, p.Title + "  ·  " + p.VendorName, p.AgreementNo, p.PoDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), Fmt.Money(p.Total, p.Currency), p.Status);
                grid.Rows[i].Tag = p;
                Color back, fore;
                PoUi.StatusColors(p.Status, out back, out fore);
                grid.Rows[i].Cells[5].Style.ForeColor = fore;
            }
            grid.Visible = rows.Count > 0;
            lblEmpty.Text = filtered ? "No purchase orders match these filters." : "No purchase orders yet. Click New purchase order to raise one against a signed agreement.";
            lblEmpty.Visible = rows.Count == 0;
            grid.ClearSelection();
        }
    }
}
