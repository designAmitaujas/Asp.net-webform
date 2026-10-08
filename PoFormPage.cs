using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // Create or edit a purchase order (a draft). Totals are shown live; the real rules are checked again by Db.SavePo.
    public class PoFormPage : Panel
    {
        public event Action<PurchaseOrder, string> Saved;
        public event EventHandler Cancelled;

        private class AgChoice
        {
            public Agreement A; public decimal Balance;
            public override string ToString() { return A.No + "   ·   " + A.Supplier + "   ·   left " + Fmt.Money(Balance, A.Currency); }
        }

        private readonly PurchaseOrder existing;      // null = new PO
        private ComboBox cbAgreement;
        private Label lblVendor, lblCcy, lblAgValue, lblBalance, lblErr;
        private TextBox tbTitle, tbRemarks;
        private DateTimePicker dtPo, dtDelivery;
        private BufferedGrid gLines, gMs;
        private Label vSub, vTax, vTotal, vSched, vDiff, vAfter;
        private Agreement ag;          // the selected agreement
        private decimal balance;       // what is left on it for this PO
        private bool loading;

        public PoFormPage(PurchaseOrder po)
        {
            existing = po;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Fill();
        }

        // ---------- layout
        private void Build()
        {
            // bottom bar: message + buttons
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
            head.Controls.Add(PoUi.L(existing == null ? "NEW PURCHASE ORDER" : "EDIT " + existing.No, Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2));
            head.Controls.Add(PoUi.L(existing == null ? "Raise a purchase order" : "Edit purchase order", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));

            PoUi.Stack(scroll, head, BuildDetailsCard(), PoUi.Gap(14), BuildLinesCard(), PoUi.Gap(14), BuildMilestonesCard(), PoUi.Gap(14), BuildTotalsCard(), PoUi.Gap(20));
        }

        private static Label Cap(string text, int x, int y) { return PoUi.L(text, Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y); }

        private Control BuildDetailsCard()
        {
            var card = new CardPanel { Height = 252 };
            card.Controls.Add(PoUi.L("Agreement and details", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));

            card.Controls.Add(Cap("AGREEMENT *", 18, 48));
            cbAgreement = new ComboBox { Location = new Point(18, 66), Width = 520, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10) };
            cbAgreement.SelectedIndexChanged += (s, e) => { if (!loading) AgreementChanged(); };
            card.Controls.Add(cbAgreement);

            // read-only facts about the chosen agreement
            card.Controls.Add(Cap("VENDOR", 580, 48));
            lblVendor = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 580, 64);
            card.Controls.Add(lblVendor);
            card.Controls.Add(Cap("CURRENCY", 580, 90));
            lblCcy = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 580, 106);
            card.Controls.Add(lblCcy);
            card.Controls.Add(Cap("AGREEMENT VALUE", 760, 90));
            lblAgValue = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 760, 106);
            card.Controls.Add(lblAgValue);
            card.Controls.Add(Cap("LEFT FOR THIS PO", 760, 48));
            lblBalance = PoUi.L("", Theme.F(9.5f, FontStyle.Bold), Theme.Good, 760, 64);
            card.Controls.Add(lblBalance);

            card.Controls.Add(Cap("TITLE *", 18, 112));
            tbTitle = new TextBox { Location = new Point(18, 130), Width = 520, Font = Theme.F(10), MaxLength = 250, BorderStyle = BorderStyle.FixedSingle };
            card.Controls.Add(tbTitle);

            card.Controls.Add(Cap("PO DATE *", 18, 176));
            dtPo = new DateTimePicker { Location = new Point(18, 194), Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", Font = Theme.F(10) };
            card.Controls.Add(dtPo);
            card.Controls.Add(Cap("DELIVERY DATE", 208, 176));
            dtDelivery = new DateTimePicker { Location = new Point(208, 194), Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", ShowCheckBox = true, Checked = false, Font = Theme.F(10) };
            card.Controls.Add(dtDelivery);
            card.Controls.Add(Cap("REMARKS", 400, 176));
            tbRemarks = new TextBox { Location = new Point(400, 194), Width = 520, Font = Theme.F(10), MaxLength = 1000, BorderStyle = BorderStyle.FixedSingle };
            card.Controls.Add(tbRemarks);
            return card;
        }

        private Control BuildLinesCard()
        {
            var card = new CardPanel { Height = 300, Padding = new Padding(1) };
            gLines = PoUi.MakeGrid(40, true);
            gLines.Columns.Add(PoUi.Col("DESCRIPTION", 46, 0, false, false));
            gLines.Columns.Add(PoUi.Col("UNIT", 0, 80, false, false));
            gLines.Columns.Add(PoUi.Col("QTY", 0, 90, true, false));
            gLines.Columns.Add(PoUi.Col("RATE", 0, 120, true, false));
            gLines.Columns.Add(PoUi.Col("TAX %", 0, 80, true, false));
            gLines.Columns.Add(PoUi.Col("AMOUNT", 0, 130, true, true));
            gLines.CellEndEdit += (s, e) => Recalc();

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1, 0, 1, 0), BackColor = Color.White };
            body.Controls.Add(gLines);
            var head = SectionHead("Lines", "Add line", () => { AddLine(null); Recalc(); }, "Remove line", () => RemoveRow(gLines));
            card.Controls.Add(body);
            card.Controls.Add(head);
            return card;
        }

        private Control BuildMilestonesCard()
        {
            var card = new CardPanel { Height = 270, Padding = new Padding(1) };
            gMs = PoUi.MakeGrid(40, true);
            gMs.Columns.Add(PoUi.Col("MILESTONE", 60, 0, false, false));
            gMs.Columns.Add(PoUi.Col("DUE DATE  (e.g. 31 Dec 2026)", 0, 200, false, false));
            gMs.Columns.Add(PoUi.Col("AMOUNT (incl. tax)", 0, 170, true, false));
            gMs.CellEndEdit += (s, e) => { NormaliseMilestoneCell(e.RowIndex, e.ColumnIndex); Recalc(); };

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1, 0, 1, 0), BackColor = Color.White };
            body.Controls.Add(gMs);
            var head = SectionHead("Payment milestones", "Add milestone", () => { AddMilestone(null); Recalc(); }, "Remove", () => RemoveRow(gMs), "Match total", MatchTotal);
            card.Controls.Add(body);
            card.Controls.Add(head);
            return card;
        }

        // Title on the left; buttons on the right (Add, Remove and an optional third)
        private Panel SectionHead(string title, string addText, Action add, string removeText, Action remove, string extraText = null, Action extra = null)
        {
            var head = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.White };
            head.Controls.Add(PoUi.L(title, Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));
            var bAdd = AppButton.Make(addText, "primary", "plus", 30, false, 120);
            var bRem = AppButton.Make(removeText, "ghost", null, 30, false, 100);
            AppButton bExtra = null;
            bAdd.Click += (s, e) => add();
            bRem.Click += (s, e) => remove();
            head.Controls.Add(bAdd); head.Controls.Add(bRem);
            if (extraText != null)
            {
                bExtra = AppButton.Make(extraText, "ghost", null, 30, false, 110);
                bExtra.Click += (s, e) => extra();
                head.Controls.Add(bExtra);
            }
            head.Resize += (s, e) =>
            {
                bAdd.Location = new Point(head.Width - 18 - bAdd.Width, 8);
                bRem.Location = new Point(bAdd.Left - 8 - bRem.Width, 8);
                if (bExtra != null) bExtra.Location = new Point(bRem.Left - 8 - bExtra.Width, 8);
            };
            return head;
        }

        private Control BuildTotalsCard()
        {
            var card = new CardPanel { Height = 86 };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Color.White, Padding = new Padding(14, 12, 14, 8) };
            for (int i = 0; i < 6; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
            Func<string, Label> cell = (caption) =>
            {
                var box = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
                box.Controls.Add(Cap(caption, 0, 0));
                var v = PoUi.L("-", Theme.F(12, FontStyle.Bold), Theme.Ink, 0, 20);
                box.Controls.Add(v);
                t.Controls.Add(box);
                return v;
            };
            vSub = cell("SUB-TOTAL"); vTax = cell("TAX"); vTotal = cell("PO TOTAL");
            vSched = cell("MILESTONES ADD UP TO"); vDiff = cell("DIFFERENCE"); vAfter = cell("AGREEMENT LEFT AFTER");
            card.Controls.Add(t);
            return card;
        }

        // ---------- fill the form
        private void Fill()
        {
            loading = true;
            string entity = Current.Entity.Code;
            if (existing == null)
            {
                try
                {
                    foreach (var a in Db.AgreementsOpenForPo(entity)) cbAgreement.Items.Add(new AgChoice { A = a, Balance = Db.AgreementBalance(a.Id, 0) });
                }
                catch (Exception ex) { lblErr.Text = "Could not load the agreements: " + ex.Message; }
                if (cbAgreement.Items.Count > 0) cbAgreement.SelectedIndex = 0;
                dtPo.Value = DateTime.Today;
                AddLine(null);
                AddMilestone(null);
            }
            else
            {
                try
                {
                    var a = Db.LoadAgreements(entity).FirstOrDefault(x => x.Id == existing.AgreementId);
                    if (a != null) cbAgreement.Items.Add(new AgChoice { A = a, Balance = Db.AgreementBalance(existing.AgreementId, existing.Id) });
                }
                catch (Exception ex) { lblErr.Text = "Could not load the agreement: " + ex.Message; }
                if (cbAgreement.Items.Count > 0) cbAgreement.SelectedIndex = 0;
                cbAgreement.Enabled = false;   // a PO stays with the agreement it was raised against
                tbTitle.Text = existing.Title;
                tbRemarks.Text = existing.Remarks;
                dtPo.Value = existing.PoDate;
                if (existing.DeliveryDate.HasValue) { dtDelivery.Value = existing.DeliveryDate.Value; dtDelivery.Checked = true; }
                foreach (var l in existing.Lines) AddLine(l);
                foreach (var m in existing.Milestones) AddMilestone(m);
            }
            loading = false;
            AgreementChanged();
            Recalc();
        }

        private void AgreementChanged()
        {
            var ch = cbAgreement.SelectedItem as AgChoice;
            ag = ch == null ? null : ch.A;
            balance = ch == null ? 0 : ch.Balance;
            if (ag == null) { lblVendor.Text = lblCcy.Text = lblAgValue.Text = lblBalance.Text = ""; return; }
            lblVendor.Text = ag.Supplier;
            lblCcy.Text = ag.Currency;
            lblAgValue.Text = Fmt.Money(ag.Value, ag.Currency);
            lblBalance.Text = Fmt.Money(balance, ag.Currency);
            if (existing == null && tbTitle.Text.Trim().Length == 0) tbTitle.Text = ag.Title;
            Recalc();
        }

        private void AddLine(PoLine l)
        {
            string defaultTax = Current.Entity.Code == "IN" ? "18" : "0";
            int i = gLines.Rows.Add(l == null ? "" : l.Description, l == null ? "" : l.Unit,
                l == null ? "1" : l.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                l == null ? "" : PoUi.N2(l.Rate), l == null ? defaultTax : l.TaxPercent.ToString("0.##", CultureInfo.InvariantCulture),
                l == null ? "" : PoUi.N2(l.Amount));
            gLines.Rows[i].Tag = l == null ? 0 : l.Id;
            if (l == null) { gLines.CurrentCell = gLines.Rows[i].Cells[0]; }
        }

        private void AddMilestone(PoMilestone m)
        {
            decimal unscheduled = 0;
            if (m == null) { var po = Collect(false); unscheduled = Math.Max(0, po == null ? 0 : po.Total - po.Milestones.Sum(x => x.Amount)); }
            int i = gMs.Rows.Add(m == null ? "" : m.Description, m == null ? "" : PoUi.Date(m.DueDate),
                m == null ? (unscheduled > 0 ? PoUi.N2(unscheduled) : "") : PoUi.N2(m.Amount));
            gMs.Rows[i].Tag = m == null ? 0 : m.Id;
            if (m == null) gMs.CurrentCell = gMs.Rows[i].Cells[0];
        }

        private void RemoveRow(DataGridView g)
        {
            if (g.CurrentRow == null) return;
            g.EndEdit();
            g.Rows.Remove(g.CurrentRow);
            Recalc();
        }

        // Tidy a typed milestone date or amount so it reads the same everywhere
        private void NormaliseMilestoneCell(int row, int col)
        {
            if (row < 0) return;
            var cell = gMs.Rows[row].Cells[col];
            if (col == 1) { DateTime d; if (PoUi.TryDate(Convert.ToString(cell.Value), out d)) cell.Value = PoUi.Date(d); }
        }

        // Adjusts the last milestone so the milestones add up to the PO total
        private void MatchTotal()
        {
            var po = Collect(false);
            if (po == null || po.Total <= 0) { lblErr.Text = "Add the lines first, so there is a total to match."; return; }
            if (gMs.Rows.Count == 0) { AddMilestone(null); }
            decimal others = 0;
            for (int i = 0; i < gMs.Rows.Count - 1; i++) others += PoUi.Dec(gMs.Rows[i].Cells[2].Value) ?? 0;
            decimal last = po.Total - others;
            if (last <= 0) { lblErr.Text = "The other milestones already add up to more than the PO total."; return; }
            lblErr.Text = "";
            gMs.Rows[gMs.Rows.Count - 1].Cells[2].Value = PoUi.N2(last);
            Recalc();
        }

        // ---------- read the screen
        // Builds a PO from what is typed. Returns null (and shows why) when a typed value is not readable; quiet = no message.
        private PurchaseOrder Collect(bool report)
        {
            var po = new PurchaseOrder { EntityCode = Current.Entity.Code };
            for (int i = 0; i < gLines.Rows.Count; i++)
            {
                var r = gLines.Rows[i];
                string desc = Convert.ToString(r.Cells[0].Value);
                string qty = Convert.ToString(r.Cells[2].Value), rate = Convert.ToString(r.Cells[3].Value), tax = Convert.ToString(r.Cells[4].Value);
                if ((desc ?? "").Trim().Length == 0 && string.IsNullOrWhiteSpace(rate) && string.IsNullOrWhiteSpace(Convert.ToString(r.Cells[1].Value))) continue;   // an untouched row
                decimal? q = PoUi.Dec(qty), rt = PoUi.Dec(rate), tx = string.IsNullOrWhiteSpace(tax) ? 0m : PoUi.Dec(tax);
                if (!q.HasValue || !rt.HasValue || !tx.HasValue)
                {
                    if (report) lblErr.Text = "Line " + (i + 1) + ": quantity, rate and tax % must be numbers.";
                    return null;
                }
                po.Lines.Add(new PoLine { Id = (int)r.Tag, Description = desc, Unit = Convert.ToString(r.Cells[1].Value), Quantity = q.Value, Rate = rt.Value, TaxPercent = tx.Value });
            }
            po.Recalculate();
            for (int i = 0; i < gMs.Rows.Count; i++)
            {
                var r = gMs.Rows[i];
                string desc = Convert.ToString(r.Cells[0].Value), due = Convert.ToString(r.Cells[1].Value), amt = Convert.ToString(r.Cells[2].Value);
                if ((desc ?? "").Trim().Length == 0 && string.IsNullOrWhiteSpace(due) && string.IsNullOrWhiteSpace(amt)) continue;
                decimal? a = PoUi.Dec(amt);
                DateTime d = DateTime.MinValue;
                bool hasDue = !string.IsNullOrWhiteSpace(due);
                if (!a.HasValue) { if (report) lblErr.Text = "Milestone " + (i + 1) + ": the amount must be a number."; return null; }
                if (hasDue && !PoUi.TryDate(due, out d)) { if (report) lblErr.Text = "Milestone " + (i + 1) + ": the due date is not valid. Use a form like 31 Dec 2026."; return null; }
                po.Milestones.Add(new PoMilestone { Id = (int)r.Tag, Description = desc, DueDate = hasDue ? (DateTime?)d : null, Amount = a.Value });
            }
            return po;
        }

        private void Recalc()
        {
            if (gLines == null || gMs == null || vSub == null) return;
            for (int i = 0; i < gLines.Rows.Count; i++)   // each line's amount
            {
                var r = gLines.Rows[i];
                decimal? q = PoUi.Dec(r.Cells[2].Value), rt = PoUi.Dec(r.Cells[3].Value);
                r.Cells[5].Value = q.HasValue && rt.HasValue ? PoUi.N2(PurchaseOrder.Round(q.Value * rt.Value)) : "";
            }
            var po = Collect(false);
            string ccy = ag == null ? "" : ag.Currency;
            if (po == null) { vSub.Text = vTax.Text = vTotal.Text = vSched.Text = vDiff.Text = vAfter.Text = "-"; return; }
            decimal sched = po.Milestones.Sum(m => PurchaseOrder.Round(m.Amount));
            decimal diff = po.Total - sched;
            vSub.Text = Fmt.Money(po.SubTotal, ccy);
            vTax.Text = Fmt.Money(po.TaxTotal, ccy);
            vTotal.Text = Fmt.Money(po.Total, ccy);
            vSched.Text = Fmt.Money(sched, ccy);
            vDiff.Text = diff == 0 ? "None" : Fmt.Money(diff, ccy);
            vDiff.ForeColor = diff == 0 ? Theme.Good : Theme.Bad;
            decimal after = balance - po.SubTotal;
            vAfter.Text = Fmt.Money(after, ccy);
            vAfter.ForeColor = after < 0 ? Theme.Bad : Theme.Ink;
        }

        // ---------- save
        private void Save()
        {
            lblErr.Text = "";
            gLines.EndEdit(); gMs.EndEdit();
            if (ag == null) { lblErr.Text = "Choose the agreement this purchase order is raised against."; return; }
            var po = Collect(true);
            if (po == null) return;

            po.Title = tbTitle.Text;
            po.Remarks = tbRemarks.Text.Trim();
            po.PoDate = dtPo.Value.Date;
            po.DeliveryDate = dtDelivery.Checked ? (DateTime?)dtDelivery.Value.Date : null;
            po.AgreementId = ag.Id;
            po.AgreementNo = ag.No;
            if (existing != null)
            {
                po.Id = existing.Id; po.No = existing.No; po.Status = existing.Status;
                po.CreatedBy = existing.CreatedBy; po.CreatedAt = existing.CreatedAt;
            }
            else po.CreatedBy = AppSession.UserName;

            try { Db.SavePo(po); }
            catch (InvalidOperationException ex) { lblErr.Text = ex.Message; return; }
            catch (Exception ex) { lblErr.Text = "Could not save: " + ex.Message; return; }

            if (Saved != null) Saved(po, existing == null ? po.No + " was created as a draft." : "Changes to " + po.No + " were saved.");
        }
    }
}
