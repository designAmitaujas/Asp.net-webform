using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcurePay
{
    public class UserRow
    {
        public string UserName;
        public string FullName;
        public bool IsActive;
        public string Access;   // e.g. "IN Accounts, US Approver"

        public override string ToString() { return UserName + "  -  " + FullName + (IsActive ? "" : "  (inactive)"); }
    }

    // Shared rules and small helpers for the two user dialogs
    internal static class UserRules
    {
        public static readonly Regex UserNameRx = new Regex(@"^[a-z0-9._-]{3,30}$");

        // At least 8 characters, with a letter and a digit, and not the same as the username
        public static string PasswordProblem(string password, string userName)
        {
            if (password.Length < 8) return "Password must be at least 8 characters.";
            if (!Regex.IsMatch(password, "[A-Za-z]") || !Regex.IsMatch(password, "[0-9]")) return "Password must contain a letter and a number.";
            if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase)) return "Password must not be the same as the username.";
            return null;
        }

        public static Label Lbl(string text, Font f, Color c, int x, int y)
        {
            return new Label { Text = text, Font = f, ForeColor = c, BackColor = Color.Transparent, AutoSize = true, UseMnemonic = false, Location = new Point(x, y) };
        }

        public static TextBox Field(Form owner, string caption, int x, int y, int width, string value, int max, bool password = false)
        {
            owner.Controls.Add(Lbl(caption.ToUpper(), Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y));
            var t = new TextBox { Location = new Point(x, y + 20), Width = width, Font = Theme.F(10), MaxLength = max, Text = value ?? "", BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = password };
            owner.Controls.Add(t);
            return t;
        }

        public static ComboBox Combo(Form owner, string caption, int x, int y, int width)
        {
            owner.Controls.Add(Lbl(caption.ToUpper(), Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y));
            var c = new ComboBox { Location = new Point(x, y + 20), Width = width, Font = Theme.F(10), DropDownStyle = ComboBoxStyle.DropDownList };
            owner.Controls.Add(c);
            return c;
        }

        public static void StyleDialog(Form f, string title, Size client)
        {
            f.Text = title;
            f.Icon = Theme.AppIcon();
            f.Font = Theme.F(9.5f);
            f.BackColor = Color.White;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = client;
        }
    }

    // Add one user, with first entity access. More access is edited later in Masters > Entity access.
    public class UserForm : Form
    {
        private readonly TextBox tUser, tName, tPass, tPass2;
        private readonly ComboBox cEntity, cRole;
        private readonly CheckBox cShow;
        private readonly Label lblErr;

        public UserForm()
        {
            UserRules.StyleDialog(this, "Add user", new Size(480, 500));
            int left = 28, w = 424, half = 204, right = 248, y = 20;

            Controls.Add(UserRules.Lbl("Add user", Theme.F(14, FontStyle.Bold), Theme.Ink, left, y));
            y += 44;

            tUser = UserRules.Field(this, "Username * (lowercase, used to sign in)", left, y, w, "", 30); y += 62;
            tName = UserRules.Field(this, "Full name *", left, y, w, "", 100); y += 62;

            cEntity = UserRules.Combo(this, "First entity *", left, y, half);
            cRole = UserRules.Combo(this, "Role *", right, y, half);
            foreach (var e in Db.EntityList()) cEntity.Items.Add(e);
            foreach (var r in Perm.Roles) cRole.Items.Add(r);
            if (cEntity.Items.Count > 0) cEntity.SelectedIndex = 0;
            if (cRole.Items.Count > 0) cRole.SelectedIndex = 0;
            y += 62;

            tPass = UserRules.Field(this, "Password *", left, y, half, "", 64, true);
            tPass2 = UserRules.Field(this, "Confirm password *", right, y, half, "", 64, true);
            y += 62;

            cShow = new CheckBox { Text = "Show password", Location = new Point(left, y), AutoSize = true, Font = Theme.F(9) };
            cShow.CheckedChanged += (s, e) => { tPass.UseSystemPasswordChar = !cShow.Checked; tPass2.UseSystemPasswordChar = !cShow.Checked; };
            Controls.Add(cShow);

            Controls.Add(UserRules.Lbl("At least 8 characters, with a letter and a number.", Theme.F(8.5f), Theme.Ink3, left, y + 26));

            lblErr = new Label { Location = new Point(left, 408), Size = new Size(w, 36), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblErr);

            var save = AppButton.Make("Add user", "primary", null, 38, false, 110);
            save.Location = new Point(ClientSize.Width - 28 - save.Width, 450);
            save.Click += (s, e) => Save();
            var cancel = AppButton.Make("Cancel", "ghost", null, 38, false, 90);
            cancel.Location = new Point(save.Left - 10 - cancel.Width, 450);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = null; CancelButton = null;
        }

        private bool Fail(string msg, Control focus) { lblErr.Text = msg; focus.Focus(); return false; }

        private bool Validate()
        {
            lblErr.Text = "";
            string u = tUser.Text.Trim().ToLowerInvariant();
            if (u.Length == 0) return Fail("Username is required.", tUser);
            if (!UserRules.UserNameRx.IsMatch(u)) return Fail("Username: 3 to 30 characters, lowercase letters, numbers, dot, dash or underscore.", tUser);
            if (Db.UserNameExists(u)) return Fail("This username is already taken.", tUser);
            if (tName.Text.Trim().Length == 0) return Fail("Full name is required.", tName);
            if (cEntity.SelectedItem == null) return Fail("Choose an entity.", cEntity);
            if (cRole.SelectedItem == null) return Fail("Choose a role.", cRole);
            string problem = UserRules.PasswordProblem(tPass.Text, u);
            if (problem != null) return Fail(problem, tPass);
            if (tPass.Text != tPass2.Text) return Fail("The two passwords do not match.", tPass2);
            return true;
        }

        private void Save()
        {
            if (!Validate()) return;
            try
            {
                Db.UserCreate(tUser.Text, tName.Text, tPass.Text, ((EntityInfo)cEntity.SelectedItem).Code, (string)cRole.SelectedItem);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { lblErr.Text = "Could not save: " + ex.Message; }
        }
    }

    // Set a new password for an existing user (admin action)
    public class PasswordResetForm : Form
    {
        private readonly UserRow user;
        private readonly TextBox tPass, tPass2;
        private readonly Label lblErr;

        public PasswordResetForm(UserRow u)
        {
            user = u;
            UserRules.StyleDialog(this, "Reset password", new Size(480, 330));
            int left = 28, w = 424, half = 204, right = 248, y = 20;

            Controls.Add(UserRules.Lbl("Reset password", Theme.F(14, FontStyle.Bold), Theme.Ink, left, y));
            Controls.Add(UserRules.Lbl(u.FullName + "  (" + u.UserName + ")", Theme.F(9.5f), Theme.Ink2, left, y + 30));
            y += 70;

            tPass = UserRules.Field(this, "New password *", left, y, half, "", 64, true);
            tPass2 = UserRules.Field(this, "Confirm password *", right, y, half, "", 64, true);
            y += 62;

            var show = new CheckBox { Text = "Show password", Location = new Point(left, y), AutoSize = true, Font = Theme.F(9) };
            show.CheckedChanged += (s, e) => { tPass.UseSystemPasswordChar = !show.Checked; tPass2.UseSystemPasswordChar = !show.Checked; };
            Controls.Add(show);

            lblErr = new Label { Location = new Point(left, 232), Size = new Size(w, 36), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblErr);

            var save = AppButton.Make("Reset password", "primary", null, 38, false, 130);
            save.Location = new Point(ClientSize.Width - 28 - save.Width, 276);
            save.Click += (s, e) => Save();
            var cancel = AppButton.Make("Cancel", "ghost", null, 38, false, 90);
            cancel.Location = new Point(save.Left - 10 - cancel.Width, 276);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = null; CancelButton = null;
        }

        private void Save()
        {
            lblErr.Text = "";
            string problem = UserRules.PasswordProblem(tPass.Text, user.UserName);
            if (problem != null) { lblErr.Text = problem; tPass.Focus(); return; }
            if (tPass.Text != tPass2.Text) { lblErr.Text = "The two passwords do not match."; tPass2.Focus(); return; }
            try { Db.UserResetPassword(user.UserName, tPass.Text); DialogResult = DialogResult.OK; }
            catch (Exception ex) { lblErr.Text = "Could not reset: " + ex.Message; }
        }
    }
}
