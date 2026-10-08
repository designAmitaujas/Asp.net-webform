using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // One invoice, read-only: header facts, the milestone it is against, and the amounts
    public class InvoiceDetailsPage : Panel
    {
        public event EventHandler BackRequested;
        public event Action<Invoice> EditRequested;
        public event Action<int> OpenPoRequested;   // PO id

        private Invoice inv;

        public InvoiceDetailsPage(Invoice invoice, string flash)
        {
            inv = invoice;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build(flash);
        }

        private static Label Cap(string text, int x, int y) { return PoUi.L(text, Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y); }

        private static Control Banner(string text, Color back, Color fore, Color line)
        {
            var n = new CardPanel { Height = 52, BackColor = back, BorderColor = line, Padding = new Padding(1) };
            n.Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 10, 0), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(9.5f, FontStyle.Bold), ForeColor = fore, BackColor = back, UseMnemonic = false, Text = text });
            return n;
        }

        private void Build(string flash)
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            Controls.Add(scroll);

            // ---- heading with buttons
            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            head.Controls.Add(PoUi.L("INVOICE", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2));
            var title = PoUi.L(inv.No, Theme.F(16, FontStyle.Bold), Theme.Ink, 0, 20);
            head.Controls.Add(title);
            Color back, fore;
            PoUi.StatusColors(inv.Status == "Paid" ? "Approved" : inv.Status, out back, out fore);
            var pill = new Label { Text = inv.Status, Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = fore, BackColor = back, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Height = 24 };
            pill.Width = TextRenderer.MeasureText(inv.Status, pill.Font).Width + 24;
            pill.Location = new Point(title.PreferredWidth + 14, 25);
            head.Controls.Add(pill);

            var bBack = AppButton.Make("Back to list", "ghost", "back", 32, false, 110);
            bBack.Click += (s, e) => { if (BackRequested != null) BackRequested(this, EventArgs.Empty); };
            head.Controls.Add(bBack);
            AppButton bEdit = null;
            if (inv.IsEditable && Perm.Can("invoice.book"))
            {
                bEdit = AppButton.Make("Edit", "primary", "edit", 32, false, 90);
                bEdit.Click += (s, e) => { if (EditRequested != null) EditRequested(inv); };
                head.Controls.Add(bEdit);
            }
            head.Resize += (s, e) =>
            {
                bBack.Location = new Point(head.Width - bBack.Width, 6);
                if (bEdit != null) bEdit.Location = new Point(bBack.Left - 8 - bEdit.Width, 6);
            };

            // ---- messages
            Control notice = PoUi.Gap(0);
            if (!string.IsNullOrEmpty(flash)) notice = Banner(flash, Theme.GoodSoft, Theme.Good, Theme.Good);
            Control status = PoUi.Gap(0);
            if (inv.Status == "Draft")
                status = Banner("Draft. It is not booked yet: the duplicate check, the milestone balance check and the currency conversion are applied when you book it (next step).", Theme.WarnSoft, Theme.Warn, Theme.WarnLine);
            else if (inv.Status == "Cancelled")
                status = Banner("Cancelled by " + inv.CancelledBy + ": " + inv.CancelReason, Theme.BadSoft, Theme.Bad, Theme.BadLine);

            PoUi.Stack(scroll, head, notice, PoUi.Gap(notice.Height > 0 ? 12 : 0), status, PoUi.Gap(status.Height > 0 ? 12 : 0),
                BuildInfoCard(), PoUi.Gap(14), BuildAmountsCard(), PoUi.Gap(20));
        }

        private Control BuildInfoCard()
        {
            var card = new CardPanel { Height = 238 };
            string due = inv.DueDate.HasValue ? PoUi.Date(inv.DueDate) : "-";
            string[,] facts =
            {
                { "VENDOR INVOICE NO.", inv.VendorInvoiceNo }, { "VENDOR", inv.VendorName }, { "PURCHASE ORDER", inv.PoNo + "  \u00B7  " + inv.PoTitle },
                { "MILESTONE", "#" + inv.MilestoneSeq + "  " + inv.MilestoneDesc }, { "MILESTONE AMOUNT / BALANCE", Fmt.Money(inv.MilestoneAmount, inv.Currency) + "  /  " + Fmt.Money(inv.MilestoneBalance, inv.Currency) }, { "CURRENCY", inv.Currency },
                { "INVOICE DATE", PoUi.Date(inv.InvoiceDate) }, { "DUE DATE", due }, { "INVOICE COPY", string.IsNullOrWhiteSpace(inv.FileRef) ? "-" : inv.FileRef },
                { "ENTERED BY", inv.CreatedBy + "  \u00B7  " + inv.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) },
                { "LAST CHANGED", inv.UpdatedAt.HasValue ? inv.UpdatedBy + "  \u00B7  " + inv.UpdatedAt.Value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) : "-" },
                { "REMARKS", string.IsNullOrWhiteSpace(inv.Remarks) ? "-" : inv.Remarks }
            };
            for (int i = 0; i < 12; i++)
            {
                int col = i % 3, row = i / 3;
                int x = 18 + col * 330, y = 16 + row * 54;
                card.Controls.Add(Cap(facts[i, 0], x, y));
                var v = PoUi.L(facts[i, 1], Theme.F(9.5f, FontStyle.Bold), Theme.Ink, x, y + 16);
                v.MaximumSize = new Size(310, 0);
                if (facts[i, 0] == "PURCHASE ORDER")
                {
                    v.ForeColor = Theme.Brand; v.Cursor = Cursors.Hand;
                    v.Click += (s, e) => { if (OpenPoRequested != null) OpenPoRequested(inv.PoId); };
                }
                else if (facts[i, 0] == "INVOICE COPY" && !string.IsNullOrWhiteSpace(inv.FileRef))
                {
                    v.ForeColor = Theme.Brand; v.Cursor = Cursors.Hand;
                    v.Click += (s, e) => OpenFileRef();
                }
                card.Controls.Add(v);
            }
            return card;
        }

        private void OpenFileRef()
        {
            string u = (inv.FileRef ?? "").Trim();
            try
            {
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Process.Start(u);
                else MessageBox.Show("Location:\r\n\r\n" + u, "Invoice copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Could not open it: " + ex.Message, "Invoice copy", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private Control BuildAmountsCard()
        {
            var card = new CardPanel { Height = 196 };
            card.Controls.Add(PoUi.L("Amounts", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));
            string ccy = inv.Currency;
            bool foreign = !string.Equals(ccy, Current.Entity.Currency, StringComparison.OrdinalIgnoreCase);

            Action<string, string, int, int, bool> cell = (cap, val, x, y, big) =>
            {
                card.Controls.Add(Cap(cap, x, y));
                card.Controls.Add(PoUi.L(val, Theme.F(big ? 12 : 10.5f, FontStyle.Bold), Theme.Ink, x, y + 18));
            };
            cell("TAXABLE VALUE", Fmt.Money(inv.Taxable, ccy), 18, 48, false);
            cell("GST  (" + inv.GstPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%)", Fmt.Money(inv.GstAmount, ccy), 250, 48, false);
            cell("INVOICE TOTAL", Fmt.Money(inv.Total, ccy), 480, 48, true);

            string tdsCap = inv.TdsPercent > 0 ? "TDS  (" + (string.IsNullOrEmpty(inv.TdsSection) ? "" : inv.TdsSection + ", ") + inv.TdsPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% of taxable)" : "TDS";
            cell(tdsCap, Fmt.Money(inv.TdsAmount, ccy), 18, 112, false);
            cell("NET PAYABLE TO VENDOR", Fmt.Money(inv.NetPayable, ccy), 480, 112, true);

            if (foreign)
            {
                cell("EXCHANGE RATE", "1 " + ccy + " = " + inv.ExchangeRate.ToString("0.######", CultureInfo.InvariantCulture) + " " + Current.Entity.Currency, 250, 112, false);
                cell("TOTAL IN " + Current.Entity.Currency, Fmt.Money(inv.BaseTotal, Current.Entity.Currency), 730, 112, false);
            }
            return card;
        }
    }
}
