using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    public partial class MainForm : Form
    {
        public bool SignedOut;

        private class NavDef
        {
            public string Key;
            public string Text;
            public string Icon;
            public bool Soon;
            public bool AdminOnly;
            public string Need;   // permission needed to see this item (null = everyone)
        }

        private static readonly NavDef[] NavItems =
        {
            new NavDef { Key = "overview", Icon = "overview",      Text = "Overview" },
            new NavDef { Key = "agreements", Icon = "agreements",    Text = "Purchase Agreements" },
            new NavDef { Key = "po", Icon = "po",            Text = "Purchase Orders" },
            new NavDef { Key = "invoices", Icon = "invoices",      Text = "Invoice Booking", Need = "invoice.view" },
            new NavDef { Key = "vouchers", Icon = "vouchers",      Text = "Payment Vouchers", Soon = true },
            new NavDef { Key = "budgets", Icon = "budgets",       Text = "Budgets", Need = "budget.view" },
            new NavDef { Key = "reports", Icon = "reports",       Text = "Reports", Soon = true },
            new NavDef { Key = "tally", Icon = "tally",         Text = "Tally Export", Soon = true },
            new NavDef { Key = "notifications", Icon = "notifications", Text = "Notifications" },
            new NavDef { Key = "masters", Icon = "masters",       Text = "Masters & Access", AdminOnly = true },
            new NavDef { Key = "audit", Icon = "audit",         Text = "Audit Log", AdminOnly = true }
        };


        private Panel sidebar, sideFooter, mainArea, topbar, content;
        private Label lblTitle, lblEntCount, lblWho, lblWhoSub, lblChip, lblCcy, lblRole;
        private ComboBox cmbEntity;
        private readonly List<NavItem> navLabels = new List<NavItem>();
        private List<EntityAccess> access;
        private string activeKey = "overview";
        private bool loading;
        private int unread;   // unread notifications for the signed-in user (shown on the sidebar)
        private System.Windows.Forms.Timer badgeTimer;   // checks for new notifications every 30 seconds
        private bool badgeBusy;
        private NotificationsPage notificationsPage;

        public MainForm()
        {
            InitializeComponent();
            AutoScaleMode = AutoScaleMode.None;
            BuildUi();
            LoadEntities();
            StartBadgeTimer();
        }

        private static Label Pill(string text, Color back, Color fore)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = Theme.F(8.5f),
                BackColor = back,
                ForeColor = fore,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(8, 4, 0, 0),
                UseMnemonic = false
            };
        }

        private void BuildUi()
        {
            Text = "ProcurePay";
            var ico = Theme.AppIcon(); if (ico != null) Icon = ico;
            Font = Theme.F(9.5f);
            BackColor = Theme.Bg;
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;

            // ================= right side: top bar + content
            mainArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Controls.Add(mainArea);

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(24) };
            mainArea.Controls.Add(content);

            topbar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White };
            mainArea.Controls.Add(topbar);
            topbar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line))
                    e.Graphics.DrawLine(pen, 0, topbar.Height - 1, topbar.Width, topbar.Height - 1);
            };

            lblTitle = new Label
            {
                AutoSize = true,
                Font = Theme.F(13, FontStyle.Bold),
                ForeColor = Theme.Ink,
                BackColor = Color.Transparent,
                Location = new Point(24, 15),
                UseMnemonic = false
            };
            topbar.Controls.Add(lblTitle);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 13, 18, 0),
                BackColor = Color.Transparent
            };
            topbar.Controls.Add(flow);

            // entity switcher box
            var sw = new Panel { Size = new Size(332, 30), BackColor = Theme.Line, Margin = new Padding(0) };
            var swIn = new Panel { Location = new Point(1, 1), Size = new Size(330, 28), BackColor = Color.White };
            sw.Controls.Add(swIn);
            lblChip = new Label
            {
                AutoSize = false,
                Size = new Size(30, 18),
                Location = new Point(6, 5),
                BackColor = Theme.BrandSoft,
                ForeColor = Theme.Brand,
                Font = Theme.F(8, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            cmbEntity = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = Theme.F(9.5f),
                Location = new Point(42, 2),
                Width = 214
            };
            lblCcy = new Label
            {
                AutoSize = false,
                Size = new Size(48, 18),
                Location = new Point(262, 5),
                ForeColor = Theme.Ink3,
                Font = Theme.F(8.5f),
                TextAlign = ContentAlignment.MiddleLeft
            };
            swIn.Controls.Add(lblChip);
            swIn.Controls.Add(cmbEntity);
            swIn.Controls.Add(lblCcy);
            cmbEntity.SelectedIndexChanged += (s, e) => { if (!loading) EntityChanged(); };
            flow.Controls.Add(sw);

            flow.Controls.Add(Pill("FY " + Current.FinancialYear(), Theme.Bg, Theme.Ink2));
            lblRole = Pill("", Theme.BrandSoft, Theme.AccentDark);
            flow.Controls.Add(lblRole);

            // ================= left side: navy sidebar
            sidebar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Brand };
            Controls.Add(sidebar);

            var mark = new Control { Location = new Point(18, 17), Size = new Size(34, 34) };
            mark.Paint += (s2, e2) =>
            {
                var g = e2.Graphics;
                g.Clear(Theme.Brand);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = Gfx.RoundRect(new Rectangle(0, 0, 33, 33), 9))
                using (var br = new SolidBrush(Theme.Gold))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, "P", Theme.F(13, FontStyle.Bold), new Rectangle(0, 0, 34, 34), Theme.NavyDark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            sidebar.Controls.Add(mark);
            sidebar.Controls.Add(new Label
            {
                Text = "ProcurePay",
                Font = Theme.F(11, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(60, 14)
            });
            lblEntCount = new Label
            {
                Font = Theme.F(8.5f),
                ForeColor = Theme.SideSub,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(61, 36)
            };
            sidebar.Controls.Add(lblEntCount);

            // footer: who is signed in + sign out
            sideFooter = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Theme.Brand };
            sidebar.Controls.Add(sideFooter);
            sideFooter.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.SideLine))
                    e.Graphics.DrawLine(pen, 12, 0, sideFooter.Width - 12, 0);
            };
            lblWho = new Label
            {
                Font = Theme.F(10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(18, 12)
            };
            lblWhoSub = new Label
            {
                Font = Theme.F(8.5f),
                ForeColor = Theme.SideSub,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(18, 33),
                UseMnemonic = false
            };
            var signOut = AppButton.Make("Sign out", "ghost", "signout", 28);
            signOut.Font = Theme.F(8.5f, FontStyle.Bold);
            signOut.Fit(28);
            signOut.Location = new Point(18, 56);
            signOut.Click += (s, e) => { SignedOut = true; Close(); };
            sideFooter.Controls.Add(lblWho);
            sideFooter.Controls.Add(lblWhoSub);
            sideFooter.Controls.Add(signOut);

            // make sure the fill areas take the leftover space
            mainArea.BringToFront();
            content.BringToFront();

            // coming back to this window (or closing a dialog) re-checks the unread count
            Activated += (s, e) => RefreshBadge();
        }

        private void LoadEntities()
        {
            access = Db.AccessFor(AppSession.UserName);
            loading = true;
            cmbEntity.Items.Clear();
            foreach (var a in access) cmbEntity.Items.Add(a.Entity);
            cmbEntity.SelectedIndex = 0;
            loading = false;

            lblEntCount.Text = access.Count + (access.Count == 1 ? " legal entity" : " legal entities");
            lblWho.Text = AppSession.FullName;
            EntityChanged();
        }

        private void EntityChanged()
        {
            var ent = (EntityInfo)cmbEntity.SelectedItem;
            var a = access.First(x => x.Entity == ent);
            Current.Entity = ent;
            Current.Role = a.Role;

            lblChip.Text = ent.Code;
            lblCcy.Text = ent.Currency;
            lblRole.Text = a.Role;
            lblWhoSub.Text = a.Role + " at " + ent.Code;

            BuildNav();
            var def = NavItems.First(x => x.Key == activeKey);
            if ((def.AdminOnly && !Current.CanEditMasters) || (def.Need != null && !Perm.Can(def.Need))) activeKey = "overview";
            ShowPage(activeKey);
        }

        // Reads the unread count from the database and updates the sidebar badge (keeps the old badge if the read failed)
        private void RefreshBadge()
        {
            int n = Db.UnreadCount(AppSession.UserName);
            if (n >= 0) SetBadge(n);
        }

        private void SetBadge(int n)
        {
            unread = n;
            foreach (var l in navLabels)
                if (l.Key == "notifications" && l.Badge != unread) { l.Badge = unread; l.Invalidate(); }
        }

        // Every 30 seconds: look for new notifications. The database read runs in the background so the window never freezes.
        private void StartBadgeTimer()
        {
            badgeTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            badgeTimer.Tick += (s, e) => PollNotifications();
            badgeTimer.Start();
            FormClosed += (s, e) => { badgeTimer.Stop(); badgeTimer.Dispose(); };
        }

        private void PollNotifications()
        {
            if (badgeBusy || string.IsNullOrEmpty(AppSession.UserName)) return;
            badgeBusy = true;
            string user = AppSession.UserName;
            System.Threading.Tasks.Task.Run(() => Db.UnreadCount(user)).ContinueWith(t =>
            {
                badgeBusy = false;
                try
                {
                    if (IsDisposed || !IsHandleCreated || t.IsFaulted || t.IsCanceled) return;
                    int n = t.Result;
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed || n < 0) return;
                        bool changed = n != unread;
                        SetBadge(n);
                        // if the Notifications page is open, show the new ones straight away
                        if (changed && activeKey == "notifications" && notificationsPage != null && !notificationsPage.IsDisposed)
                            notificationsPage.RefreshList();
                    }));
                }
                catch { /* window is closing */ }
            });
        }

        // A notification was clicked: switch to its entity if needed, then open the agreement it is about
        private void OpenNotification(NotificationRow n)
        {
            if (!n.CanOpen) return;
            string code = !string.IsNullOrEmpty(n.EntityCode) ? n.EntityCode : Current.Entity.Code;

            if (Current.Entity.Code != code)
            {
                var ent = access.Select(a => a.Entity).FirstOrDefault(x => x.Code == code);
                if (ent == null)
                {
                    MessageBox.Show("You no longer have access to entity " + code + ", so this item cannot be opened.", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                cmbEntity.SelectedItem = ent;   // runs EntityChanged: new role, new menu, new page
            }

            if (n.Module == "PO")
            {
                PurchaseOrder po;
                try { po = Db.LoadPos(code).FirstOrDefault(x => x.No == n.RecordId); }
                catch (Exception ex) { MessageBox.Show("Could not open the purchase order: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                if (po == null)
                {
                    MessageBox.Show("Purchase order " + n.RecordId + " could not be found.", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ShowPo(po);
                return;
            }

            Agreement ag;
            try { ag = AgreementStore.ForEntity(code).FirstOrDefault(x => x.No == n.RecordId); }
            catch (Exception ex) { MessageBox.Show("Could not open the agreement: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            if (ag == null)
            {
                MessageBox.Show("Agreement " + n.RecordId + " could not be found.", "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowAgreement(ag);
        }

        private void BuildNav()
        {
            foreach (var l in navLabels) { sidebar.Controls.Remove(l); l.Dispose(); }
            navLabels.Clear();

            int y = 80;
            foreach (var d in NavItems)
            {
                if (d.AdminOnly && !Current.CanEditMasters) continue;
                if (d.Need != null && !Perm.Can(d.Need)) continue;

                var item = new NavItem
                {
                    Text = d.Text,
                    Key = d.Key,
                    Icon = d.Icon,
                    Soon = d.Soon,
                    Active = d.Key == activeKey,
                    Location = new Point(12, y),
                    Size = new Size(212, 38),
                    Badge = d.Key == "notifications" ? unread : 0,
                    Cursor = d.Soon ? Cursors.Default : Cursors.Hand,
                    TabStop = !d.Soon
                };
                if (!d.Soon) item.Click += (s, e) => ShowPage(((NavItem)s).Key);
                sidebar.Controls.Add(item);
                navLabels.Add(item);
                y += 40;
                if (d.Key == "tally" || d.Key == "notifications") y += 8;   // group: workflow / tools / admin
            }
        }

        // Opens one agreement's details inside the main window
        private void ShowAgreement(Agreement ag, string flash = null)
        {
            activeKey = "agreements";
            lblTitle.Text = ag.No + " · " + ag.Type;
            StyleNav();

            foreach (var c in content.Controls.Cast<Control>().ToList())
            {
                content.Controls.Remove(c);
                c.Dispose();
            }

            var page = new AgreementDetailsPage(ag, flash);
            page.BackRequested += (s, e) => ShowPage("agreements");
            page.EditRequested += x => ShowForm(x);
            page.PrintRequested += x => MessageBox.Show("Print / PDF export is built in a later step.", "ProcurePay");
            content.Controls.Add(page);
        }

        // Opens the 8-tab form: ag == null is "New agreement", otherwise "Edit fields" for that agreement
        private void ShowForm(Agreement ag)
        {
            activeKey = "agreements";
            lblTitle.Text = ag == null ? "New agreement" : "Edit " + ag.No;
            StyleNav();

            foreach (var c in content.Controls.Cast<Control>().ToList())
            {
                content.Controls.Remove(c);
                c.Dispose();
            }

            var page = new AgreementFormPage(ag);
            page.Saved += (saved, msg) => ShowAgreement(saved, msg);
            page.Cancelled += (s, e) => { if (ag == null) ShowPage("agreements"); else ShowAgreement(ag); };
            content.Controls.Add(page);
        }

        // Opens one purchase order's details inside the main window
        private void ShowPo(PurchaseOrder po, string flash = null)
        {
            activeKey = "po";
            lblTitle.Text = po.No;
            StyleNav();
            ClearContent();
            var page = new PoDetailsPage(po, flash);
            page.BackRequested += (s, e) => ShowPage("po");
            page.EditRequested += p => ShowPoForm(p);
            page.OpenAgreementRequested += id =>
            {
                var ag = Db.LoadAgreements(Current.Entity.Code).FirstOrDefault(x => x.Id == id);
                if (ag != null) ShowAgreement(ag);
            };
            content.Controls.Add(page);
        }

        // po == null is "New purchase order", otherwise edit that draft
        private void ShowPoForm(PurchaseOrder po)
        {
            if (po == null && Db.AgreementsOpenForPo(Current.Entity.Code).Count == 0)
            {
                MessageBox.Show("There is no agreement to raise a purchase order against.\r\n\r\nA PO needs a signed agreement that is linked to a vendor and still has money left.",
                    "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            activeKey = "po";
            lblTitle.Text = po == null ? "New purchase order" : "Edit " + po.No;
            StyleNav();
            ClearContent();
            var page = new PoFormPage(po);
            page.Saved += (saved, msg) => ShowPo(Db.LoadPos(Current.Entity.Code).First(x => x.Id == saved.Id), msg);
            page.Cancelled += (s, e) => { if (po == null) ShowPage("po"); else ShowPo(po); };
            content.Controls.Add(page);
        }

        // Opens one invoice's details inside the main window
        private void ShowInvoice(Invoice inv, string flash = null)
        {
            activeKey = "invoices";
            lblTitle.Text = inv.No;
            StyleNav();
            ClearContent();
            var page = new InvoiceDetailsPage(inv, flash);
            page.BackRequested += (s, e) => ShowPage("invoices");
            page.EditRequested += i => ShowInvoiceForm(i);
            page.OpenPoRequested += id =>
            {
                var po = Db.LoadPos(Current.Entity.Code).FirstOrDefault(x => x.Id == id);
                if (po != null) ShowPo(po);
            };
            content.Controls.Add(page);
        }

        // inv == null is "New invoice", otherwise edit that draft
        private void ShowInvoiceForm(Invoice inv)
        {
            if (inv == null && Db.PosOpenForInvoice(Current.Entity.Code).Count == 0)
            {
                MessageBox.Show("There is no purchase order to book an invoice against.\r\n\r\nAn invoice needs an approved purchase order that still has a milestone with money left.",
                    "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            activeKey = "invoices";
            lblTitle.Text = inv == null ? "New invoice" : "Edit " + inv.No;
            StyleNav();
            ClearContent();
            var page = new InvoiceFormPage(inv);
            page.Saved += (saved, msg) => ShowInvoice(Db.LoadInvoice(Current.Entity.Code, saved.Id) ?? saved, msg);
            page.Cancelled += (s, e) => { if (inv == null) ShowPage("invoices"); else ShowInvoice(inv); };
            content.Controls.Add(page);
        }

        private void ClearContent()
        {
            foreach (var c in content.Controls.Cast<Control>().ToList())
            {
                content.Controls.Remove(c);
                c.Dispose();
            }
        }

        private void StyleNav()
        {
            foreach (var l in navLabels)
            {
                l.Active = l.Key == activeKey;
                l.Invalidate();
            }
        }

        // Shows a page in the content area. For now every page is a placeholder card.
        // Shows a page in the content area.
        private void ShowPage(string key)
        {
            activeKey = key;
            RefreshBadge();
            var def = NavItems.First(x => x.Key == key);
            lblTitle.Text = def.Text;
            StyleNav();

            foreach (var c in content.Controls.Cast<Control>().ToList())
            {
                content.Controls.Remove(c);
                c.Dispose();
            }

            if (key == "overview")
            {
                var page = new OverviewPage();
                page.ActionClicked += a => ShowAgreement(a);   // real agreement screen comes in Step 8
                content.Controls.Add(page);
                return;
            }
            if (key == "agreements")
            {
                var page = new AgreementsPage();
                page.OpenRequested += a => ShowAgreement(a);
                page.NewRequested += (s, e) => ShowForm(null);
                content.Controls.Add(page);
                return;
            }
            if (key == "po")
            {
                var page = new PurchaseOrdersPage();
                page.OpenRequested += p => ShowPo(p);
                page.NewRequested += (s, e) => ShowPoForm(null);
                content.Controls.Add(page);
                return;
            }
            if (key == "invoices" && Perm.Can("invoice.view"))
            {
                var page = new InvoicesPage();
                page.OpenRequested += i => ShowInvoice(i);
                page.NewRequested += (s, e) => ShowInvoiceForm(null);
                content.Controls.Add(page);
                return;
            }
            if (key == "notifications")
            {
                var page = new NotificationsPage();
                page.ReadStateChanged += (s, e) => RefreshBadge();
                page.OpenRequested += n => OpenNotification(n);
                notificationsPage = page;
                content.Controls.Add(page);
                return;
            }
            if (key == "budgets" && Perm.Can("budget.view"))
            {
                content.Controls.Add(new BudgetsPage());
                return;
            }
            if (key == "masters" && Current.CanEditMasters)
            {
                content.Controls.Add(new MastersPage());
                return;
            }
            // every other page is still a placeholder
            var card = new Panel { Dock = DockStyle.Top, Height = 130, BackColor = Color.White, Padding = new Padding(1) };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            card.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 0, 20, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.F(10.5f),
                ForeColor = Theme.Ink2,
                UseMnemonic = false,
                Text = def.Text + " - this screen is built in the next steps.\r\n\r\n" +
                       "Signed in as " + AppSession.FullName + "   |   Role: " + Current.Role +
                       "   |   Entity: " + Current.Entity.Name
            });
            content.Controls.Add(card);
        }
    }
}