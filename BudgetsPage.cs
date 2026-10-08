using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // Budgets for the entity you are working in: one row per budget line for the chosen financial year,
    // with what purchase orders have already committed against it.
    public class BudgetsPage : Panel
    {
        private ComboBox cmbYear;
        private CheckBox chkInactive;
        private Label lblEyebrow, lblTotals, lblEmpty, lblNote;
        private BufferedGrid grid;
        private bool loading;
        private readonly bool canEdit = Perm.Can("budget.edit");

        public BudgetsPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Reload();
        }

        private void Build()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            grid = PoUi.MakeGrid(48, false);
            grid.Columns.Add(PoUi.Col("BUDGET LINE", 34, 0, false, true));
            grid.Columns.Add(PoUi.Col("BUDGET", 0, 140, true, true));
            grid.Columns.Add(PoUi.Col("COMMITTED", 0, 140, true, true));
            grid.Columns.Add(PoUi.Col("AVAILABLE", 0, 140, true, true));
            grid.Columns.Add(PoUi.Col("USED", 0, 70, true, true));
            grid.Columns.Add(PoUi.Col("STATUS", 0, 130, false, true));
            foreach (DataGridViewColumn c in grid.Columns) c.DefaultCellStyle.BackColor = Color.White;
            grid.Columns[0].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[3].DefaultCellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            grid.Columns[5].DefaultCellStyle.Font = Theme.F(9, FontStyle.Bold);
            if (canEdit)
            {
                grid.CellClick += (s, e) => { if (e.RowIndex >= 0) OpenRow((BudgetRow)grid.Rows[e.RowIndex].Tag); };
                grid.CellMouseEnter += (s, e) => { if (e.RowIndex >= 0) grid.Cursor = Cursors.Hand; };
                grid.CellMouseLeave += (s, e) => grid.Cursor = Cursors.Default;
            }

            lblEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Ink3, BackColor = Color.White, Visible = false };
            card.Controls.Add(grid);
            card.Controls.Add(lblEmpty);
            Controls.Add(card);

            // ---- header: title on the left; year, show-inactive and Add on the right
            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            lblEyebrow = PoUi.L("", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2);
            head.Controls.Add(lblEyebrow);
            head.Controls.Add(PoUi.L("Budgets", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));

            cmbYear = new ComboBox { Width = 110, Font = Theme.F(10), DropDownStyle = ComboBoxStyle.DropDownList };
            loading = true;
            foreach (var y in Db.BudgetYears(Current.Entity.Code)) cmbYear.Items.Add(y);
            cmbYear.SelectedItem = Current.FinancialYear();
            if (cmbYear.SelectedIndex < 0 && cmbYear.Items.Count > 0) cmbYear.SelectedIndex = 0;
            loading = false;
            cmbYear.SelectedIndexChanged += (s, e) => { if (!loading) Reload(); };

            chkInactive = new CheckBox { Text = "Show inactive", AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.Ink2, BackColor = Color.Transparent };
            chkInactive.CheckedChanged += (s, e) => Reload();

            AppButton add = null;
            if (canEdit)
            {
                add = AppButton.Make("Add budget line", "primary", "plus", 36);
                add.Font = Theme.F(9.5f, FontStyle.Bold);
                add.Fit(36);
                add.Click += (s, e) => AddNew();
                head.Controls.Add(add);
            }
            head.Controls.Add(cmbYear);
            head.Controls.Add(chkInactive);
            head.Resize += (s, e) =>
            {
                int right = head.Width;
                if (add != null) { add.Location = new Point(right - add.Width, 6); right = add.Left - 12; }
                cmbYear.Location = new Point(right - cmbYear.Width, 10); right = cmbYear.Left - 16;
                chkInactive.Location = new Point(right - chkInactive.Width, 14);
            };

            // ---- explanation + warnings
            var notice = new CardPanel { Height = 64, BackColor = Theme.BrandSoft, BorderColor = Theme.BrandLine, Padding = new Padding(1) };
            lblNote = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(46, 0, 14, 0), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(9.5f), ForeColor = Theme.Brand,
                BackColor = Theme.BrandSoft, UseMnemonic = false
            };
            lblNote.Paint += (s, e) => Icons.Draw(e.Graphics, "info", new Rectangle(15, 22, 18, 18), Theme.Brand2);
            notice.Controls.Add(lblNote);

            lblTotals = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(10, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = Color.Transparent, UseMnemonic = false };
            var totalsRow = new Panel { Height = 44, BackColor = Color.Transparent };
            totalsRow.Controls.Add(lblTotals);                  // the Fill control goes in first, the Right panel after it
            totalsRow.Controls.Add(BuildModeControl());

            PoUi.Stack(this, head, notice, totalsRow, PoUi.Gap(4));
        }

        private static readonly string[] ModeValues = { "Warn", "Block", "Off" };
        private static readonly string[] ModeText = { "Warn only", "Block", "Don't check" };
        private bool modeLoading;

        // What happens when a PO is sent for approval over its budget (per entity). Only people who can edit budgets may change it.
        private Control BuildModeControl()
        {
            string mode = Db.BudgetControlMode(Current.Entity.Code);
            var box = new Panel { Dock = DockStyle.Right, Width = canEdit ? 330 : 280, BackColor = Color.Transparent };
            if (!canEdit)
            {
                box.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = Theme.F(9.5f), ForeColor = Theme.Ink2, UseMnemonic = false,
                    Text = "Over-budget POs: " + ModeText[Array.IndexOf(ModeValues, mode)] });
                return box;
            }
            var cap = PoUi.L("IF A PO GOES OVER BUDGET", Theme.F(8, FontStyle.Bold), Theme.Ink3, 0, 15);
            var combo = new ComboBox { Width = 130, Font = Theme.F(10), DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(box.Width - 130, 8) };
            combo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            modeLoading = true;
            foreach (var t in ModeText) combo.Items.Add(t);
            combo.SelectedIndex = Array.IndexOf(ModeValues, mode);
            modeLoading = false;
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (modeLoading || combo.SelectedIndex < 0) return;
                try { Db.SetSetting("BudgetControl", ModeValues[combo.SelectedIndex], Current.Entity.Code); }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Could not save the setting: " + ex.Message, "Budgets", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    modeLoading = true; combo.SelectedIndex = Array.IndexOf(ModeValues, Db.BudgetControlMode(Current.Entity.Code)); modeLoading = false;
                }
            };
            box.Controls.Add(cap);
            box.Controls.Add(combo);
            return box;
        }

        private string Year { get { return cmbYear.SelectedItem as string ?? Current.FinancialYear(); } }

        private void Reload()
        {
            if (loading || cmbYear == null) return;
            string ccy = Current.Entity.Currency;
            List<BudgetRow> all;
            int otherCcy;
            try { all = Db.BudgetList(Current.Entity.Code, Year, out otherCcy); }
            catch (Exception ex)
            {
                grid.Visible = false;
                lblEmpty.Text = "Could not load budgets: " + ex.Message;
                lblEmpty.Visible = true;
                return;
            }

            var rows = all.Where(r => chkInactive.Checked || !r.HasBudget || r.IsActive).ToList();
            var live = all.Where(r => r.HasBudget && r.IsActive).ToList();
            int budgeted = live.Count, unbudgeted = all.Count(r => !r.HasBudget);
            lblEyebrow.Text = (budgeted + (budgeted == 1 ? " BUDGET LINE" : " BUDGET LINES") + " IN " + Current.Entity.Code + "  ·  " + Year).ToUpper();

            decimal total = live.Sum(r => r.Amount), committed = all.Sum(r => r.Committed);
            lblTotals.Text = "Budget " + Fmt.Money(total, ccy) + "     Committed " + Fmt.Money(committed, ccy) + "     Available " + Fmt.Money(total - committed, ccy);
            lblTotals.ForeColor = total - committed < 0 ? Theme.Bad : Theme.Ink;

            string note = "Committed = purchase orders that are pending approval, approved or closed, before tax, matched to a budget line by the agreement's \"GL / budget line\" field. Invoices are added in a later step.";
            if (otherCcy > 0) note += "  " + otherCcy + " PO(s) in another currency are not counted yet.";
            if (unbudgeted > 0) note += "  " + unbudgeted + " line(s) are used by POs but have no budget.";
            lblNote.Text = note;

            grid.Rows.Clear();
            foreach (var r in rows)
            {
                string label = r.Line.Length == 0 ? "(no budget line set on the agreement)" : r.Line;
                if (!string.IsNullOrEmpty(r.Description)) label += "  ·  " + r.Description;
                string status; Color color;
                if (!r.HasBudget) { status = "No budget"; color = Theme.Warn; }
                else if (!r.IsActive) { status = "Inactive"; color = Theme.Ink3; }
                else if (r.Committed > r.Amount) { status = "Over budget"; color = Theme.Bad; }
                else if (r.UsedPercent >= 90) { status = "Near limit"; color = Theme.Warn; }
                else { status = "On track"; color = Theme.Good; }

                int i = grid.Rows.Add(label,
                    r.HasBudget ? Fmt.Money(r.Amount, r.Currency) : "-",
                    Fmt.Money(r.Committed, r.Currency),
                    r.HasBudget ? Fmt.Money(r.Available, r.Currency) : "-",
                    r.HasBudget ? r.UsedPercent.ToString("0", CultureInfo.InvariantCulture) + "%" : "-",
                    status);
                grid.Rows[i].Tag = r;
                grid.Rows[i].Cells[5].Style.ForeColor = color;
                if (r.HasBudget && r.Available < 0) grid.Rows[i].Cells[3].Style.ForeColor = Theme.Bad;
                if (r.HasBudget && !r.IsActive) foreach (DataGridViewCell cell in grid.Rows[i].Cells) cell.Style.ForeColor = Theme.Ink3;
            }
            grid.Visible = rows.Count > 0;
            lblEmpty.Text = canEdit
                ? "No budget lines for " + Year + " yet. Click Add budget line to create the first one."
                : "No budget lines for " + Year + " yet. Accounts or an Administrator can add them.";
            lblEmpty.Visible = rows.Count == 0;
            grid.ClearSelection();
        }

        private void AddNew()
        {
            using (var f = new BudgetForm(null, Year, false))
                if (f.ShowDialog(FindForm()) == DialogResult.OK) { SelectYear(f.SavedYear); Reload(); }
        }

        private void OpenRow(BudgetRow row)
        {
            // a line POs already use but nobody budgeted: open the form with that line filled in (its name stays as it is)
            BudgetRow target = row.HasBudget ? row : new BudgetRow { EntityCode = row.EntityCode, FY = row.FY, Line = row.Line };
            if (!row.HasBudget && row.Line.Length == 0)
            {
                MessageBox.Show(FindForm(), "These purchase orders belong to agreements that have no \"GL / budget line\" filled in. Open those agreements and enter a budget line first.",
                    "Budgets", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var f = new BudgetForm(target, Year, !row.HasBudget || row.PoCount > 0))
                if (f.ShowDialog(FindForm()) == DialogResult.OK) { SelectYear(f.SavedYear); Reload(); }
                else if (row.HasBudget) Reload();   // the save may have failed because someone else changed it: show the latest
        }

        private void SelectYear(string fy)
        {
            if (fy == null || fy == Year) return;
            loading = true;
            if (!cmbYear.Items.Contains(fy)) cmbYear.Items.Insert(0, fy);
            cmbYear.SelectedItem = fy;
            loading = false;
        }
    }

    // Add a budget line, or change the amount / description / active state of an existing one
    public class BudgetForm : Form
    {
        public string SavedYear;
        private readonly BudgetRow existing;
        private readonly ComboBox cYear;
        private readonly TextBox tLine, tDesc, tAmount;
        private readonly CheckBox chkActive;
        private readonly Label lblErr;

        public BudgetForm(BudgetRow row, string year, bool lockLine)
        {
            existing = row != null && row.HasBudget ? row : null;
            bool isNew = existing == null;
            UserRules.StyleDialog(this, isNew ? "Add budget line" : "Edit budget line", new Size(480, isNew ? 440 : 490));
            int left = 28, w = 424, y = 20;
            Controls.Add(UserRules.Lbl(isNew ? "Add budget line" : "Edit budget line", Theme.F(14, FontStyle.Bold), Theme.Ink, left, y));
            y += 48;

            cYear = UserRules.Combo(this, "Financial year *", left, y, 140);
            foreach (var fy in Db.BudgetYears(Current.Entity.Code)) cYear.Items.Add(fy);
            string startYear = row != null && row.FY != null ? row.FY : year;
            if (!cYear.Items.Contains(startYear)) cYear.Items.Add(startYear);
            cYear.SelectedItem = startYear;
            cYear.Enabled = isNew && (row == null);   // a line prefilled from the list stays in the year being viewed
            y += 62;

            tLine = UserRules.Field(this, "Budget line *", left, y, w, row == null ? "" : row.Line, 150);
            if (lockLine)
            {
                tLine.ReadOnly = true;
                Controls.Add(UserRules.Lbl("Purchase orders use this line, so its name stays as it is.", Theme.F(8.5f), Theme.Ink3, left, y + 46));
            }
            y += 62;

            tDesc = UserRules.Field(this, "Description (optional)", left, y, w, row == null ? "" : row.Description, 250);
            y += 62;

            tAmount = UserRules.Field(this, "Budget amount (" + Current.Entity.Currency + ") *", left, y, 200, existing == null ? "" : existing.Amount.ToString("0.##", CultureInfo.InvariantCulture), 16);
            Controls.Add(UserRules.Lbl("Before tax, for the whole year.", Theme.F(8.5f), Theme.Ink3, left + 216, y + 24));
            y += 62;

            chkActive = new CheckBox { Text = "Active (inactive lines are hidden and not budgeted)", Checked = isNew || existing.IsActive, AutoSize = true, Font = Theme.F(9.5f), Location = new Point(left, y), BackColor = Color.Transparent };
            if (!isNew) { Controls.Add(chkActive); y += 34; }

            lblErr = new Label { Location = new Point(left, y), Size = new Size(w, 40), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblErr);

            int by = ClientSize.Height - 54;
            var ok = AppButton.Make(isNew ? "Add line" : "Save", "primary", null, 38, false, 110);
            ok.Location = new Point(ClientSize.Width - 28 - ok.Width, by);
            ok.Click += (s, e) => Save();
            var cancel = AppButton.Make("Cancel", "ghost", null, 38, false, 90);
            cancel.Location = new Point(ok.Left - 10 - cancel.Width, by);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = null; CancelButton = null;
        }

        private void Save()
        {
            lblErr.Text = "";
            string line = tLine.Text.Trim();
            if (line.Length == 0) { lblErr.Text = "Enter the budget line."; tLine.Focus(); return; }
            decimal? amount = PoUi.Dec(tAmount.Text);
            if (!amount.HasValue || amount.Value <= 0) { lblErr.Text = "Enter a budget amount above zero, for example 2500000."; tAmount.Focus(); return; }

            var b = new BudgetRow
            {
                Id = existing != null ? existing.Id : 0,
                RowVer = existing != null ? existing.RowVer : null,
                EntityCode = Current.Entity.Code,
                FY = (string)cYear.SelectedItem,
                Line = line,
                Description = tDesc.Text.Trim(),
                Amount = amount.Value,
                IsActive = existing == null || chkActive.Checked
            };
            try
            {
                Db.BudgetSave(b);
                SavedYear = b.FY;
                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex) { lblErr.Text = ex.Message; }
            catch (Exception ex) { lblErr.Text = "Could not save: " + ex.Message; }
        }
    }
}
