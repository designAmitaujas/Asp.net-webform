using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ProcurePay
{
    public partial class LoginForm : Form
    {
        private TextBox txtUser, txtPass;
        private Label lblError;
        private AppButton btnLogin;
        private Point dragStartCursor, dragStartLocation;

        public LoginForm()
        {
            InitializeComponent();
            AutoScaleMode = AutoScaleMode.None;
            BuildUi();
        }

        private static Label Lbl(string text, Font font, Color fore, int x, int y, int w, int h)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = fore,
                BackColor = Color.Transparent,
                AutoSize = false,
                UseMnemonic = false,
                Location = new Point(x, y),
                Size = new Size(w, h)
            };
        }

        private TextBox MakeInput(Control parent, int y, bool password, string icon)
        {
            var outer = new Panel { Location = new Point(34, y), Size = new Size(340, 38), BackColor = Theme.Line };
            var inner = new Panel { Location = new Point(1, 1), Size = new Size(338, 36), BackColor = Color.White };
            var tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Theme.F(11),
                Location = new Point(40, 8),
                Width = 288,
                UseSystemPasswordChar = password
            };
            inner.Controls.Add(tb);
            outer.Controls.Add(inner);
            parent.Controls.Add(outer);
            inner.Paint += (s, e) =>
                Icons.Draw(e.Graphics, icon, new Rectangle(12, 9, 18, 18), tb.Focused ? Theme.Brand2 : Theme.Ink3);
            tb.Enter += (s, e) => { outer.BackColor = Theme.Brand2; inner.Invalidate(); };
            tb.Leave += (s, e) => { outer.BackColor = Theme.Line; inner.Invalidate(); };
            inner.Click += (s, e) => tb.Focus();
            return tb;
        }

        private void BuildUi()
        {
            Text = "ProcurePay - Sign in";
            var ico = Theme.AppIcon(); if (ico != null) Icon = ico;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 560);
            BackColor = Theme.Brand;
            Font = Theme.F(9.5f);
            DoubleBuffered = true;

            // white card in the middle
            var card = new Panel { Size = new Size(820, 440), BackColor = Color.White };
            card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);
            Controls.Add(card);

            // ---- left brand panel
            var left = new Panel { Location = new Point(0, 0), Size = new Size(410, 440), BackColor = Theme.Brand };
            card.Controls.Add(left);

            var mark = new Control { Location = new Point(34, 40), Size = new Size(46, 46) };
            mark.Paint += (s2, e2) =>
            {
                var g = e2.Graphics;
                g.Clear(Theme.Brand);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Gfx.RoundRect(new Rectangle(0, 0, 45, 45), 12))
                using (var br = new SolidBrush(Theme.Gold))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, "P", Theme.F(19, FontStyle.Bold), new Rectangle(0, 0, 46, 46), Theme.NavyDark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            left.Controls.Add(mark);

            left.Controls.Add(Lbl("ProcurePay", Theme.F(22, FontStyle.Bold), Color.White, 34, 100, 340, 40));
            left.Controls.Add(Lbl("Multi-entity procure-to-pay, from agreement to Tally posting.",
                Theme.F(10), Theme.LightText, 34, 148, 330, 44));

            string[] points =
            {
                "One platform, many legal entities",
                "Roles & access set per user, per entity",
                "Value-based approvals & separation of duties"
            };
            for (int i = 0; i < points.Length; i++)
            {
                var tick = new Control { Location = new Point(34, 217 + i * 34), Size = new Size(20, 20) };
                tick.Paint += (s2, e2) =>
                {
                    e2.Graphics.Clear(Theme.Brand);
                    Icons.Draw(e2.Graphics, "check", new Rectangle(1, 1, 16, 16), Theme.Gold, 2.2f);
                };
                left.Controls.Add(tick);
                left.Controls.Add(Lbl(points[i], Theme.F(9.5f), Theme.LightText, 62, 216 + i * 34, 330, 24));
            }

            // ---- right sign-in panel
            var right = new Panel { Location = new Point(410, 0), Size = new Size(410, 440), BackColor = Color.White };
            card.Controls.Add(right);

            right.Controls.Add(Lbl("Sign in", Theme.F(18, FontStyle.Bold), Theme.Ink, 34, 36, 340, 36));

            lblError = Lbl("", Theme.F(9), Theme.Bad, 34, 82, 340, 30);
            lblError.BackColor = Theme.BadSoft;
            lblError.TextAlign = ContentAlignment.MiddleLeft;
            lblError.Padding = new Padding(10, 0, 0, 0);
            lblError.Visible = false;
            right.Controls.Add(lblError);

            right.Controls.Add(Lbl("Username", Theme.F(8.5f, FontStyle.Bold), Theme.Ink2, 34, 126, 340, 18));
            txtUser = MakeInput(right, 148, false, "user");

            right.Controls.Add(Lbl("Password", Theme.F(8.5f, FontStyle.Bold), Theme.Ink2, 34, 196, 340, 18));
            txtPass = MakeInput(right, 218, true, "lock");

            btnLogin = AppButton.Make("Sign in", "primary", "arrow", 42, true, 340);
            btnLogin.Font = Theme.F(10, FontStyle.Bold);
            btnLogin.Location = new Point(34, 282);
            btnLogin.Click += (s, e) => DoLogin();
            right.Controls.Add(btnLogin);
            AcceptButton = btnLogin;   // Enter key = Sign in

            right.Controls.Add(Lbl("First run users: rahul, ashish, priya, nikhil, admin - password Admin@123",
                Theme.F(8.5f), Theme.Ink3, 34, 338, 340, 40));

            // close button (window has no title bar)
            var close = Lbl("×", Theme.F(16), Theme.Ink3, 366, 6, 32, 30);
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.Cursor = Cursors.Hand;
            close.MouseEnter += (s, e) => close.ForeColor = Theme.Bad;
            close.MouseLeave += (s, e) => close.ForeColor = Theme.Ink3;
            close.Click += (s, e) => Close();
            right.Controls.Add(close);

            // drag the window by the background or the brand panel
            MouseDown += DragStart; MouseMove += DragMove;
            left.MouseDown += DragStart; left.MouseMove += DragMove;

            Shown += (s, e) => txtUser.Focus();
        }

        private void DragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragStartCursor = Cursor.Position;
            dragStartLocation = Location;
        }

        private void DragMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Location = new Point(dragStartLocation.X + Cursor.Position.X - dragStartCursor.X,
                                 dragStartLocation.Y + Cursor.Position.Y - dragStartCursor.Y);
        }

        // dark blue gradient behind the card
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientRectangle.Width == 0 || ClientRectangle.Height == 0) return;
            using (var br = new LinearGradientBrush(ClientRectangle, Theme.NavyDark, Theme.Brand, 45f))
                e.Graphics.FillRectangle(br, ClientRectangle);
        }

        private void ShowError(string msg)
        {
            lblError.Text = msg;
            lblError.Visible = true;
        }

        private void DoLogin()
        {
            string user = txtUser.Text.Trim().ToLowerInvariant();
            if (user.Length == 0 || txtPass.Text.Length == 0)
            {
                ShowError("Enter your username and password.");
                return;
            }

            string fullName;
            try
            {
                btnLogin.Enabled = false;
                Cursor = Cursors.WaitCursor;
                fullName = Db.Authenticate(user, txtPass.Text);
            }
            catch (Exception ex)
            {
                ShowError("Cannot reach the database: " + ex.Message);
                return;
            }
            finally
            {
                btnLogin.Enabled = true;
                Cursor = Cursors.Default;
            }

            if (fullName == null)
            {
                ShowError("Invalid username or password.");
                txtPass.Clear();
                txtPass.Focus();
                return;
            }

            try
            {
                if (Db.AccessFor(user).Count == 0)
                {
                    ShowError("No entity access is assigned to this user. Ask an administrator.");
                    txtPass.Clear();
                    return;
                }
            }
            catch (Exception ex) { ShowError("Cannot reach the database: " + ex.Message); return; }

            AppSession.UserName = user;
            AppSession.FullName = fullName;
            AppSession.Role = "";
            Db.Audit("Auth", user, "Login");

            DialogResult = DialogResult.OK;
        }
    }
}