using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // Book or edit an invoice (a draft). Amounts are shown live; the real rules are checked again by Db.SaveInvoice.
    public class InvoiceFormPage : Panel
    {
        public event Action<Invoice, string> Saved;
        public event EventHandler Cancelled;

        private class PoChoice
        {
            public PurchaseOrder Po;
            public override string ToString() { return Po.No + "   \u00B7   " + Po.VendorName + "   \u00B7   " + Po.Title; }
        }

        private class MsChoice
        {
            public PoMilestone M; public string Ccy;
            public override string ToString() { return "#" + M.SeqNo + "  " + M.Description + "   \u00B7   balance " + Fmt.Money(M.Balance, Ccy); }
        }

        private static readonly string[] TdsSections = { "", "194C", "194J", "194Q", "194H", "194I", "194A" };

        private readonly Invoice existing;          // null = new invoice
        private readonly string baseCcy;            // the entity's own currency
        private ComboBox cbPo, cbMs, cbTds;
        private Label lblVendor, lblCcy, vMsAmt, vMsInv, vMsBal, capRate, lblCheck, lblErr;
        private Label vGst, vTotal, vTds, vNet, vBase;
        private TextBox tbVin, tbFile, tbRemarks, tbTaxable, tbGst, tbTdsPct, tbRate;
        private DateTimePicker dtInv, dtDue;
        private PurchaseOrder po;                   // the selected PO
        private PoMilestone ms;                     // the selected milestone
        private bool loading;

        public InvoiceFormPage(Invoice inv)
        {
            existing = inv;
            baseCcy = Current.Entity.Currency;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Fill();
        }

        private static Label Cap(string text, int x, int y) { return PoUi.L(text, Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y); }

        private static TextBox Tb(Control parent, int x, int y, int w, int max, bool right = false)
        {
            var t = new TextBox { Location = new Point(x, y), Width = w, Font = Theme.F(10), MaxLength = max, BorderStyle = BorderStyle.FixedSingle };
            if (right) t.TextAlign = HorizontalAlignment.Right;
            parent.Controls.Add(t);
            return t;
        }

        // ---------- layout
        private void Build()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.White };
            bar.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
            lblErr = new Label { AutoSize = false, Location = new Point(16, 8), Height = 48, ForeColor = Theme.Bad, Font = Theme.F(9.5f, FontStyle.Bold), UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            var bSave = AppButton.Make(existing == null ? "Save draft" : "Save changes", "primary", "check", 38, false, 130);
            var bCancel = AppButton.Make("Cancel", "ghost", "close", 38, false, 96);
            bSave.Click += (s, e) => Save();
            bCancel.Click += (s, e) => { if (Cancelled != null) Cancelled(this, EventArgs.Empty); };
            bar.Controls.AddRange(new Control[] { lblErr, bSave, bCancel });
            bar.Resize += (s, e) =>
            {
                bSave.Location = new Point(bar.Width - bSave.Width - 16, 13);
                bCancel.Location = new Point(bSave.Left - bCancel.Width - 8, 13);
                lblErr.Width = Math.Max(100, bCancel.Left - 32);
            };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            Controls.Add(scroll);
            Controls.Add(bar);

            var head = new Panel { Height = 56, BackColor = Color.Transparent };
            head.Controls.Add(PoUi.L(existing == null ? "NEW INVOICE" : "EDIT " + existing.No, Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2));
            head.Controls.Add(PoUi.L(existing == null ? "Book an invoice" : "Edit invoice", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));

            PoUi.Stack(scroll, head, BuildPoCard(), PoUi.Gap(14), BuildDetailsCard(), PoUi.Gap(14), BuildAmountsCard(), PoUi.Gap(20));
        }

        private Control BuildPoCard()
        {
            var card = new CardPanel { Height = 176 };
            card.Controls.Add(PoUi.L("Purchase order and milestone", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));

            card.Controls.Add(Cap("PURCHASE ORDER *", 18, 48));
            cbPo = new ComboBox { Location = new Point(18, 66), Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10) };
            cbPo.SelectedIndexChanged += (s, e) => { if (!loading) PoChanged(); };
            card.Controls.Add(cbPo);

            card.Controls.Add(Cap("MILESTONE *", 480, 48));
            cbMs = new ComboBox { Location = new Point(480, 66), Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10) };
            cbMs.SelectedIndexChanged += (s, e) => { if (!loading) MsChanged(); };
            card.Controls.Add(cbMs);

            card.Controls.Add(Cap("VENDOR", 18, 114));
            lblVendor = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 18, 130);
            lblVendor.MaximumSize = new Size(290, 0);
            card.Controls.Add(lblVendor);
            card.Controls.Add(Cap("CURRENCY", 330, 114));
            lblCcy = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 330, 130);
            card.Controls.Add(lblCcy);
            card.Controls.Add(Cap("MILESTONE AMOUNT", 440, 114));
            vMsAmt = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 440, 130);
            card.Controls.Add(vMsAmt);
            card.Controls.Add(Cap("ALREADY INVOICED", 620, 114));
            vMsInv = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 620, 130);
            card.Controls.Add(vMsInv);
            card.Controls.Add(Cap("BALANCE", 800, 114));
            vMsBal = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Good, 800, 130);
            card.Controls.Add(vMsBal);
            return card;
        }

        private Control BuildDetailsCard()
        {
            var card = new CardPanel { Height = 186 };
            card.Controls.Add(PoUi.L("Invoice details", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));

            card.Controls.Add(Cap("VENDOR INVOICE NO. *", 18, 48));
            tbVin = Tb(card, 18, 66, 280, 50);

            card.Controls.Add(Cap("INVOICE DATE *", 318, 48));
            dtInv = new DateTimePicker { Location = new Point(318, 66), Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", Font = Theme.F(10) };
            card.Controls.Add(dtInv);

            card.Controls.Add(Cap("DUE DATE", 508, 48));
            dtDue = new DateTimePicker { Location = new Point(508, 66), Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", ShowCheckBox = true, Checked = false, Font = Theme.F(10) };
            card.Controls.Add(dtDue);

            card.Controls.Add(Cap("INVOICE COPY  (link or network path)", 18, 112));
            tbFile = Tb(card, 18, 130, 470, 500);
            card.Controls.Add(Cap("REMARKS", 508, 112));
            tbRemarks = Tb(card, 508, 130, 412, 1000);
            return card;
        }

        private Control BuildAmountsCard()
        {
            var card = new CardPanel { Height = 282 };
            card.Controls.Add(PoUi.L("Amounts", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));

            // row 1: taxable value, GST
            card.Controls.Add(Cap("TAXABLE VALUE *  (before GST)", 18, 48));
            tbTaxable = Tb(card, 18, 66, 170, 18, true);
            var bFill = AppButton.Make("Use milestone balance", "ghost", null, 26, false, 170);
            bFill.Location = new Point(200, 67);
            bFill.Click += (s, e) => FillFromBalance();
            card.Controls.Add(bFill);
            card.Controls.Add(Cap("GST %", 400, 48));
            tbGst = Tb(card, 400, 66, 80, 6, true);
            card.Controls.Add(Cap("GST AMOUNT", 500, 48));
            vGst = PoUi.L("-", Theme.F(10.5f, FontStyle.Bold), Theme.Ink, 500, 68);
            card.Controls.Add(vGst);
            card.Controls.Add(Cap("INVOICE TOTAL", 700, 48));
            vTotal = PoUi.L("-", Theme.F(12, FontStyle.Bold), Theme.Ink, 700, 66);
            card.Controls.Add(vTotal);

            // row 2: TDS
            card.Controls.Add(Cap("TDS SECTION", 18, 118));
            cbTds = new ComboBox { Location = new Point(18, 136), Width = 170, DropDownStyle = ComboBoxStyle.DropDown, Font = Theme.F(10), MaxLength = 20 };
            cbTds.Items.AddRange(TdsSections);
            card.Controls.Add(cbTds);
            card.Controls.Add(Cap("TDS %", 400, 118));
            tbTdsPct = Tb(card, 400, 136, 80, 6, true);
            card.Controls.Add(Cap("TDS AMOUNT  (on taxable value)", 500, 118));
            vTds = PoUi.L("-", Theme.F(10.5f, FontStyle.Bold), Theme.Ink, 500, 138);
            card.Controls.Add(vTds);
            card.Controls.Add(Cap("NET PAYABLE", 700, 118));
            vNet = PoUi.L("-", Theme.F(12, FontStyle.Bold), Theme.Ink, 700, 136);
            card.Controls.Add(vNet);

            // row 3: currency conversion
            capRate = Cap("EXCHANGE RATE", 18, 188);
            card.Controls.Add(capRate);
            tbRate = Tb(card, 18, 206, 170, 14, true);
            card.Controls.Add(Cap("INVOICE TOTAL IN " + baseCcy, 700, 188));
            vBase = PoUi.L("-", Theme.F(12, FontStyle.Bold), Theme.Ink, 700, 206);
            card.Controls.Add(vBase);

            lblCheck = new Label { AutoSize = false, Location = new Point(18, 244), Size = new Size(900, 28), Font = Theme.F(9.5f, FontStyle.Bold), UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            card.Controls.Add(lblCheck);

            tbTaxable.TextChanged += (s, e) => Recalc();
            tbGst.TextChanged += (s, e) => Recalc();
            tbTdsPct.TextChanged += (s, e) => Recalc();
            tbRate.TextChanged += (s, e) => Recalc();
            return card;
        }

        // ---------- fill the form
        private void Fill()
        {
            loading = true;
            string entity = Current.Entity.Code;
            try
            {
                if (existing == null)
                {
                    foreach (var p in Db.PosOpenForInvoice(entity)) cbPo.Items.Add(new PoChoice { Po = p });
                    dtInv.Value = DateTime.Today;
                    tbGst.Text = entity == "IN" ? "18" : "0";
                    tbTdsPct.Text = "0";
                }
                else
                {
                    var p = Db.LoadPos(entity).FirstOrDefault(x => x.Id == existing.PoId);
                    if (p != null) cbPo.Items.Add(new PoChoice { Po = p });
                    cbPo.Enabled = false;   // an invoice stays with the PO it was booked against
                    tbVin.Text = existing.VendorInvoiceNo;
                    dtInv.Value = existing.InvoiceDate;
                    if (existing.DueDate.HasValue) { dtDue.Value = existing.DueDate.Value; dtDue.Checked = true; }
                    tbFile.Text = existing.FileRef;
                    tbRemarks.Text = existing.Remarks;
                    tbTaxable.Text = PoUi.N2(existing.Taxable);
                    tbGst.Text = existing.GstPercent.ToString("0.##", CultureInfo.InvariantCulture);
                    cbTds.Text = existing.TdsSection;
                    tbTdsPct.Text = existing.TdsPercent.ToString("0.##", CultureInfo.InvariantCulture);
                    tbRate.Text = existing.ExchangeRate.ToString("0.######", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) { lblErr.Text = "Could not load the purchase orders: " + ex.Message; }
            if (cbPo.Items.Count > 0) cbPo.SelectedIndex = 0;
            loading = false;
            PoChanged();
        }

        private void PoChanged()
        {
            var ch = cbPo.SelectedItem as PoChoice;
            po = ch == null ? null : ch.Po;

            loading = true;
            cbMs.Items.Clear();
            if (po != null)
            {
                int keep = existing != null ? existing.MilestoneId : 0;
                foreach (var m in po.Milestones.Where(q => q.Balance > 0 || q.Id == keep))
                    cbMs.Items.Add(new MsChoice { M = m, Ccy = po.Currency });
                for (int i = 0; i < cbMs.Items.Count; i++)
                    if (keep != 0 && ((MsChoice)cbMs.Items[i]).M.Id == keep) cbMs.SelectedIndex = i;
                if (cbMs.SelectedIndex < 0 && cbMs.Items.Count > 0) cbMs.SelectedIndex = 0;
            }
            loading = false;

            if (po == null) { lblVendor.Text = lblCcy.Text = ""; }
            else
            {
                lblVendor.Text = po.VendorName;
                lblCcy.Text = po.Currency;
                bool same = string.Equals(po.Currency, baseCcy, StringComparison.OrdinalIgnoreCase);
                capRate.Text = same ? "EXCHANGE RATE  (not needed: same currency)" : "EXCHANGE RATE *  (1 " + po.Currency + " = ? " + baseCcy + ")";
                tbRate.Enabled = !same;
                if (same) tbRate.Text = "1";
                else if (existing == null || existing.ExchangeRate == 1m) tbRate.Text = "";
            }
            MsChanged();
        }

        private void MsChanged()
        {
            var ch = cbMs.SelectedItem as MsChoice;
            ms = ch == null ? null : ch.M;
            if (ms == null || po == null) { vMsAmt.Text = vMsInv.Text = vMsBal.Text = ""; Recalc(); return; }
            vMsAmt.Text = Fmt.Money(ms.Amount, po.Currency);
            vMsInv.Text = Fmt.Money(ms.InvoicedAmount, po.Currency);
            vMsBal.Text = Fmt.Money(ms.Balance, po.Currency);
            Recalc();
        }

        // Sets the taxable value so that taxable + GST equals what is left on the milestone
        private void FillFromBalance()
        {
            if (ms == null) { lblErr.Text = "Choose a milestone first."; return; }
            decimal? gst = string.IsNullOrWhiteSpace(tbGst.Text) ? 0m : PoUi.Dec(tbGst.Text);
            if (!gst.HasValue) { lblErr.Text = "GST % must be a number."; return; }
            lblErr.Text = "";
            tbTaxable.Text = PoUi.N2(Invoice.Round(ms.Balance / (1m + gst.Value / 100m)));
        }

        // ---------- live figures
        private void Recalc()
        {
            if (vGst == null || vTotal == null || lblCheck == null) return;
            string ccy = po == null ? "" : po.Currency;
            decimal? taxable = PoUi.Dec(tbTaxable.Text);
            decimal? gst = string.IsNullOrWhiteSpace(tbGst.Text) ? 0m : PoUi.Dec(tbGst.Text);
            decimal? tds = string.IsNullOrWhiteSpace(tbTdsPct.Text) ? 0m : PoUi.Dec(tbTdsPct.Text);
            decimal? rate = tbRate.Enabled ? PoUi.Dec(tbRate.Text) : (decimal?)1m;

            lblCheck.Text = "";
            if (!taxable.HasValue || !gst.HasValue || !tds.HasValue)
            {
                vGst.Text = vTotal.Text = vTds.Text = vNet.Text = vBase.Text = "-";
                return;
            }
            var t = new Invoice { Taxable = taxable.Value, GstPercent = gst.Value, TdsPercent = tds.Value, ExchangeRate = rate.HasValue && rate.Value > 0 ? rate.Value : 1m };
            t.Recalculate();
            vGst.Text = Fmt.Money(t.GstAmount, ccy);
            vTotal.Text = Fmt.Money(t.Total, ccy);
            vTds.Text = Fmt.Money(t.TdsAmount, ccy);
            vNet.Text = Fmt.Money(t.NetPayable, ccy);
            vBase.Text = rate.HasValue && rate.Value > 0 ? Fmt.Money(t.BaseTotal, baseCcy) : "-";

            if (ms != null && t.Total > 0)
            {
                if (t.Total > ms.Balance)
                {
                    lblCheck.ForeColor = Theme.Warn;
                    lblCheck.Text = "This invoice is " + Fmt.Money(t.Total - ms.Balance, ccy) + " more than the milestone balance. It can be saved as a draft, but it cannot be booked until it fits.";
                }
                else
                {
                    lblCheck.ForeColor = Theme.Good;
                    lblCheck.Text = "Within the milestone balance. " + Fmt.Money(ms.Balance - t.Total, ccy) + " would be left on this milestone.";
                }
            }
        }

        // ---------- save
        private void Save()
        {
            lblErr.Text = "";
            if (po == null) { lblErr.Text = "Choose the purchase order this invoice is for."; return; }
            if (ms == null) { lblErr.Text = "Choose the milestone this invoice is for."; return; }

            decimal? taxable = PoUi.Dec(tbTaxable.Text);
            if (!taxable.HasValue) { lblErr.Text = "The taxable value must be a number."; return; }
            decimal? gst = string.IsNullOrWhiteSpace(tbGst.Text) ? 0m : PoUi.Dec(tbGst.Text);
            if (!gst.HasValue) { lblErr.Text = "GST % must be a number."; return; }
            decimal? tds = string.IsNullOrWhiteSpace(tbTdsPct.Text) ? 0m : PoUi.Dec(tbTdsPct.Text);
            if (!tds.HasValue) { lblErr.Text = "TDS % must be a number."; return; }
            decimal? rate = tbRate.Enabled ? PoUi.Dec(tbRate.Text) : (decimal?)1m;
            if (!rate.HasValue) { lblErr.Text = "Enter the exchange rate (1 " + po.Currency + " = ? " + baseCcy + ")."; return; }
            if (tds.Value > 0 && cbTds.Text.Trim().Length == 0) { lblErr.Text = "Enter the TDS section, or set TDS % to 0."; return; }

            var inv = new Invoice
            {
                EntityCode = Current.Entity.Code,
                PoId = po.Id, PoNo = po.No,
                MilestoneId = ms.Id, MilestoneSeq = ms.SeqNo,
                VendorInvoiceNo = tbVin.Text,
                InvoiceDate = dtInv.Value.Date,
                DueDate = dtDue.Checked ? (DateTime?)dtDue.Value.Date : null,
                ExchangeRate = rate.Value,
                Taxable = taxable.Value, GstPercent = gst.Value,
                TdsSection = tds.Value > 0 ? cbTds.Text.Trim() : "", TdsPercent = tds.Value,
                FileRef = tbFile.Text.Trim(), Remarks = tbRemarks.Text.Trim()
            };
            if (existing != null)
            {
                inv.Id = existing.Id; inv.No = existing.No; inv.Status = existing.Status;
                inv.CreatedBy = existing.CreatedBy; inv.CreatedAt = existing.CreatedAt;
            }
            else inv.CreatedBy = AppSession.UserName;

            try { Db.SaveInvoice(inv); }
            catch (InvalidOperationException ex) { lblErr.Text = ex.Message; return; }
            catch (Exception ex) { lblErr.Text = "Could not save: " + ex.Message; return; }

            if (Saved != null) Saved(inv, existing == null ? inv.No + " was saved as a draft." : "Changes to " + inv.No + " were saved.");
        }
    }
}
