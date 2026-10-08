using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    public class VendorRow
    {
        public int Id;
        public string EntityCode;
        public string Name;
        public bool IsActive;
        public string Gstin, Pan, ContactPerson, Email, Phone, Address, BankName, AccountNo, Ifsc, MsmeNo;
        public bool IsMsme;
        public int? PaymentTermsDays;
    }

    // Masters & Access (admin only). One tab per master; each tab is built in its own step.
    public class MastersPage : Panel
    {
        private static readonly string[] Sections = { "Vendors", "Users", "Entity access", "Approval matrix", "Entities", "Tally settings" };

        private string section = "Vendors";
        private readonly List<PillTab> tabs = new List<PillTab>();
        private CardPanel card;
        private Label lblEyebrow;

        public MastersPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            ShowSection("Vendors");
        }

        private static Label L(string text, Font font, Color color, int x, int y)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = true, UseMnemonic = false, Location = new Point(x, y) };
        }

        // Stacks controls top to bottom (WinForms docks the LAST added control highest, so we add in reverse).
        private static void Stack(Control parent, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                topToBottom[i].Dock = DockStyle.Top;
                parent.Controls.Add(topToBottom[i]);
            }
        }

        private void Build()
        {
            // card first (it fills the space left over)
            card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            Controls.Add(card);

            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            lblEyebrow = L("", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2);
            head.Controls.Add(lblEyebrow);
            head.Controls.Add(L("Masters & Access", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));

            var tabRow = new Panel { Height = 42, BackColor = Color.Transparent };
            int x = 0;
            foreach (var name in Sections)
            {
                var tab = new PillTab(name) { Tag = name, Location = new Point(x, 6) };
                tab.Click += (s, e) => ShowSection((string)((PillTab)s).Tag);
                tabRow.Controls.Add(tab);
                tabs.Add(tab);
                x += tab.Width + 6;
            }

            Stack(this, head, tabRow);
        }

        private void ShowSection(string name)
        {
            section = name;
            foreach (var t in tabs) { t.On = (string)t.Tag == section; t.Invalidate(); }

            foreach (var c in card.Controls.Cast<Control>().ToList()) { card.Controls.Remove(c); c.Dispose(); }

            if (section == "Vendors") { BuildVendors(); return; }
            if (section == "Users") { BuildUsers(); return; }
            if (section == "Entity access") { BuildAccess(); return; }
            if (section == "Approval matrix") { BuildMatrix(); return; }
            if (section == "Entities") { BuildEntities(); return; }
            if (section == "Tally settings") { BuildTally(); return; }

            lblEyebrow.Text = ("Admin · " + Current.Entity.Code).ToUpper();
            card.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = section + " - this screen is built in a later step.",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.F(10),
                ForeColor = Theme.Ink3,
                BackColor = Color.White
            });
        }

        // ---------- Vendors tab: list + add / edit / activate
        private void BuildVendors()
        {
            var rows = Db.VendorList(Current.Entity.Code);
            int active = rows.Count(r => r.IsActive);
            lblEyebrow.Text = (rows.Count + " vendors in " + Current.Entity.Code + " · " + active + " active").ToUpper();

            // toolbar (docked top inside the card)
            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var add = AppButton.Make("Add vendor", "primary", null, 36);
            add.Location = new Point(14, 11);
            add.Click += (s, e) => OpenVendorForm(null);
            var edit = AppButton.Make("Edit", "ghost", null, 36, false, 70);
            edit.Location = new Point(add.Right + 10, 11);
            var toggle = AppButton.Make("Deactivate", "ghost", null, 36, false, 110);
            toggle.Location = new Point(edit.Right + 8, 11);
            var hint = new Label { Text = "Double-click a row to edit", AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(8.5f), BackColor = Color.Transparent };
            hint.Location = new Point(toggle.Right + 16, 22);
            bar.Controls.AddRange(new Control[] { add, edit, toggle, hint });
            edit.Enabled = false; toggle.Enabled = false;

            var grid = new BufferedGrid
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = Theme.Line2,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 38,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.RowTemplate.Height = 46;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Hover, ForeColor = Theme.Ink3, Font = Theme.F(8, FontStyle.Bold),
                SelectionBackColor = Theme.Hover, SelectionForeColor = Theme.Ink3,
                Padding = new Padding(10, 0, 10, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White, ForeColor = Theme.Ink, Font = Theme.F(9.5f),
                SelectionBackColor = Theme.BrandSoft, SelectionForeColor = Theme.Ink,
                Padding = new Padding(10, 0, 10, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };

            Func<string, float, DataGridViewTextBoxColumn> col = (h, wt) =>
                new DataGridViewTextBoxColumn { HeaderText = h, FillWeight = wt, SortMode = DataGridViewColumnSortMode.NotSortable };
            grid.Columns.Add(col("VENDOR NAME", 100));
            grid.Columns.Add(col("GSTIN", 55));
            grid.Columns.Add(col("CONTACT", 60));
            grid.Columns.Add(col("MSME", 28));
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "STATUS", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });

            foreach (var v in rows)
            {
                int i = grid.Rows.Add(v.Name, v.Gstin, v.ContactPerson, v.IsMsme ? "Yes" : "", v.IsActive ? "Active" : "Inactive");
                grid.Rows[i].Tag = v;
                grid.Rows[i].Cells[4].Style.ForeColor = v.IsActive ? Theme.Good : Theme.Ink3;
                grid.Rows[i].Cells[4].Style.Font = Theme.F(9, FontStyle.Bold);
                if (!v.IsActive) grid.Rows[i].Cells[0].Style.ForeColor = Theme.Ink3;
            }
            grid.ClearSelection();

            Func<VendorRow> picked = () => grid.CurrentRow == null ? null : grid.CurrentRow.Tag as VendorRow;
            grid.SelectionChanged += (s, e) =>
            {
                var v = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as VendorRow : null;
                edit.Enabled = v != null; toggle.Enabled = v != null;
                if (v != null) { toggle.Text = v.IsActive ? "Deactivate" : "Activate"; toggle.Fit(36, 110); }
            };
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenVendorForm(grid.Rows[e.RowIndex].Tag as VendorRow); };
            edit.Click += (s, e) => { var v = picked(); if (v != null) OpenVendorForm(v); };
            toggle.Click += (s, e) =>
            {
                var v = picked(); if (v == null) return;
                string verb = v.IsActive ? "Deactivate" : "Activate";
                string note = v.IsActive ? "\r\n\r\nIt will no longer appear in the agreement form. Existing agreements are not changed." : "";
                if (MessageBox.Show(verb + " " + v.Name + "?" + note, "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { Db.VendorSetActive(v.Id, v.EntityCode, v.Name, !v.IsActive); }
                catch (Exception ex) { MessageBox.Show("Could not update: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                ShowSection("Vendors");
            };

            // grid first (fills), then toolbar (docks on top of it)
            if (rows.Count == 0)
                card.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "No vendors yet. Click Add vendor.", TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Ink3, BackColor = Color.White });
            else
                card.Controls.Add(grid);
            card.Controls.Add(bar);
        }

        // ---------- Users tab: list + add / reset password / deactivate
        internal static BufferedGrid MakeGrid()
        {
            var grid = new BufferedGrid
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = Theme.Line2,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 38,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.RowTemplate.Height = 46;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Hover, ForeColor = Theme.Ink3, Font = Theme.F(8, FontStyle.Bold),
                SelectionBackColor = Theme.Hover, SelectionForeColor = Theme.Ink3,
                Padding = new Padding(10, 0, 10, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White, ForeColor = Theme.Ink, Font = Theme.F(9.5f),
                SelectionBackColor = Theme.BrandSoft, SelectionForeColor = Theme.Ink,
                Padding = new Padding(10, 0, 10, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            return grid;
        }

        private static void Warn(string msg)
        {
            MessageBox.Show(msg, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void BuildUsers()
        {
            var rows = Db.UserList();
            int active = rows.Count(r => r.IsActive);
            lblEyebrow.Text = (rows.Count + " users · " + active + " active").ToUpper();

            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var add = AppButton.Make("Add user", "primary", null, 36);
            add.Location = new Point(14, 11);
            var reset = AppButton.Make("Reset password", "ghost", null, 36, false, 130);
            reset.Location = new Point(add.Right + 10, 11);
            var toggle = AppButton.Make("Deactivate", "ghost", null, 36, false, 110);
            toggle.Location = new Point(reset.Right + 8, 11);
            var hint = new Label { Text = "Entity access is changed in the Entity access tab", AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(8.5f), BackColor = Color.Transparent };
            hint.Location = new Point(toggle.Right + 16, 22);
            bar.Controls.AddRange(new Control[] { add, reset, toggle, hint });
            reset.Enabled = false; toggle.Enabled = false;

            var grid = MakeGrid();
            Func<string, float, DataGridViewTextBoxColumn> col = (h, wt) =>
                new DataGridViewTextBoxColumn { HeaderText = h, FillWeight = wt, SortMode = DataGridViewColumnSortMode.NotSortable };
            grid.Columns.Add(col("USERNAME", 45));
            grid.Columns.Add(col("FULL NAME", 60));
            grid.Columns.Add(col("ENTITY ACCESS", 110));
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "STATUS", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });

            foreach (var u in rows)
            {
                bool me = string.Equals(u.UserName, AppSession.UserName, StringComparison.OrdinalIgnoreCase);
                int i = grid.Rows.Add(u.UserName + (me ? "  (you)" : ""), u.FullName, u.Access.Length == 0 ? "No access" : u.Access, u.IsActive ? "Active" : "Inactive");
                grid.Rows[i].Tag = u;
                grid.Rows[i].Cells[3].Style.ForeColor = u.IsActive ? Theme.Good : Theme.Ink3;
                grid.Rows[i].Cells[3].Style.Font = Theme.F(9, FontStyle.Bold);
                if (!u.IsActive) { grid.Rows[i].Cells[0].Style.ForeColor = Theme.Ink3; grid.Rows[i].Cells[1].Style.ForeColor = Theme.Ink3; }
                if (u.Access.Length == 0) grid.Rows[i].Cells[2].Style.ForeColor = Theme.Warn;
            }
            grid.ClearSelection();

            Func<UserRow> picked = () => grid.CurrentRow == null ? null : grid.CurrentRow.Tag as UserRow;
            grid.SelectionChanged += (s, e) =>
            {
                var u = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as UserRow : null;
                reset.Enabled = u != null; toggle.Enabled = u != null;
                if (u != null) { toggle.Text = u.IsActive ? "Deactivate" : "Activate"; toggle.Fit(36, 110); }
            };

            add.Click += (s, e) =>
            {
                if (!Perm.Can("masters.edit")) { Warn("You do not have permission to edit masters."); return; }
                using (var f = new UserForm())
                    if (f.ShowDialog(FindForm()) == DialogResult.OK) ShowSection("Users");
            };

            reset.Click += (s, e) =>
            {
                var u = picked(); if (u == null) return;
                if (!Perm.Can("masters.edit")) { Warn("You do not have permission to edit masters."); return; }
                using (var f = new PasswordResetForm(u))
                    if (f.ShowDialog(FindForm()) == DialogResult.OK)
                        MessageBox.Show("Password reset for " + u.UserName + ". Give them the new password in person or by phone.", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            toggle.Click += (s, e) =>
            {
                var u = picked(); if (u == null) return;
                if (!Perm.Can("masters.edit")) { Warn("You do not have permission to edit masters."); return; }
                string verb = u.IsActive ? "Deactivate" : "Activate";
                string note = u.IsActive ? "\r\n\r\nThey will no longer be able to sign in. Their history and audit trail are kept." : "";
                if (MessageBox.Show(verb + " " + u.UserName + " (" + u.FullName + ")?" + note, "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { Db.UserSetActive(u.UserName, !u.IsActive); }
                catch (InvalidOperationException ex) { Warn(ex.Message); return; }
                catch (Exception ex) { MessageBox.Show("Could not update: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                ShowSection("Users");
            };

            if (rows.Count == 0)
                card.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "No users yet. Click Add user.", TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Ink3, BackColor = Color.White });
            else
                card.Controls.Add(grid);
            card.Controls.Add(bar);
        }

        // ---------- Entity access tab: pick a user, set a role for each entity
        private const string NoAccess = "No access";
        private string accessUser;   // remembered so the same user stays selected after saving

        private void BuildAccess()
        {
            var users = Db.UserList();
            var entities = Db.EntityList();
            lblEyebrow.Text = ("Role per entity · " + entities.Count + " entities").ToUpper();

            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var cap = new Label { Text = "USER", AutoSize = true, Font = Theme.F(8, FontStyle.Bold), ForeColor = Theme.Ink3, BackColor = Color.Transparent, Location = new Point(14, 23) };
            var cmbUser = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10), Width = 330, Location = new Point(58, 15) };
            foreach (var u in users) cmbUser.Items.Add(u);
            var save = AppButton.Make("Save access", "primary", null, 36, false, 120);
            save.Location = new Point(cmbUser.Right + 16, 11);
            var msg = new Label { AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(8.5f), BackColor = Color.Transparent, Location = new Point(save.Right + 16, 22) };
            bar.Controls.AddRange(new Control[] { cap, cmbUser, save, msg });

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
            body.Controls.Add(new Label
            {
                Text = "Pick a role for each entity this user works in. Choose \"" + NoAccess + "\" to remove an entity.",
                AutoSize = true, Font = Theme.F(9), ForeColor = Theme.Ink2, BackColor = Color.Transparent, Location = new Point(28, 18)
            });

            var combos = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
            int y = 56;
            foreach (var e in entities)
            {
                body.Controls.Add(new Label { Text = e.Code + "   ·   " + e.Name, AutoSize = true, Font = Theme.F(10, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = Color.Transparent, UseMnemonic = false, Location = new Point(28, y + 4) });
                body.Controls.Add(new Label { Text = "Currency " + e.Currency, AutoSize = true, Font = Theme.F(8.5f), ForeColor = Theme.Ink3, BackColor = Color.Transparent, Location = new Point(28, y + 26) });
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10), Width = 240, Location = new Point(440, y + 8) };
                cb.Items.Add(NoAccess);
                foreach (var r in Perm.Roles) cb.Items.Add(r);
                body.Controls.Add(cb);
                combos[e.Code] = cb;
                y += 64;
            }

            Action load = () =>
            {
                var u = cmbUser.SelectedItem as UserRow; if (u == null) return;
                accessUser = u.UserName;
                var map = Db.AccessFor(u.UserName).ToDictionary(a => a.Entity.Code, a => a.Role, StringComparer.OrdinalIgnoreCase);
                bool self = string.Equals(u.UserName, AppSession.UserName, StringComparison.OrdinalIgnoreCase);
                foreach (var kv in combos)
                {
                    string role;
                    if (map.TryGetValue(kv.Key, out role)) { if (!kv.Value.Items.Contains(role)) kv.Value.Items.Add(role); kv.Value.SelectedItem = role; }
                    else kv.Value.SelectedItem = NoAccess;
                    kv.Value.Enabled = !self;
                }
                save.Enabled = !self;
                msg.Text = self ? "You cannot change your own access. Ask another administrator." : "Changes apply the next time the user signs in.";
            };

            if (users.Count > 0)
            {
                int idx = users.FindIndex(x => x.UserName == accessUser);
                cmbUser.SelectedIndex = idx >= 0 ? idx : 0;
                load();
                cmbUser.SelectedIndexChanged += (s, e) => load();
            }
            else save.Enabled = false;

            save.Click += (s, e) =>
            {
                var u = cmbUser.SelectedItem as UserRow; if (u == null) return;
                if (!Perm.Can("masters.edit")) { Warn("You do not have permission to edit masters."); return; }

                var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in combos)
                {
                    string role = kv.Value.SelectedItem as string;
                    if (role != null && role != NoAccess) wanted[kv.Key] = role;
                }
                if (wanted.Count == 0)
                {
                    Warn("A user needs access to at least one entity.\r\n\r\nTo stop someone signing in, deactivate them in the Users tab.");
                    return;
                }

                var before = Db.AccessFor(u.UserName).ToDictionary(a => a.Entity.Code, a => a.Role, StringComparer.OrdinalIgnoreCase);
                bool newAdmin = wanted.Any(kv => kv.Value == "Administrator" && (!before.ContainsKey(kv.Key) || before[kv.Key] != "Administrator"));
                if (newAdmin && MessageBox.Show("Administrator can edit all masters, users and access.\r\n\r\nGive " + u.UserName + " the Administrator role?", "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                try { Db.UserSaveAccess(u.UserName, wanted); }
                catch (InvalidOperationException ex) { Warn(ex.Message); return; }
                catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                MessageBox.Show("Access saved for " + u.UserName + ".", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowSection("Entity access");
            };

            card.Controls.Add(body);
            card.Controls.Add(bar);
        }

        private static bool MayEditMasters()
        {
            if (Perm.Can("masters.edit")) return true;
            Warn("You do not have permission to edit masters.");
            return false;
        }

        // ---------- Approval matrix tab: edit the levels in memory, then Save matrix writes them all at once
        private void BuildMatrix()
        {
            var saved = Db.MatrixAll();
            var work = saved.Select(l => new MatrixLevel { Level = l.Level, Role = l.Role, Label = l.Label, MinValue = l.MinValue }).ToList();
            var counts = Db.RoleUserCounts();
            bool dirty = false;
            lblEyebrow.Text = ("Approval matrix · " + saved.Count + " levels").ToUpper();

            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var add = AppButton.Make("Add level", "primary", null, 36, false, 100);
            var edit = AppButton.Make("Edit", "ghost", null, 36, false, 70);
            var remove = AppButton.Make("Remove", "ghost", null, 36, false, 90);
            var up = AppButton.Make("Move up", "ghost", null, 36, false, 90);
            var down = AppButton.Make("Move down", "ghost", null, 36, false, 100);
            var save = AppButton.Make("Save matrix", "primary", null, 36, false, 120);
            var discard = AppButton.Make("Discard", "ghost", null, 36, false, 90);
            int x = 14;
            foreach (var b in new[] { add, edit, remove, up, down }) { b.Location = new Point(x, 11); x = b.Right + 8; }
            save.Location = new Point(x + 16, 11);
            discard.Location = new Point(save.Right + 8, 11);
            var state = new Label { AutoSize = true, Font = Theme.F(8.5f, FontStyle.Bold), BackColor = Color.Transparent, Location = new Point(discard.Right + 14, 22) };
            bar.Controls.AddRange(new Control[] { add, edit, remove, up, down, save, discard, state });

            var grid = MakeGrid();
            Func<string, float, DataGridViewTextBoxColumn> col = (h, wt) =>
                new DataGridViewTextBoxColumn { HeaderText = h, FillWeight = wt, SortMode = DataGridViewColumnSortMode.NotSortable };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "LEVEL", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 80, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(col("APPROVER ROLE", 45));
            grid.Columns.Add(col("STEP NAME", 70));
            grid.Columns.Add(col("APPLIES FROM", 50));
            grid.Columns.Add(col("ACTIVE USERS IN ROLE", 55));

            var tryPanel = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Theme.Hover };
            tryPanel.Controls.Add(new Label { Text = "TRY A VALUE", AutoSize = true, Font = Theme.F(8, FontStyle.Bold), ForeColor = Theme.Ink3, BackColor = Color.Transparent, Location = new Point(14, 10) });
            var tryBox = new TextBox { Location = new Point(14, 30), Width = 160, Font = Theme.F(10), Text = "100000", BorderStyle = BorderStyle.FixedSingle };
            var tryOut = new Label { Location = new Point(190, 26), Size = new Size(640, 40), Font = Theme.F(9.5f), ForeColor = Theme.Ink, BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            tryPanel.Controls.Add(tryBox);
            tryPanel.Controls.Add(tryOut);

            Func<int> sel = () => grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Index : -1;

            Action preview = () =>
            {
                decimal v;
                if (!decimal.TryParse(tryBox.Text.Replace(",", "").Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out v) || v < 0)
                { tryOut.Text = "Type an agreement value to see which levels apply."; return; }
                var hit = work.Select((l, i) => new { l, i }).Where(t => t.l.MinValue <= v)
                              .Select(t => "L" + (t.i + 1) + " " + t.l.Label + " (" + t.l.Role + ")").ToList();
                tryOut.Text = hit.Count == 0 ? "No level applies, so an agreement of this value is approved automatically."
                                             : "Approvals needed, in order:  " + string.Join("   >   ", hit);
            };

            Action updateButtons = () =>
            {
                int i = sel();
                edit.Enabled = i >= 0; remove.Enabled = i >= 0;
                up.Enabled = i > 0; down.Enabled = i >= 0 && i < work.Count - 1;
            };

            Action<int> refresh = null;
            refresh = selectRow =>
            {
                grid.Rows.Clear();
                for (int i = 0; i < work.Count; i++)
                {
                    var l = work[i];
                    int n; counts.TryGetValue(l.Role, out n);
                    int r = grid.Rows.Add("L" + (i + 1), l.Role, l.Label,
                        l.MinValue == 0 ? "Every agreement" : l.MinValue.ToString("N0") + " and above",
                        n == 0 ? "0  (nobody can approve)" : n.ToString());
                    grid.Rows[r].Cells[0].Style.Font = Theme.F(9.5f, FontStyle.Bold);
                    if (n == 0) { grid.Rows[r].Cells[4].Style.ForeColor = Theme.Warn; grid.Rows[r].Cells[4].Style.Font = Theme.F(9, FontStyle.Bold); }
                }
                grid.ClearSelection();
                if (selectRow >= 0 && selectRow < grid.Rows.Count) { grid.Rows[selectRow].Selected = true; grid.CurrentCell = grid.Rows[selectRow].Cells[0]; }
                save.Enabled = dirty; discard.Enabled = dirty;
                state.Text = dirty ? "Unsaved changes" : "Saved";
                state.ForeColor = dirty ? Theme.Warn : Theme.Good;
                preview(); updateButtons();
            };

            grid.SelectionChanged += (s, e) => updateButtons();
            tryBox.TextChanged += (s, e) => preview();

            add.Click += (s, e) =>
            {
                if (!MayEditMasters()) return;
                using (var f = new MatrixLevelForm(null))
                    if (f.ShowDialog(FindForm()) == DialogResult.OK) { work.Add(f.Result); dirty = true; refresh(work.Count - 1); }
            };
            edit.Click += (s, e) =>
            {
                int i = sel(); if (i < 0 || !MayEditMasters()) return;
                using (var f = new MatrixLevelForm(work[i]))
                    if (f.ShowDialog(FindForm()) == DialogResult.OK) { work[i] = f.Result; dirty = true; refresh(i); }
            };
            remove.Click += (s, e) =>
            {
                int i = sel(); if (i < 0 || !MayEditMasters()) return;
                if (work.Count == 1) { Warn("At least one approval level is required."); return; }
                if (MessageBox.Show("Remove level " + (i + 1) + " (" + work[i].Label + ")?\r\n\r\nLevels below it move up. Nothing is saved until you click Save matrix.", "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                work.RemoveAt(i); dirty = true; refresh(Math.Min(i, work.Count - 1));
            };
            up.Click += (s, e) =>
            {
                int i = sel(); if (i <= 0 || !MayEditMasters()) return;
                var t = work[i]; work[i] = work[i - 1]; work[i - 1] = t; dirty = true; refresh(i - 1);
            };
            down.Click += (s, e) =>
            {
                int i = sel(); if (i < 0 || i >= work.Count - 1 || !MayEditMasters()) return;
                var t = work[i]; work[i] = work[i + 1]; work[i + 1] = t; dirty = true; refresh(i + 1);
            };
            discard.Click += (s, e) =>
            {
                if (MessageBox.Show("Throw away your unsaved changes?", "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) ShowSection("Approval matrix");
            };
            save.Click += (s, e) =>
            {
                if (!MayEditMasters()) return;
                if (!work.Any(l => l.MinValue == 0) &&
                    MessageBox.Show("No level applies to every agreement.\r\n\r\nAgreements below " + work.Min(l => l.MinValue).ToString("N0") + " will be approved automatically with no review. Save anyway?",
                                    "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var nobody = work.Where(l => { int n; counts.TryGetValue(l.Role, out n); return n == 0; }).Select(l => l.Role).Distinct().ToList();
                if (nobody.Count > 0 &&
                    MessageBox.Show("No active user holds the role: " + string.Join(", ", nobody) + ".\r\n\r\nAgreements will get stuck at that level until someone is given the role in Entity access. Save anyway?",
                                    "ProcurePay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try { Db.MatrixSave(work); }
                catch (InvalidOperationException ex) { Warn(ex.Message); return; }
                catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                MessageBox.Show("Approval matrix saved.\r\n\r\nAgreements submitted from now on use the new matrix. Agreements already submitted keep their current approval steps.", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowSection("Approval matrix");
            };

            card.Controls.Add(grid);
            card.Controls.Add(tryPanel);
            card.Controls.Add(bar);
            refresh(work.Count > 0 ? 0 : -1);
        }

        // ---------- Entities tab: list + add / edit
        private void BuildEntities()
        {
            var rows = Db.EntityStats();
            lblEyebrow.Text = (rows.Count + " entities").ToUpper();

            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var add = AppButton.Make("Add entity", "primary", null, 36, false, 110);
            add.Location = new Point(14, 11);
            var edit = AppButton.Make("Edit", "ghost", null, 36, false, 70);
            edit.Location = new Point(add.Right + 10, 11);
            edit.Enabled = false;
            var hint = new Label { Text = "A new entity shows in the entity picker after users sign in again. You get Administrator access to entities you add.", AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(8.5f), BackColor = Color.Transparent, Location = new Point(edit.Right + 16, 22) };
            bar.Controls.AddRange(new Control[] { add, edit, hint });

            var grid = MakeGrid();
            Func<string, int, DataGridViewTextBoxColumn> fixedCol = (h, w) =>
                new DataGridViewTextBoxColumn { HeaderText = h, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable };
            grid.Columns.Add(fixedCol("CODE", 90));
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "LEGAL NAME", FillWeight = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(fixedCol("CURRENCY", 100));
            grid.Columns.Add(fixedCol("USERS", 90));
            grid.Columns.Add(fixedCol("VENDORS", 90));
            grid.Columns.Add(fixedCol("AGREEMENTS", 120));
            foreach (var r in rows)
            {
                int i = grid.Rows.Add(r.Code, r.Name, r.Currency, r.Users, r.Vendors, r.Agreements);
                grid.Rows[i].Tag = r;
                grid.Rows[i].Cells[0].Style.Font = Theme.F(9.5f, FontStyle.Bold);
            }
            grid.ClearSelection();
            grid.SelectionChanged += (s, e) => edit.Enabled = grid.SelectedRows.Count > 0;

            add.Click += (s, e) =>
            {
                if (!MayEditMasters()) return;
                using (var f = new EntityForm(null))
                    if (f.ShowDialog(FindForm()) == DialogResult.OK) ShowSection("Entities");
            };
            edit.Click += (s, e) =>
            {
                var r = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as EntityRow; if (r == null || !MayEditMasters()) return;
                using (var f = new EntityForm(r))
                    if (f.ShowDialog(FindForm()) == DialogResult.OK) ShowSection("Entities");
            };

            card.Controls.Add(grid);
            card.Controls.Add(bar);
        }

        // ---------- Tally settings tab: ledger names per entity, used later by the Tally XML export
        private void BuildTally()
        {
            var entities = Db.EntityList();
            lblEyebrow.Text = "Tally ledger names".ToUpper();

            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var cap = new Label { Text = "ENTITY", AutoSize = true, Font = Theme.F(8, FontStyle.Bold), ForeColor = Theme.Ink3, BackColor = Color.Transparent, Location = new Point(14, 23) };
            var cmbEnt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10), Width = 300, Location = new Point(66, 15) };
            foreach (var en in entities) cmbEnt.Items.Add(en.Code + "  -  " + en.Name);
            var save = AppButton.Make("Save settings", "primary", null, 36, false, 130);
            save.Location = new Point(cmbEnt.Right + 16, 11);
            var msg = new Label { AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(8.5f), BackColor = Color.Transparent, Location = new Point(save.Right + 16, 22), Text = "Settings are kept separately for each entity." };
            bar.Controls.AddRange(new Control[] { cap, cmbEnt, save, msg });

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
            body.Controls.Add(new Label
            {
                Text = "Ledger names must match Tally exactly, including spelling, spaces and capitals. The Tally export (a later step) uses these.",
                AutoSize = true, Font = Theme.F(9), ForeColor = Theme.Ink2, BackColor = Color.Transparent, Location = new Point(28, 18)
            });

            var boxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Db.TallyFields.Length; i++)
            {
                var f = Db.TallyFields[i];
                int colX = 28 + (i % 2) * 440;
                int rowY = 56 + (i / 2) * 82;
                body.Controls.Add(new Label { Text = f[1].ToUpper(), AutoSize = true, Font = Theme.F(8, FontStyle.Bold), ForeColor = Theme.Ink3, BackColor = Color.Transparent, UseMnemonic = false, Location = new Point(colX, rowY) });
                var tb = new TextBox { Location = new Point(colX, rowY + 20), Width = 400, Font = Theme.F(10), MaxLength = 100, BorderStyle = BorderStyle.FixedSingle };
                body.Controls.Add(tb);
                body.Controls.Add(new Label { Text = f[2], AutoSize = true, Font = Theme.F(8.5f), ForeColor = Theme.Ink3, BackColor = Color.Transparent, UseMnemonic = false, Location = new Point(colX, rowY + 48) });
                boxes[f[0]] = tb;
            }

            Action load = () =>
            {
                if (cmbEnt.SelectedIndex < 0) return;
                var vals = Db.TallyLoad(entities[cmbEnt.SelectedIndex].Code);
                foreach (var kv in boxes)
                {
                    string v;
                    kv.Value.Text = vals.TryGetValue(kv.Key, out v) ? v : (kv.Key == "tally.voucher_type" ? "Purchase" : "");
                }
            };

            if (entities.Count > 0)
            {
                int idx = entities.FindIndex(en => Current.Entity != null && en.Code == Current.Entity.Code);
                cmbEnt.SelectedIndex = idx >= 0 ? idx : 0;
                load();
                cmbEnt.SelectedIndexChanged += (s, e) => load();
            }
            else save.Enabled = false;

            save.Click += (s, e) =>
            {
                if (cmbEnt.SelectedIndex < 0 || !MayEditMasters()) return;
                var code = entities[cmbEnt.SelectedIndex].Code;
                var values = boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim(), StringComparer.OrdinalIgnoreCase);
                try { Db.TallySave(code, values); }
                catch (InvalidOperationException ex) { Warn(ex.Message); return; }
                catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                MessageBox.Show("Tally settings saved for " + code + ".", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            card.Controls.Add(body);
            card.Controls.Add(bar);
        }

        private void OpenVendorForm(VendorRow existing)
        {
            if (!Perm.Can("masters.edit")) { MessageBox.Show("You do not have permission to edit masters.", "ProcurePay"); return; }
            using (var f = new VendorForm(existing, Current.Entity.Code))
                if (f.ShowDialog(FindForm()) == DialogResult.OK) ShowSection("Vendors");
        }
    }
}
