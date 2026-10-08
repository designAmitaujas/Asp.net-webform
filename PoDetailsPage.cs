using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // One purchase order, read-only: header facts, lines, payment milestones
    public class PoDetailsPage : Panel
    {
        public event EventHandler BackRequested;
        public event Action<PurchaseOrder> EditRequested;
        public event Action<int> OpenAgreementRequested;   // agreement id

        private PurchaseOrder po;

        public PoDetailsPage(PurchaseOrder order, string flash)
        {
            po = order;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build(flash, null);
        }

        private static Label Cap(string text, int x, int y) { return PoUi.L(text, Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y); }

        // After any action: reload the PO from the database (so the screen always shows the real state) and draw the page again
        private void Rebuild(string ok, string error)
        {
            try { var fresh = Db.LoadPos(po.EntityCode).FirstOrDefault(x => x.Id == po.Id); if (fresh != null) po = fresh; }
            catch { /* keep what we have */ }
            SuspendLayout();
            foreach (var c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
            Build(ok, error);
            ResumeLayout();
        }

        private void Defer(string ok, string error)
        {
            BeginInvoke(new Action(() => Rebuild(ok, error)));   // after the current click has finished
        }

        // Runs one workflow action; whatever happens, the page is redrawn from the database afterwards
        private void Act(Action work, string okMessage)
        {
            try { work(); Defer(okMessage, null); }
            catch (InvalidOperationException ex) { Defer(null, ex.Message); }
            catch (Exception ex) { Defer(null, "Could not complete that: " + ex.Message); }
        }

        private static Control Banner(string text, bool ok)
        {
            var n = new CardPanel { Height = 52, BackColor = ok ? Theme.GoodSoft : Theme.BadSoft, BorderColor = ok ? Theme.Good : Theme.Bad, Padding = new Padding(1) };
            n.Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 10, 0), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(9.5f, FontStyle.Bold), ForeColor = ok ? Theme.Good : Theme.Bad, BackColor = ok ? Theme.GoodSoft : Theme.BadSoft, UseMnemonic = false, Text = text });
            return n;
        }

        private void Build(string flash, string error)
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            Controls.Add(scroll);

            // ---- heading with buttons
            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            head.Controls.Add(PoUi.L("PURCHASE ORDER", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2));
            var title = PoUi.L(po.No, Theme.F(16, FontStyle.Bold), Theme.Ink, 0, 20);
            head.Controls.Add(title);
            Color back, fore;
            PoUi.StatusColors(po.Status, out back, out fore);
            var pill = new Label { Text = po.Status, Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = fore, BackColor = back, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Height = 24 };
            pill.Width = TextRenderer.MeasureText(po.Status, pill.Font).Width + 24;
            pill.Location = new Point(title.PreferredWidth + 14, 25);
            head.Controls.Add(pill);

            var bBack = AppButton.Make("Back to list", "ghost", "back", 32, false, 110);
            bBack.Click += (s, e) => { if (BackRequested != null) BackRequested(this, EventArgs.Empty); };
            head.Controls.Add(bBack);
            AppButton bEdit = null;
            if (po.IsEditable && Perm.Can("po.create"))
            {
                bEdit = AppButton.Make("Edit", "primary", "edit", 32, false, 90);
                bEdit.Click += (s, e) => { if (EditRequested != null) EditRequested(po); };
                head.Controls.Add(bEdit);
            }
            head.Resize += (s, e) =>
            {
                bBack.Location = new Point(head.Width - bBack.Width, 6);
                if (bEdit != null) bEdit.Location = new Point(bBack.Left - 8 - bEdit.Width, 6);
            };

            // ---- message after a save or an action
            Control notice = PoUi.Gap(0);
            if (!string.IsNullOrEmpty(error)) notice = Banner(error, false);
            else if (!string.IsNullOrEmpty(flash)) notice = Banner(flash, true);

            var budgetCard = BuildBudgetCard();
            PoUi.Stack(scroll, head, notice, PoUi.Gap(notice.Height > 0 ? 12 : 0), BuildInfoCard(), PoUi.Gap(14), budgetCard, PoUi.Gap(budgetCard.Height > 0 ? 14 : 0), BuildLinesCard(), PoUi.Gap(14), BuildMilestonesCard(), PoUi.Gap(14), BuildWorkflowCard(), PoUi.Gap(20));
        }

        // Shows how this PO stands against its budget line while it can still be changed or decided
        private Control BuildBudgetCard()
        {
            if (po.Status != "Draft" && po.Status != "Rejected" && po.Status != "Pending Approval") return PoUi.Gap(0);
            BudgetCheckResult r;
            try { r = Db.BudgetCheckPo(po); }
            catch { return PoUi.Gap(0); }   // the budget note is extra information; never stop the page from opening
            if (r == null || r.Outcome == "Off") return PoUi.Gap(0);

            Color back, fore, line;
            if (r.Outcome == "Ok") { back = Theme.GoodSoft; fore = Theme.Good; line = Theme.GoodLine; }
            else if (r.Outcome == "OtherCurrency") { back = Theme.BrandSoft; fore = Theme.Brand; line = Theme.BrandLine; }
            else if (r.Blocks) { back = Theme.BadSoft; fore = Theme.Bad; line = Theme.BadLine; }
            else { back = Theme.WarnSoft; fore = Theme.Warn; line = Theme.WarnLine; }

            string lead = r.Outcome == "Ok" ? "Budget check: " : r.Blocks ? "Budget check - blocked: " : r.IsProblem ? "Budget check - warning: " : "Budget check: ";
            var card = new CardPanel { Height = 56, BackColor = back, BorderColor = line, Padding = new Padding(1) };
            card.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(16, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.F(9.5f, FontStyle.Bold), ForeColor = fore, BackColor = back, UseMnemonic = false, Text = lead + r.Message
            });
            return card;
        }

        private void SubmitClicked()
        {
            // Warn mode: say what is wrong and let the person decide. (Block mode is enforced when saving, with the same message.)
            BudgetCheckResult chk = null;
            try { chk = Db.BudgetCheckPo(po); } catch { /* the save below still checks */ }
            if (chk != null && chk.IsProblem && !chk.Blocks)
            {
                var answer = MessageBox.Show(FindForm(), chk.Message + "\r\n\r\nSend it for approval anyway? Accounts will be told.", "Budget check",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }
            Act(() => PoWorkflow.Submit(po), "Sent for approval.");
        }

        private Control BuildInfoCard()
        {
            var card = new CardPanel { Height = 150 };
            string[,] facts =
            {
                { "TITLE", po.Title }, { "VENDOR", po.VendorName }, { "AGREEMENT", po.AgreementNo },
                { "PO DATE", po.PoDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) }, { "DELIVERY DATE", string.IsNullOrEmpty(PoUi.Date(po.DeliveryDate)) ? "-" : PoUi.Date(po.DeliveryDate) },
                { "CURRENCY", po.Currency }, { "RAISED BY", po.CreatedBy + "  ·  " + po.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) },
                { "REMARKS", string.IsNullOrWhiteSpace(po.Remarks) ? "-" : po.Remarks }
            };
            for (int i = 0; i < 8; i++)
            {
                int col = i % 3, row = i / 3;
                int x = 18 + col * 330, y = 16 + row * 44;
                card.Controls.Add(Cap(facts[i, 0], x, y));
                var v = PoUi.L(facts[i, 1], Theme.F(9.5f, FontStyle.Bold), Theme.Ink, x, y + 16);
                v.MaximumSize = new Size(310, 0);
                if (facts[i, 0] == "AGREEMENT")
                {
                    v.ForeColor = Theme.Brand; v.Cursor = Cursors.Hand;
                    v.Click += (s, e) => { if (OpenAgreementRequested != null) OpenAgreementRequested(po.AgreementId); };
                }
                card.Controls.Add(v);
            }
            if (po.Status == "Cancelled")
            {
                card.Height = 190;
                card.Controls.Add(PoUi.L("Cancelled by " + po.CancelledBy + ": " + po.CancelReason, Theme.F(9.5f, FontStyle.Bold), Theme.Bad, 18, 148));
            }
            return card;
        }

        private Control BuildLinesCard()
        {
            int rows = Math.Max(1, po.Lines.Count);
            var card = new CardPanel { Height = 46 + 36 + rows * 40 + 78 + 6, Padding = new Padding(1) };
            var g = PoUi.MakeGrid(40, false);
            g.ScrollBars = ScrollBars.None;
            g.Columns.Add(PoUi.Col("#", 0, 50, false, true));
            g.Columns.Add(PoUi.Col("DESCRIPTION", 50, 0, false, true));
            g.Columns.Add(PoUi.Col("UNIT", 0, 80, false, true));
            g.Columns.Add(PoUi.Col("QTY", 0, 90, true, true));
            g.Columns.Add(PoUi.Col("RATE", 0, 120, true, true));
            g.Columns.Add(PoUi.Col("TAX %", 0, 80, true, true));
            g.Columns.Add(PoUi.Col("AMOUNT", 0, 130, true, true));
            int n = 0;
            foreach (var l in po.Lines)
                g.Rows.Add(++n, l.Description, l.Unit, l.Quantity.ToString("0.###", CultureInfo.InvariantCulture), PoUi.N2(l.Rate), l.TaxPercent.ToString("0.##", CultureInfo.InvariantCulture), PoUi.N2(l.Amount));
            foreach (DataGridViewColumn c in g.Columns) c.DefaultCellStyle.BackColor = Color.White;
            g.ClearSelection();

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Theme.Hover };
            foot.Controls.Add(PoUi.L("Sub-total  " + Fmt.Money(po.SubTotal, po.Currency) + "      Tax  " + Fmt.Money(po.TaxTotal, po.Currency), Theme.F(9.5f), Theme.Ink2, 18, 12));
            foot.Controls.Add(PoUi.L("PO total  " + Fmt.Money(po.Total, po.Currency), Theme.F(12, FontStyle.Bold), Theme.Ink, 18, 38));

            var headRow = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.White };
            headRow.Controls.Add(PoUi.L("Lines", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));
            card.Controls.Add(g);
            card.Controls.Add(foot);
            card.Controls.Add(headRow);
            return card;
        }

        private Control BuildMilestonesCard()
        {
            int rows = Math.Max(1, po.Milestones.Count);
            var card = new CardPanel { Height = 46 + 36 + rows * 40 + 8, Padding = new Padding(1) };
            var g = PoUi.MakeGrid(40, false);
            g.ScrollBars = ScrollBars.None;
            g.Columns.Add(PoUi.Col("#", 0, 50, false, true));
            g.Columns.Add(PoUi.Col("MILESTONE", 50, 0, false, true));
            g.Columns.Add(PoUi.Col("DUE DATE", 0, 130, false, true));
            g.Columns.Add(PoUi.Col("AMOUNT", 0, 140, true, true));
            g.Columns.Add(PoUi.Col("INVOICED", 0, 140, true, true));
            g.Columns.Add(PoUi.Col("STATUS", 0, 110, false, true));
            foreach (var m in po.Milestones)
                g.Rows.Add(m.SeqNo, m.Description, string.IsNullOrEmpty(PoUi.Date(m.DueDate)) ? "-" : PoUi.Date(m.DueDate), Fmt.Money(m.Amount, po.Currency), Fmt.Money(m.InvoicedAmount, po.Currency), m.Status);
            foreach (DataGridViewColumn c in g.Columns) c.DefaultCellStyle.BackColor = Color.White;
            g.ClearSelection();
            var headRow = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.White };
            headRow.Controls.Add(PoUi.L("Payment milestones", Theme.F(11, FontStyle.Bold), Theme.Ink, 18, 14));
            card.Controls.Add(g);
            card.Controls.Add(headRow);
            return card;
        }

        // ================= approval workflow + action buttons
        private const int BaseW = 900;   // the card is built at this width, then stretches to the window
        private static readonly AnchorStyles TLR = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        private static readonly AnchorStyles TR = AnchorStyles.Top | AnchorStyles.Right;
        private static readonly Color Grey = ColorTranslator.FromHtml("#E9EEF0");

        private static AppButton MakeBtn(string text, string kind, string icon)
        {
            var b = AppButton.Make(text, kind, icon, 34, false, 110);
            b.Font = Theme.F(9.5f, FontStyle.Bold);
            return b.Fit(34, 110);
        }

        private Control BuildWorkflowCard()
        {
            var card = new CardPanel { Width = BaseW, Padding = new Padding(1) };
            card.Controls.Add(PoUi.L("APPROVAL WORKFLOW", Theme.F(8.5f, FontStyle.Bold), Theme.Brand2, 18, 16));
            int y = 44;
            string role = Current.Role, user = AppSession.UserName;

            // ---- the approval steps
            if (po.Approvals.Count == 0)
            {
                string msg = po.Status == "Draft"
                    ? "Not yet sent for approval. Sending it routes the PO through the approval levels that apply to its total."
                    : "No approval steps recorded.";
                card.Controls.Add(PoUi.L(msg, Theme.F(9), Theme.Ink3, 18, y));
                y += 30;
            }
            else
            {
                var cur = po.Pending;
                foreach (var s in po.Approvals.OrderBy(x => x.Level))
                {
                    card.Controls.Add(StepItem(s, po.Status == "Pending Approval" && cur != null && cur.Level == s.Level, y));
                    y += 60;
                }
            }

            // ---- what the status means
            string note = null;
            switch (po.Status)
            {
                case "Approved": note = "Approved. Invoices can be booked against its milestones once invoice booking is built."; break;
                case "Rejected": note = "Rejected. Edit the purchase order to fix the points raised, then send it again."; break;
                case "Cancelled": note = "Cancelled by " + po.CancelledBy + ". Its amount is back on the agreement balance."; break;
            }
            if (note != null)
            {
                card.Controls.Add(PoUi.L(note, Theme.F(9), Theme.Ink3, 18, y + 2));
                y += 30;
            }

            // ---- approve / reject with remarks
            if (PoRules.CanApprove(po, role, user))
            {
                y += 6;
                card.Controls.Add(PoUi.L("Your remarks (optional to approve; required to reject)", Theme.F(8.5f, FontStyle.Bold), Theme.Ink2, 18, y));
                y += 22;
                var remarks = new TextBox { Location = new Point(18, y), Size = new Size(BaseW - 38, 60), Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 500, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f), Anchor = TLR };
                card.Controls.Add(remarks);
                y += 70;

                var bApprove = MakeBtn("Approve L" + po.Pending.Level, "good", "check");
                var bReject = MakeBtn("Reject", "bad", "close");
                bApprove.Location = new Point(18, y);
                bReject.Location = new Point(18 + bApprove.Width + 8, y);
                var err = new Label { AutoSize = true, ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent, UseMnemonic = false, Location = new Point(bReject.Right + 14, y + 9) };
                bApprove.Click += (s, e) => Act(() => PoWorkflow.Approve(po, user, remarks.Text.Trim()), "Approved.");
                bReject.Click += (s, e) =>
                {
                    if (remarks.Text.Trim().Length == 0) { err.Text = "Remarks are required to reject a purchase order."; remarks.Focus(); return; }
                    Act(() => PoWorkflow.Reject(po, user, remarks.Text.Trim()), "Purchase order rejected.");
                };
                card.Controls.Add(bApprove); card.Controls.Add(bReject); card.Controls.Add(err);
                y += 34 + 14;
            }

            // ---- submit / cancel
            var btns = new List<AppButton>();
            if (PoRules.CanSubmit(po, role))
            {
                var b = MakeBtn(po.Status == "Rejected" ? "Send again for approval" : "Submit for approval", "primary", null);
                b.Click += (s, e) => SubmitClicked();
                btns.Add(b);
            }
            if (PoRules.CanCancel(po, role))
            {
                var b = MakeBtn("Cancel PO", "bad", "close");
                b.Click += (s, e) =>
                {
                    string reason;
                    using (var dlg = new PoCancelDialog(po))
                    {
                        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                        reason = dlg.Reason;
                    }
                    Act(() => PoWorkflow.Cancel(po, user, reason), "Purchase order cancelled.");
                };
                btns.Add(b);
            }
            if (btns.Count > 0)
            {
                y += 4;
                int x = 18;
                foreach (var b in btns) { b.Location = new Point(x, y); card.Controls.Add(b); x += b.Width + 8; }
                y += 34 + 14;
            }

            card.Height = y + 10;
            return card;
        }

        private Control StepItem(ApprovalStep s, bool now, int y)
        {
            var item = new CardPanel
            {
                Location = new Point(18, y),
                Size = new Size(BaseW - 38, 52),
                Anchor = TLR,
                BackColor = now ? Theme.WarnSoft : Color.White,
                BorderColor = now ? Theme.WarnLine : Theme.Line2
            };

            Dot dot;
            if (s.Status == "Approved") dot = new Dot("\u2713", Theme.Good, Color.White);
            else if (s.Status == "Rejected") dot = new Dot("\u2715", Theme.Bad, Color.White);
            else if (now) dot = new Dot(s.Level.ToString(), Theme.Warn, Color.White);
            else dot = new Dot(s.Level.ToString(), Grey, Theme.Ink2);
            dot.Location = new Point(12, 13);
            item.Controls.Add(dot);

            var title = PoUi.L(s.Label, Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 50, 8);
            item.Controls.Add(title);
            item.Controls.Add(PoUi.L("\u00B7 " + s.Role, Theme.F(9), Theme.Ink2, 50 + TextRenderer.MeasureText(s.Label, title.Font).Width, 9));

            string sub = s.Status == "Pending" ? "Awaiting approval"
                : s.Status == "Cancelled" ? "Not needed: the purchase order was cancelled"
                : s.Status + " by " + s.ApprovedBy + " \u00B7 " + (s.At.HasValue ? s.At.Value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) : "");
            if (!string.IsNullOrWhiteSpace(s.Remarks)) sub += " \u2014 \u201C" + s.Remarks + "\u201D";
            item.Controls.Add(PoUi.L(sub, Theme.F(8.5f), Theme.Ink2, 50, 29));

            Color back, fore;
            PoUi.StatusColors(s.Status == "Pending" ? "Pending Approval" : s.Status, out back, out fore);
            var badge = new Badge(s.Status, back, fore) { Anchor = TR };
            badge.Location = new Point(item.Width - badge.Width - 12, 15);
            item.Controls.Add(badge);
            return item;
        }
    }
}
