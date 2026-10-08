using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcurePay
{
    // Add / edit one vendor. Opens as a dialog from Masters & Access > Vendors.
    public class VendorForm : Form
    {
        private readonly VendorRow v;
        private readonly TextBox tName, tGst, tPan, tContact, tEmail, tPhone, tTerms, tAddress, tBank, tAcc, tIfsc, tMsme;
        private readonly CheckBox cMsme, cActive;
        private readonly Label lblErr;

        public int SavedId;

        public VendorForm(VendorRow existing, string entityCode)
        {
            v = existing ?? new VendorRow { EntityCode = entityCode, IsActive = true };
            bool isNew = v.Id == 0;

            Text = isNew ? "Add vendor" : "Edit vendor";
            Icon = Theme.AppIcon();
            Font = Theme.F(9.5f);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 612);

            int left = 28, right = 352, w2 = 300, y = 20;

            Controls.Add(Lbl(isNew ? "Add vendor" : "Edit vendor", Theme.F(14, FontStyle.Bold), Theme.Ink, left, y));
            Controls.Add(Lbl("Entity: " + v.EntityCode, Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, right + 150, y + 6));
            y += 44;

            tName = Field("Vendor name *", left, y, 624, v.Name, 200); y += 62;
            tGst = Field("GSTIN", left, y, w2, v.Gstin, 15); tPan = Field("PAN", right, y, w2, v.Pan, 10); y += 62;
            tContact = Field("Contact person", left, y, w2, v.ContactPerson, 100); tEmail = Field("Email", right, y, w2, v.Email, 150); y += 62;
            tPhone = Field("Phone", left, y, w2, v.Phone, 30); tTerms = Field("Payment terms (days)", right, y, w2, v.PaymentTermsDays.HasValue ? v.PaymentTermsDays.ToString() : "", 3); y += 62;
            tAddress = Field("Address", left, y, 624, v.Address, 400); y += 62;
            tBank = Field("Bank name", left, y, w2, v.BankName, 100); tAcc = Field("Account number", right, y, w2, v.AccountNo, 30); y += 62;
            tIfsc = Field("IFSC / SWIFT", left, y, w2, v.Ifsc, 15);

            cMsme = new CheckBox { Text = "MSME registered", Location = new Point(right, y + 22), AutoSize = true, Checked = v.IsMsme, Font = Theme.F(9.5f) };
            Controls.Add(cMsme);
            y += 62;

            tMsme = Field("MSME (Udyam) number", left, y, w2, v.MsmeNo, 30);
            tMsme.Enabled = cMsme.Checked;
            cMsme.CheckedChanged += (s, e) => { tMsme.Enabled = cMsme.Checked; if (!cMsme.Checked) tMsme.Text = ""; };

            cActive = new CheckBox { Text = "Active (shows in the agreement form)", Location = new Point(right, y + 22), AutoSize = true, Checked = v.IsActive, Font = Theme.F(9.5f) };
            Controls.Add(cActive);

            lblErr = new Label { Location = new Point(left, 540), Size = new Size(624, 20), ForeColor = Theme.Bad, Font = Theme.F(9, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblErr);

            var save = AppButton.Make(isNew ? "Add vendor" : "Save changes", "primary", null, 38, false, 130);
            save.Location = new Point(ClientSize.Width - 28 - save.Width, 564);
            save.Click += (s, e) => Save();
            var cancel = AppButton.Make("Cancel", "ghost", null, 38, false, 90);
            cancel.Location = new Point(save.Left - 10 - cancel.Width, 564);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = null; CancelButton = null;
        }

        private static Label Lbl(string text, Font f, Color c, int x, int y)
        {
            return new Label { Text = text, Font = f, ForeColor = c, BackColor = Color.Transparent, AutoSize = true, UseMnemonic = false, Location = new Point(x, y) };
        }

        private TextBox Field(string caption, int x, int y, int width, string value, int max)
        {
            Controls.Add(Lbl(caption.ToUpper(), Theme.F(8, FontStyle.Bold), Theme.Ink3, x, y));
            var t = new TextBox { Location = new Point(x, y + 20), Width = width, Font = Theme.F(10), MaxLength = max, Text = value ?? "", BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(t);
            return t;
        }

        private static readonly Regex GstRx = new Regex(@"^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$");
        private static readonly Regex PanRx = new Regex(@"^[A-Z]{5}\d{4}[A-Z]$");
        private static readonly Regex EmailRx = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        private bool Fail(string msg, Control focus) { lblErr.Text = msg; focus.Focus(); return false; }

        private bool Validate()
        {
            lblErr.Text = "";
            if (tName.Text.Trim().Length == 0) return Fail("Vendor name is required.", tName);
            if (Db.VendorNameExists(v.EntityCode, tName.Text, v.Id)) return Fail("A vendor with this name already exists in " + v.EntityCode + ".", tName);

            string gst = tGst.Text.Trim().ToUpper(), pan = tPan.Text.Trim().ToUpper();
            if (v.EntityCode == "IN")
            {
                if (gst.Length > 0 && !GstRx.IsMatch(gst)) return Fail("GSTIN must be 15 characters, e.g. 24ABCDE1234F1Z5.", tGst);
                if (pan.Length > 0 && !PanRx.IsMatch(pan)) return Fail("PAN must look like ABCDE1234F.", tPan);
                if (gst.Length > 0 && pan.Length > 0 && gst.Substring(2, 10) != pan) return Fail("PAN does not match the PAN inside the GSTIN.", tPan);
            }
            if (tEmail.Text.Trim().Length > 0 && !EmailRx.IsMatch(tEmail.Text.Trim())) return Fail("Email address is not valid.", tEmail);
            int d;
            if (tTerms.Text.Trim().Length > 0 && (!int.TryParse(tTerms.Text.Trim(), out d) || d < 0 || d > 365)) return Fail("Payment terms must be a number of days (0 to 365).", tTerms);
            if (cMsme.Checked && tMsme.Text.Trim().Length == 0) return Fail("Enter the MSME (Udyam) number, or untick MSME.", tMsme);
            return true;
        }

        private void Save()
        {
            if (!Validate()) return;
            try
            {
                int d;
                v.Name = tName.Text.Trim();
                v.Gstin = tGst.Text.Trim().ToUpper(); v.Pan = tPan.Text.Trim().ToUpper();
                v.ContactPerson = tContact.Text; v.Email = tEmail.Text; v.Phone = tPhone.Text; v.Address = tAddress.Text;
                v.BankName = tBank.Text; v.AccountNo = tAcc.Text; v.Ifsc = tIfsc.Text.Trim().ToUpper();
                v.IsMsme = cMsme.Checked; v.MsmeNo = tMsme.Text;
                v.PaymentTermsDays = int.TryParse(tTerms.Text.Trim(), out d) ? (int?)d : null;
                v.IsActive = cActive.Checked;
                SavedId = Db.VendorSave(v);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { lblErr.Text = "Could not save: " + ex.Message; }
        }
    }
}
