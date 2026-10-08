using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ProcurePay
{
    public class EntityRow
    {
        public string Code;
        public string Name;
        public string Currency;
        public int Users;        // people with access to this entity
        public int Vendors;
        public int Agreements;
    }

    // Add or edit one approval level (role, step name, and the value from which it applies)
    public class MatrixLevelForm : Form
    {
        public MatrixLevel Result;
        private readonly ComboBox cRole;
        private readonly TextBox tLabel, tMin;
        private readonly Label lblErr;

        public MatrixLevelForm(MatrixLevel existing)
        {
            UserRules.StyleDialog(this, existing == null ? "Add approval level" : "Edit approval level", new Size(480, 400));
            int left = 28, w = 424, y = 20;
            Controls.Add(UserRules.Lbl(existing == null ? "Add approval level" : "Edit approval level", Theme.F(14, FontStyle.Bold), Theme.Ink, left, y));
            y += 48;

            cRole = UserRules.Combo(this, "Approver role *", left, y, w);
            foreach (var r in Perm.Roles) cRole.Items.Add(r);
            if (existing != null && !cRole.Items.Contains(existing.Role)) cRole.Items.Add(existing.Role);
            cRole.SelectedItem = existing != null ? existing.Role : "Approver";
            y += 62;

            tLabel = UserRules.Field(this, "Step name * (shown on the agreement)", left, y, w, existing == null ? "" : existing.Label, 100);
            y += 62;

            tMin = UserRules.Field(this, "Applies from value *", left, y, 200, existing == null ? "0" : existing.MinValue.ToString("0.##", CultureInfo.InvariantCulture), 18);
            Controls.Add(UserRules.Lbl("Applies when the agreement value is at least this.\r\nUse 0 for every agreement.", Theme.F(8.5f), Theme.Ink3, left + 216, y + 20));
            y += 62;

            lblErr = new Label { Location = new Point(left, 296), Size = new Size(w, 36), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblErr);

            var ok = AppButton.Make(existing == null ? "Add level" : "Done", "primary", null, 38, false, 110);
            ok.Location = new Point(ClientSize.Width - 28 - ok.Width, 346);
            ok.Click += (s, e) => Accept(existing);
            var cancel = AppButton.Make("Cancel", "ghost", null, 38, false, 90);
            cancel.Location = new Point(ok.Left - 10 - cancel.Width, 346);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = null; CancelButton = null;
        }

        private void Accept(MatrixLevel existing)
        {
            lblErr.Text = "";
            string role = cRole.SelectedItem as string;
            if (role == null) { lblErr.Text = "Choose a role."; cRole.Focus(); return; }
            string label = tLabel.Text.Trim();
            if (label.Length == 0) { lblErr.Text = "Step name is required."; tLabel.Focus(); return; }
            decimal min;
            if (!decimal.TryParse(tMin.Text.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out min) || min < 0 || min > 9999999999999m)
            { lblErr.Text = "Enter a value of 0 or more, for example 500000."; tMin.Focus(); return; }
            Result = new MatrixLevel { Level = existing == null ? 0 : existing.Level, Role = role, Label = label, MinValue = Math.Round(min, 2) };
            DialogResult = DialogResult.OK;
        }
    }

    // Add a new entity or edit the name / currency of an existing one
    public class EntityForm : Form
    {
        private static readonly string[] Currencies = { "INR", "USD", "GBP", "EUR", "AED", "SGD", "AUD", "CAD" };
        private readonly EntityRow existing;
        private readonly TextBox tCode, tName;
        private readonly ComboBox cCur;
        private readonly Label lblErr;

        public EntityForm(EntityRow e)
        {
            existing = e;
            UserRules.StyleDialog(this, e == null ? "Add entity" : "Edit entity", new Size(480, 420));
            int left = 28, w = 424, y = 20;
            Controls.Add(UserRules.Lbl(e == null ? "Add entity" : "Edit entity", Theme.F(14, FontStyle.Bold), Theme.Ink, left, y));
            y += 48;

            tCode = UserRules.Field(this, e == null ? "Code * (2 to 10 letters or numbers, cannot be changed later)" : "Code", left, y, 200, e == null ? "" : e.Code, 10);
            tCode.CharacterCasing = CharacterCasing.Upper;
            tCode.ReadOnly = e != null;
            y += 62;

            tName = UserRules.Field(this, "Legal name *", left, y, w, e == null ? "" : e.Name, 150);
            y += 62;

            cCur = UserRules.Combo(this, "Currency *", left, y, 200);
            foreach (var c in Currencies) cCur.Items.Add(c);
            if (e != null && !cCur.Items.Contains(e.Currency)) cCur.Items.Add(e.Currency);
            cCur.SelectedItem = e != null ? e.Currency : "INR";
            if (e != null && e.Agreements > 0)
            {
                cCur.Enabled = false;
                Controls.Add(UserRules.Lbl("Currency is locked because this entity has agreements.", Theme.F(8.5f), Theme.Ink3, left + 216, y + 24));
            }
            y += 62;

            lblErr = new Label { Location = new Point(left, 316), Size = new Size(w, 36), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblErr);

            var save = AppButton.Make(e == null ? "Add entity" : "Save", "primary", null, 38, false, 110);
            save.Location = new Point(ClientSize.Width - 28 - save.Width, 366);
            save.Click += (s, ev) => Save();
            var cancel = AppButton.Make("Cancel", "ghost", null, 38, false, 90);
            cancel.Location = new Point(save.Left - 10 - cancel.Width, 366);
            cancel.Click += (s, ev) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = null; CancelButton = null;
        }

        private void Save()
        {
            lblErr.Text = "";
            string code = tCode.Text.Trim().ToUpperInvariant();
            string name = tName.Text.Trim();
            string cur = cCur.SelectedItem as string;
            if (existing == null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9]{2,10}$")) { lblErr.Text = "Code must be 2 to 10 letters or numbers, no spaces."; tCode.Focus(); return; }
                try { if (Db.EntityExists(code)) { lblErr.Text = "This code is already used."; tCode.Focus(); return; } }
                catch (Exception ex) { lblErr.Text = "Could not check: " + ex.Message; return; }
            }
            if (name.Length == 0) { lblErr.Text = "Legal name is required."; tName.Focus(); return; }
            if (cur == null) { lblErr.Text = "Choose a currency."; cCur.Focus(); return; }
            try
            {
                if (existing == null) Db.EntityCreate(code, name, cur); else Db.EntityUpdate(existing.Code, name, cur);
                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex) { lblErr.Text = ex.Message; }
            catch (Exception ex) { lblErr.Text = "Could not save: " + ex.Message; }
        }
    }
}
