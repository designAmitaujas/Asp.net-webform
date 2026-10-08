using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    public class NotificationRow
    {
        public long Id;
        public string EntityCode;
        public string Title;
        public string Message;
        public string Module;     // which kind of item it is about, e.g. "Agreement"
        public string RecordId;   // that item's number, e.g. "PA/2026-27/0007"
        public bool IsRead;
        public DateTime CreatedAt;

        public bool CanOpen { get { return (Module == "Agreement" || Module == "PO") && !string.IsNullOrEmpty(RecordId); } }
    }

    // The signed-in user's notifications. Click a notification to open the item it is about (and mark it read);
    // click the dot at the left to mark one read or unread without opening it.
    public class NotificationsPage : Panel
    {
        // Raised after any change to read state so the sidebar badge can refresh
        public event EventHandler ReadStateChanged;
        // Raised (after the page has finished handling the click) when the user opens a notification's item
        public event Action<NotificationRow> OpenRequested;

        private bool unreadOnly = true;
        private long lastId;   // the notification last clicked or selected, kept across refreshes
        private CardPanel card;
        private Label lblEyebrow;
        private readonly List<PillTab> tabs = new List<PillTab>();

        public NotificationsPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            Reload(0);
        }

        private static Label L(string text, Font font, Color color, int x, int y)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = true, UseMnemonic = false, Location = new Point(x, y) };
        }

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
            card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            Controls.Add(card);

            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            lblEyebrow = L("", Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2);
            head.Controls.Add(lblEyebrow);
            head.Controls.Add(L("Notifications", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));

            var tabRow = new Panel { Height = 42, BackColor = Color.Transparent };
            int x = 0;
            foreach (var name in new[] { "Unread", "All" })
            {
                var tab = new PillTab(name) { Tag = name, Location = new Point(x, 6), On = name == "Unread" };
                tab.Click += (s, e) =>
                {
                    unreadOnly = (string)((PillTab)s).Tag == "Unread";
                    foreach (var t in tabs) { t.On = (string)t.Tag == (unreadOnly ? "Unread" : "All"); t.Invalidate(); }
                    Reload(0);
                };
                tabRow.Controls.Add(tab);
                tabs.Add(tab);
                x += tab.Width + 6;
            }

            Stack(this, head, tabRow);
        }

        private static string When(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "Just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
            if (t.Date == DateTime.Today) return "Today " + t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return "Yesterday " + t.ToString("HH:mm");
            return t.ToString("dd MMM yyyy");
        }

        private void Changed()
        {
            var h = ReadStateChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // Called by the main window when new notifications have arrived
        public void RefreshList()
        {
            if (IsDisposed) return;
            Reload(lastId);
        }

        // Rebuilds the card contents from the database. keepId re-selects that notification if it is still listed.
        private void Reload(long keepId)
        {
            foreach (var c in card.Controls.Cast<Control>().ToList()) { card.Controls.Remove(c); c.Dispose(); }

            List<NotificationRow> rows;
            int total;
            try
            {
                rows = Db.NotificationList(AppSession.UserName, unreadOnly);
                total = Db.NotificationTotal(AppSession.UserName);
            }
            catch (Exception ex)
            {
                lblEyebrow.Text = "";
                card.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Could not load notifications: " + ex.Message, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Bad, BackColor = Color.White });
                return;
            }
            int unread = Math.Max(0, Db.UnreadCount(AppSession.UserName));
            lblEyebrow.Text = (unread + " unread · " + total + " total").ToUpper();

            // toolbar
            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            var markAll = AppButton.Make("Mark all as read", "ghost", null, 36, false, 140);
            markAll.Location = new Point(14, 11);
            markAll.Enabled = unread > 0;
            markAll.Click += (s, e) => MarkAll();
            var hint = new Label { Text = "Click a notification to open it. Click the dot to mark it read or unread.", AutoSize = true, ForeColor = Theme.Ink3, Font = Theme.F(8.5f), BackColor = Color.Transparent };
            hint.Location = new Point(markAll.Right + 16, 22);
            bar.Controls.AddRange(new Control[] { markAll, hint });

            if (rows.Count == 0)
            {
                card.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = unreadOnly ? "You're all caught up. Select All to see earlier notifications." : "No notifications yet.",
                    TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10), ForeColor = Theme.Ink3, BackColor = Color.White
                });
                card.Controls.Add(bar);
                return;
            }

            var grid = MastersPage.MakeGrid();
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 44, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TITLE", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "MESSAGE", FillWeight = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ENTITY", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 80, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "WHEN", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 140, SortMode = DataGridViewColumnSortMode.NotSortable });

            int keepRow = -1;
            foreach (var n in rows)
            {
                int i = grid.Rows.Add(n.IsRead ? "\u25CB" : "\u25CF", n.Title, n.Message ?? "", n.EntityCode ?? "All", When(n.CreatedAt));
                grid.Rows[i].Tag = n;
                grid.Rows[i].Cells[0].ToolTipText = n.IsRead ? "Mark as unread" : "Mark as read";
                if (n.IsRead)
                {
                    foreach (DataGridViewCell cell in grid.Rows[i].Cells) cell.Style.ForeColor = Theme.Ink3;
                }
                else
                {
                    grid.Rows[i].Cells[0].Style.ForeColor = Theme.Brand2;
                    grid.Rows[i].Cells[1].Style.Font = Theme.F(9.5f, FontStyle.Bold);
                }
                if (n.Id == keepId) keepRow = i;
            }
            grid.ClearSelection();
            if (keepRow >= 0) { grid.Rows[keepRow].Selected = true; grid.CurrentCell = grid.Rows[keepRow].Cells[1]; }

            grid.CellMouseEnter += (s, e) => { grid.Cursor = e.RowIndex >= 0 ? Cursors.Hand : Cursors.Default; };
            grid.MouseLeave += (s, e) => { grid.Cursor = Cursors.Default; };
            grid.SelectionChanged += (s, e) =>
            {
                var sel = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as NotificationRow : null;
                if (sel != null) lastId = sel.Id;
            };
            grid.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var n = grid.Rows[e.RowIndex].Tag as NotificationRow;
                if (n == null) return;
                lastId = n.Id;
                if (e.ColumnIndex == 0) ToggleRead(n); else OpenItem(n);
            };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                var n = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as NotificationRow;
                if (n == null) return;
                e.Handled = true; e.SuppressKeyPress = true;
                OpenItem(n);
            };

            card.Controls.Add(grid);
            card.Controls.Add(bar);
        }

        private void ToggleRead(NotificationRow n)
        {
            try { Db.NotificationSetRead(AppSession.UserName, n.Id, !n.IsRead); }
            catch (Exception ex) { MessageBox.Show("Could not update: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Reload(n.Id);
            Changed();
        }

        // Marks the notification read, then asks the main window to open its item.
        // The hand-off is deferred so this page is not torn down while its own click handler is still running.
        private void OpenItem(NotificationRow n)
        {
            if (!n.IsRead)
            {
                try { Db.NotificationSetRead(AppSession.UserName, n.Id, true); }
                catch (Exception ex) { MessageBox.Show("Could not update: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                Changed();
            }
            var open = OpenRequested;
            if (!n.CanOpen || open == null)
            {
                if (!n.IsRead) Reload(n.Id);   // nothing to open: just show it as read
                return;
            }
            BeginInvoke(new Action(() => open(n)));
        }

        private void MarkAll()
        {
            try { Db.NotificationMarkAllRead(AppSession.UserName); }
            catch (Exception ex) { MessageBox.Show("Could not update: " + ex.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Reload(0);
            Changed();
        }
    }
}
