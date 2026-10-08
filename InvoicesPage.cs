using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // The list of invoices: status tabs, search, one row per invoice
    public class InvoicesPage : Panel
    {
        public event Action<Invoice> OpenRequested;
        public event EventHandler NewRequested;

        private readonly string[] statuses = { "", "Draft", "Pending Approval", "Approved", "Rejected", "Cancelled", "Paid" };
        private string filter = "";
        private List<Invoice> all = new List<Invoice>();
        private Label lblEyebrow, lblEmpty;
        private BufferedGrid grid;
        private TextBox tbSearch;
        private readonly List<PillTab> tabs = new List<PillTab>();

        public InvoicesPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Reload();
        }

        private void Build()
        {
            try { all = Db.LoadInvoices(Current.Entity.Code); }
            catch (Exception ex) { MessageBox.Show("Could not load the invoices:\r\n\r\n" + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); }

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            grid = PoUi.MakeGrid(48, false);
            grid.Columns.Add(PoUi.Col("BOOKING NO.", 18, 0, false, true));
            grid.Columns.Add(PoUi.Col("VENDOR INVOICE / VENDOR", 36, 0, false, true));
            grid.Columns.Add(PoUi.Col("PO / MILESTONE", 24, 0, false, true));
            grid.Columns.Add(PoUi.Col("INVOICE DATE", 0, 120, false, true));
            grid.Columns.Add(PoUi.Col("TOTAL", 0, 150, true, true));
            grid.Columns.Add(PoUi.Col("STATUS", 0, 140, false, true));
            foreach (DataGridViewColumn c in grid.Columns) c.DefaultCellStyle.BackColor = Color.White;
            grid.Columns[0].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[4].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[5].DefaultCellStyle.Font = Theme.F(9, FontStyle.Bold);
            grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && OpenRequested != null) OpenRequested((Invoice)grid.Rows[e.RowIndex].Tag); };
            grid.CellMouseEnter += (s, e) => { if (e.RowIndex >= 0) grid.Cursor = Cursors.Hand; };
            grid.CellMouseLeave += (s, e) => grid.Cursor = Cursors.Default;

            lblEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Ink3, BackColor = Color.White, Visible = false };
            card.Controls.Add(grid);
            card.Controls.Add(lblEmpty);
            Controls.Add(card);

            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            lblEyebrow = PoUi.L("", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2);
            head.Controls.Add(lblEyebrow);
            head.Controls.Add(PoUi.L("Invoice booking", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));
            if (Perm.Can("invoice.book"))
            {
                var btn = AppButton.Make("New invoice", "primary", "plus", 36);
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
                Text = "An invoice is booked against one payment milestone of an approved purchase order. Vendor and currency come from the PO."
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

            tbSearch = new TextBox { Width = 340, Font = Theme.F(10), BorderStyle = BorderStyle.FixedSingle };
            tbSearch.HandleCreated += (s, e) => Cue.Set(tbSearch, "Search booking no., vendor invoice no., vendor, PO");
            tbSearch.TextChanged += (s, e) => Reload();
            var searchRow = new Panel { Height = 36, BackColor = Color.Transparent };
            tbSearch.Location = new Point(0, 3);
            searchRow.Controls.Add(tbSearch);

            PoUi.Stack(this, head, notice, PoUi.Gap(14), tabRow, searchRow, PoUi.Gap(6));
        }

        private bool Matches(Invoice i)
        {
            foreach (var term in tbSearch.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if ((i.No + " " + i.VendorInvoiceNo + " " + i.VendorName + " " + i.PoNo + " " + i.PoTitle).IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void Reload()
        {
            var rows = all.Where(i => (filter == "" || i.Status == filter) && Matches(i)).ToList();
            bool filtered = filter != "" || tbSearch.Text.Trim().Length > 0;
            lblEyebrow.Text = ((filtered ? rows.Count + " of " + all.Count : rows.Count.ToString()) + " in " + Current.Entity.Code).ToUpper();

            foreach (var t in tabs) { t.On = (string)t.Tag == filter; t.Invalidate(); }

            grid.Rows.Clear();
            foreach (var i in rows)
            {
                int r = grid.Rows.Add(i.No, i.VendorInvoiceNo + "  \u00B7  " + i.VendorName, i.PoNo + "  \u00B7  #" + i.MilestoneSeq + " " + i.MilestoneDesc,
                    i.InvoiceDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), Fmt.Money(i.Total, i.Currency), i.Status);
                grid.Rows[r].Tag = i;
                Color back, fore;
                PoUi.StatusColors(i.Status == "Paid" ? "Approved" : i.Status, out back, out fore);
                grid.Rows[r].Cells[5].Style.ForeColor = fore;
            }
            grid.Visible = rows.Count > 0;
            lblEmpty.Text = filtered ? "No invoices match these filters." : "No invoices yet. Click New invoice to book one against an approved purchase order.";
            lblEmpty.Visible = rows.Count == 0;
            grid.ClearSelection();
        }
    }
}
