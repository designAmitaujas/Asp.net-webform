using System;
using System.Drawing;
using System.Windows.Forms;

namespace ProcurePay
{
    // Asks for the reason before an agreement is cancelled. Cancelling cannot be undone.
    public class CancelDialog : Form
    {
        private readonly TextBox tb;
        private readonly Label err;
        public string Reason;

        public CancelDialog(Agreement a)
        {
            Text = "Cancel agreement";
            Icon = Theme.AppIcon();
            Font = Theme.F(9.5f);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(500, 270);

            Controls.Add(new Label { Text = "Cancel " + a.No + "?", Font = Theme.F(13, FontStyle.Bold), ForeColor = Theme.Ink, AutoSize = true, UseMnemonic = false, Location = new Point(24, 20) });
            string warn = a.Status == "Signed"
                ? "This agreement is already signed. Cancelling it ends it for good: no new purchase orders can be raised against it."
                : "A cancelled agreement cannot be edited, reopened or approved again.";
            Controls.Add(new Label { Text = warn, ForeColor = Theme.Ink2, Location = new Point(24, 54), Size = new Size(452, 40), UseMnemonic = false });
            Controls.Add(new Label { Text = "REASON *", Font = Theme.F(8, FontStyle.Bold), ForeColor = Theme.Ink3, AutoSize = true, Location = new Point(24, 100) });

            tb = new TextBox { Location = new Point(24, 120), Size = new Size(452, 80), Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 500, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(tb);

            err = new Label { Location = new Point(24, 204), Size = new Size(452, 20), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), UseMnemonic = false };
            Controls.Add(err);

            var ok = AppButton.Make("Cancel agreement", "bad", "close", 36, false, 150);
            ok.Location = new Point(ClientSize.Width - 24 - ok.Width, 226);
            ok.Click += (s, e) =>
            {
                string r = tb.Text.Trim();
                if (r.Length < 5) { err.Text = "Please give a reason (at least 5 characters)."; tb.Focus(); return; }
                Reason = r;
                DialogResult = DialogResult.OK;
            };
            var keep = AppButton.Make("Keep agreement", "ghost", null, 36, false, 130);
            keep.Location = new Point(ok.Left - 8 - keep.Width, 226);
            keep.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(ok); Controls.Add(keep);
        }
    }
}
