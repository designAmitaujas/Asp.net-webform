using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ProcurePay
{
    // Grey hint text inside a text box
    internal static class Cue
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void Set(TextBox tb, string text)
        {
            SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);   // EM_SETCUEBANNER
        }
    }

    // Small coloured status pill
    public class Badge : Control
    {
        private readonly Color back, fore;

        public Badge(string text, Color backColor, Color foreColor)
        {
            Text = text; back = backColor; fore = foreColor;
            Font = Theme.F(8.5f, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            int w = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, 22),
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 34;
            Size = new Size(w, 24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Gfx.RoundRect(r, 12))
            using (var br = new SolidBrush(back))
                g.FillPath(br, path);
            using (var dot = new SolidBrush(fore))
                g.FillEllipse(dot, 11, (Height - 6) / 2, 6, 6);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(22, 0, Width - 22, Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    // Round step marker (tick, cross or level number)
    public class Dot : Control
    {
        private readonly Color fill, fore;

        public Dot(string text, Color fillColor, Color foreColor)
        {
            Text = text; fill = fillColor; fore = foreColor;
            Font = Theme.F(9, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(26, 26);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(fill))
                g.FillEllipse(br, 0, 0, Width - 1, Height - 1);
            if (Text == "\u2713") Icons.Draw(g, "check", new Rectangle(6, 6, 14, 14), fore, 2.6f);
            else if (Text == "\u2715") Icons.Draw(g, "close", new Rectangle(6, 6, 14, 14), fore, 2.6f);
            else
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    public class AgreementDetailsPage : Panel
    {
        public event EventHandler BackRequested;
        public event Action<Agreement> EditRequested;
        public event Action<Agreement> PrintRequested;

        private const int BaseW = 900;   // all cards are built at this width, then stretch to the window
        private static readonly Color Line2 = Theme.Line2;
        private static readonly Color Grey = ColorTranslator.FromHtml("#E9EEF0");
        private static readonly AnchorStyles TL = AnchorStyles.Top | AnchorStyles.Left;
        private static readonly AnchorStyles TLR = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        private static readonly AnchorStyles TR = AnchorStyles.Top | AnchorStyles.Right;

        private readonly Agreement a;
        private string flashOk, flashError;

        public AgreementDetailsPage(Agreement agreement, string flash = null)
        {
            a = agreement;
            flashOk = flash;   // e.g. "Agreement PA/2026-27/0007 saved as draft." after the form
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Rebuild();
        }

        // ================= helpers
        private static Label L(string text, Font font, Color color, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(x, y)
            };
        }

        private static void Stack(Control parent, params Control[] topToBottom)
        {
            // WinForms docks the LAST added control highest, so we add in reverse
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                topToBottom[i].Dock = DockStyle.Top;
                parent.Controls.Add(topToBottom[i]);
            }
        }

        private static Panel Gap(int h)
        {
            return new Panel { Height = h, BackColor = Color.Transparent };
        }

        private static void BadgeColors(string status, out Color back, out Color fore)
        {
            switch (status)
            {
                case "Signed":
                case "Approved": back = Theme.GoodSoft; fore = Theme.Good; break;
                case "Pending Approval":
                case "Pending": back = Theme.WarnSoft; fore = Theme.Warn; break;
                case "Rejected":
                case "Cancelled": back = Theme.BadSoft; fore = Theme.Bad; break;
                default: back = Grey; fore = Theme.Ink2; break;
            }
        }

        private static Button MakeBtn(string text, string kind, int h)
        {
            string icon = null;
            if (text.StartsWith("Back")) icon = "back";
            else if (text.StartsWith("Export")) icon = "print";
            else if (text.StartsWith("Approve")) icon = "check";
            else if (text.StartsWith("Reject")) icon = "close";
            else if (text.StartsWith("Submit")) icon = "send";
            else if (text.StartsWith("Sign")) icon = "sign";
            else if (text.StartsWith("Reopen")) icon = "reopen";
            else if (text.StartsWith("Edit")) icon = "edit";
            else if (text.StartsWith("Cancel")) icon = "close";
            else if (text.StartsWith("Attach")) icon = "attach";
            return AppButton.Make(text, kind, icon, h);
        }

        // Bordered text box. For a multi-line box pass multi = true.
        private static Panel Input(int w, int h, bool multi, string cue, out TextBox textBox)
        {
            var outer = new Panel { Size = new Size(w, h), BackColor = Theme.Line };
            var inner = new Panel { Location = new Point(1, 1), Size = new Size(w - 2, h - 2), BackColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            var tb = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.F(9.5f), BackColor = Color.White };
            if (multi)
            {
                tb.Multiline = true;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.Location = new Point(8, 6);
                tb.Size = new Size(inner.Width - 16, inner.Height - 12);
                tb.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            }
            else
            {
                tb.Location = new Point(8, (h - 2 - 18) / 2);
                tb.Width = inner.Width - 16;
                tb.Anchor = TLR;
            }
            if (cue != null) tb.HandleCreated += (s, e) => Cue.Set(tb, cue);
            inner.Controls.Add(tb);
            outer.Controls.Add(inner);
            tb.Enter += (s, e) => outer.BackColor = Theme.Brand2;
            tb.Leave += (s, e) => outer.BackColor = Theme.Line;
            inner.Click += (s, e) => tb.Focus();
            textBox = tb;
            return outer;
        }

        // A white card with a gold heading; "fill" adds the content to the body and returns its height
        private CardPanel Card(string title, Func<Panel, int> fill)
        {
            var card = new CardPanel { Width = BaseW };
            var body = new Panel { Location = new Point(1, 40), Width = BaseW - 2, BackColor = Color.White, Anchor = TLR };
            card.Controls.Add(L(title, Theme.F(8.5f, FontStyle.Bold), Theme.Brand2, 18, 16));
            int h = fill(body);
            body.Height = h;
            card.Height = 40 + h + 14;
            card.Controls.Add(body);
            return card;
        }

        private void Defer()
        {
            BeginInvoke(new Action(Rebuild));   // rebuild after the current click has finished
        }

        // ================= whole page
        private void Rebuild()
        {
            SuspendLayout();
            foreach (var c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }

            var items = new List<Control>();
            if (flashOk != null) { items.Add(Banner(flashOk, true)); items.Add(Gap(12)); }
            if (flashError != null) { items.Add(Banner(flashError, false)); items.Add(Gap(12)); }
            flashOk = null; flashError = null;

            items.Add(BuildHeader());
            items.Add(Gap(12));
            items.Add(BuildWorkflowCard());
            items.Add(Gap(16));
            items.Add(BuildDocumentsCard());
            items.Add(Gap(16));

            foreach (var tab in AgreementSchema.Tabs)
            {
                var shown = tab.Fields.Where(f => AgreementSchema.HasValue(f, a) || f.Type == "vendor").ToList();
                if (shown.Count == 0) continue;
                items.Add(BuildFieldsCard(tab, shown));
                items.Add(Gap(16));
            }

            Stack(this, items.ToArray());
            ResumeLayout(true);
        }

        private Control Banner(string text, bool ok)
        {
            var box = new CardPanel
            {
                Width = BaseW,
                Height = 40,
                Padding = new Padding(1),
                BackColor = ok ? Theme.GoodSoft : Theme.BadSoft,
                BorderColor = ok ? Theme.GoodLine : Theme.BadLine
            };
            box.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                Padding = new Padding(42, 0, 0, 0),
                UseMnemonic = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.F(9.5f, FontStyle.Bold),
                ForeColor = ok ? Theme.Good : Theme.Bad,
                BackColor = box.BackColor
            });
            box.Controls[0].Paint += (s2, e2) =>
                Icons.Draw(e2.Graphics, ok ? "check" : "warn", new Rectangle(14, 11, 18, 18), ok ? Theme.Good : Theme.Bad, 2.1f);
            return box;
        }

        // ================= header: status, title, back / print buttons
        private Control BuildHeader()
        {
            var head = new Panel { Width = BaseW, Height = 84, BackColor = Color.Transparent };
            Color bc, fc;
            BadgeColors(a.Status, out bc, out fc);

            var badge = new Badge(a.Status, bc, fc) { Location = new Point(0, 0) };
            head.Controls.Add(badge);
            head.Controls.Add(L(AgreementRules.Stage(a), Theme.F(9), Theme.Ink2, badge.Right + 10, 3));
            head.Controls.Add(L(Current.Entity.Name + " · raised by " + a.CreatedBy + " · " + a.CreatedAt.ToString("dd MMM yyyy"),
                Theme.F(8.5f), Theme.Ink3, 0, 30));
            head.Controls.Add(L(a.Title, Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 50));

            var bBack = MakeBtn("Back to list", "ghost", 30);
            var bPrint = MakeBtn("Export / print PDF", "ghost", 30);
            bBack.Anchor = TR; bPrint.Anchor = TR;
            bBack.Location = new Point(BaseW - bBack.Width, 0);
            bPrint.Location = new Point(bBack.Left - 8 - bPrint.Width, 0);
            bBack.Click += (s, e) => { var h = BackRequested; if (h != null) h(this, EventArgs.Empty); };
            bPrint.Click += (s, e) => { var h = PrintRequested; if (h != null) h(a); };
            head.Controls.Add(bBack);
            head.Controls.Add(bPrint);
            return head;
        }

        // ================= approval workflow + action buttons
        private Control BuildWorkflowCard()
        {
            return Card("APPROVAL WORKFLOW", body =>
            {
                int y = 0;

                if (a.Approvals.Count == 0)
                {
                    string msg = a.Status == "Draft"
                        ? "Not yet submitted. On submission it routes through the approval matrix based on contract value."
                        : (a.Status == "Approved" ? "Auto-approved (no approval level applied)." : "No approval steps recorded.");
                    body.Controls.Add(L(msg, Theme.F(9), Theme.Ink3, 18, 0));
                    y = 26;
                }
                else
                {
                    var cur = a.Pending;
                    foreach (var s in a.Approvals.OrderBy(x => x.Level))
                    {
                        bool now = a.Status == "Pending Approval" && cur != null && cur.Level == s.Level;
                        body.Controls.Add(StepItem(body, s, now, y));
                        y += 60;
                    }
                    if (a.Status == "Signed")
                    {
                        body.Controls.Add(ExecutedItem(body, y));
                        y += 60;
                    }
                }

                // ---- cancelled / locked notices
                if (a.Status == "Cancelled")
                {
                    string when = a.CancelledAt.HasValue ? a.CancelledAt.Value.ToString("dd MMM yyyy HH:mm") : "";
                    var lbl = L("Cancelled by " + a.CancelledBy + " · " + when, Theme.F(9, FontStyle.Bold), Theme.Bad, 18, y + 4);
                    body.Controls.Add(lbl);
                    var why = L("Reason: " + a.CancelReason, Theme.F(9), Theme.Ink2, 18, y + 26);
                    why.AutoSize = false; why.Size = new Size(body.Width - 36, 36); why.Anchor = TLR;
                    body.Controls.Add(why);
                    y += 68;
                }
                else if (a.Status == "Signed")
                {
                    body.Controls.Add(L("Locked: a signed agreement cannot be edited. You can still attach documents.", Theme.F(9), Theme.Ink3, 18, y + 4));
                    y += 30;
                }

                // ---- approve / reject with remarks
                if (AgreementRules.CanApprove(a, Current.Role, AppSession.UserName))
                {
                    y += 6;
                    body.Controls.Add(L("Your remarks (optional to approve; required to reject)", Theme.F(8.5f, FontStyle.Bold), Theme.Ink2, 18, y));
                    y += 22;

                    TextBox remarks;
                    var box = Input(body.Width - 36, 64, true, null, out remarks);
                    box.Location = new Point(18, y);
                    box.Anchor = TLR;
                    body.Controls.Add(box);
                    y += 74;

                    var bApprove = MakeBtn("Approve L" + a.Pending.Level, "good", 34);
                    var bReject = MakeBtn("Reject", "bad", 34);
                    bApprove.Location = new Point(18, y);
                    bReject.Location = new Point(18 + bApprove.Width + 8, y);
                    bApprove.Click += (s, e) =>
                    {
                        AgreementWorkflow.Approve(a, AppSession.UserName, remarks.Text.Trim());
                        flashOk = "Approved.";
                        Defer();
                    };
                    bReject.Click += (s, e) =>
                    {
                        if (remarks.Text.Trim().Length == 0)
                        {
                            flashError = "Remarks are required to reject an agreement.";
                            Defer();
                            return;
                        }
                        AgreementWorkflow.Reject(a, AppSession.UserName, remarks.Text.Trim());
                        flashOk = "Agreement rejected.";
                        Defer();
                    };
                    body.Controls.Add(bApprove);
                    body.Controls.Add(bReject);
                    y += 34 + 14;
                }

                // ---- submit / sign / reopen / edit
                var btns = new List<Button>();
                if (AgreementRules.CanSubmit(a, Current.Role))
                {
                    var b = MakeBtn("Submit for approval", "primary", 34);
                    b.Click += (s, e) => { AgreementWorkflow.Submit(a); flashOk = "Submitted."; Defer(); };
                    btns.Add(b);
                }
                if (AgreementRules.CanSign(a, Current.Role))
                {
                    var b = MakeBtn("Sign & execute", "good", 34);
                    b.Click += (s, e) =>
                    {
                        if (MessageBox.Show("Sign and execute this agreement?", "ProcurePay",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        AgreementWorkflow.Sign(a, AppSession.UserName, Current.Role);
                        flashOk = "Agreement signed and executed.";
                        Defer();
                    };
                    btns.Add(b);
                }
                if (AgreementRules.CanReopen(a, Current.Role))
                {
                    var b = MakeBtn("Reopen to draft", "ghost", 34);
                    b.Click += (s, e) => { AgreementWorkflow.Reopen(a); flashOk = "Reopened to draft."; Defer(); };
                    btns.Add(b);
                }
                if (AgreementRules.CanEdit(a, Current.Role))
                {
                    var b = MakeBtn("Edit fields", "ghost", 34);
                    b.Click += (s, e) => { var h = EditRequested; if (h != null) h(a); };
                    btns.Add(b);
                }
                if (AgreementRules.CanCancel(a, Current.Role))
                {
                    var b = MakeBtn("Cancel agreement", "bad", 34);
                    b.Click += (s, e) =>
                    {
                        string reason;
                        using (var dlg = new CancelDialog(a))
                        {
                            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                            reason = dlg.Reason;
                        }
                        try { AgreementWorkflow.Cancel(a, AppSession.UserName, reason); flashOk = "Agreement cancelled."; }
                        catch (Exception ex) { flashError = "Could not cancel: " + ex.Message; }
                        Defer();
                    };
                    btns.Add(b);
                }
                if (btns.Count > 0)
                {
                    y += 4;
                    int x = 18;
                    foreach (var b in btns)
                    {
                        b.Location = new Point(x, y);
                        body.Controls.Add(b);
                        x += b.Width + 8;
                    }
                    y += 34 + 6;
                }
                return y;
            });
        }

        private Control StepItem(Panel body, ApprovalStep s, bool now, int y)
        {
            var item = new CardPanel
            {
                Location = new Point(18, y),
                Size = new Size(body.Width - 36, 52),
                Anchor = TLR,
                BackColor = now ? Theme.WarnSoft : Color.White,
                BorderColor = now ? Theme.WarnLine : Line2
            };

            Dot dot;
            if (s.Status == "Approved") dot = new Dot("\u2713", Theme.Good, Color.White);
            else if (s.Status == "Rejected") dot = new Dot("\u2715", Theme.Bad, Color.White);
            else if (now) dot = new Dot(s.Level.ToString(), Theme.Warn, Color.White);
            else dot = new Dot(s.Level.ToString(), Grey, Theme.Ink2);
            dot.Location = new Point(12, 13);
            item.Controls.Add(dot);

            var title = L(s.Label, Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 50, 8);
            item.Controls.Add(title);
            item.Controls.Add(L("· " + s.Role, Theme.F(9), Theme.Ink2, 50 + TextRenderer.MeasureText(s.Label, title.Font).Width, 9));

            string sub = s.Status == "Pending"
                ? "Awaiting approval"
                : s.Status == "Cancelled" ? "Not needed - the agreement was cancelled"
                : s.Status + " by " + s.ApprovedBy + " · " + (s.At.HasValue ? s.At.Value.ToString("dd MMM yyyy HH:mm") : "");
            if (!string.IsNullOrWhiteSpace(s.Remarks)) sub += " — “" + s.Remarks + "”";
            item.Controls.Add(L(sub, Theme.F(8.5f), Theme.Ink2, 50, 29));

            Color bc, fc;
            BadgeColors(s.Status, out bc, out fc);
            var badge = new Badge(s.Status, bc, fc) { Anchor = TR };
            badge.Location = new Point(item.Width - badge.Width - 12, 15);
            item.Controls.Add(badge);
            return item;
        }

        private Control ExecutedItem(Panel body, int y)
        {
            var item = new CardPanel { Location = new Point(18, y), Size = new Size(body.Width - 36, 52), Anchor = TLR, BorderColor = Line2 };
            var dot = new Dot("\u2713", Theme.Good, Color.White) { Location = new Point(12, 13) };
            item.Controls.Add(dot);
            item.Controls.Add(L("Executed", Theme.F(9.5f, FontStyle.Bold), Theme.Ink, 50, 8));
            item.Controls.Add(L("Signed by " + a.SignedBy + " (" + a.SignedRole + ") · " +
                (a.SignedAt.HasValue ? a.SignedAt.Value.ToString("dd MMM yyyy HH:mm") : ""), Theme.F(8.5f), Theme.Ink2, 50, 29));
            var badge = new Badge("Signed", Theme.GoodSoft, Theme.Good) { Anchor = TR };
            badge.Location = new Point(item.Width - badge.Width - 12, 15);
            item.Controls.Add(badge);
            return item;
        }

        // ================= documents
        private Control BuildDocumentsCard()
        {
            return Card("DOCUMENTS", body =>
            {
                int y = 0;
                bool canEdit = AgreementRules.CanCreate(Current.Role) && a.Status != "Cancelled";

                if (a.Documents.Count == 0)
                {
                    body.Controls.Add(L("No documents linked yet.", Theme.F(9), Theme.Ink3, 18, 0));
                    y = 28;
                }

                foreach (var d in a.Documents.ToList())
                {
                    var row = new Panel { Location = new Point(18, y), Size = new Size(body.Width - 36, 36), Anchor = TLR, BackColor = Color.White };
                    row.Paint += (s, e) =>
                    {
                        using (var pen = new Pen(Line2))
                            e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
                    };

                    bool hasUrl = !string.IsNullOrWhiteSpace(d.Url);
                    var name = L(d.Name, Theme.F(9.5f, hasUrl ? FontStyle.Underline : FontStyle.Regular), hasUrl ? Theme.Brand : Theme.Ink, 0, 9);
                    if (hasUrl) { name.Cursor = Cursors.Hand; name.Click += (s, e) => OpenDoc(d); }
                    row.Controls.Add(name);
                    row.Controls.Add(L("· " + (hasUrl ? "link" : "reference") + " · " + d.AddedBy, Theme.F(8.5f), Theme.Ink2,
                        TextRenderer.MeasureText(d.Name, name.Font).Width + 6, 10));

                    if (canEdit)
                    {
                        var rm = L("Remove", Theme.F(9, FontStyle.Bold | FontStyle.Underline), Theme.Bad, 0, 10);
                        rm.Cursor = Cursors.Hand;
                        rm.Anchor = TR;
                        rm.Location = new Point(row.Width - rm.PreferredWidth - 2, 10);
                        rm.Click += (s, e) => { a.Documents.Remove(d); AgreementStore.Save(a); Defer(); };
                        row.Controls.Add(rm);
                    }
                    body.Controls.Add(row);
                    y += 36;
                }

                if (canEdit)
                {
                    y += 10;
                    TextBox tbName, tbUrl;
                    var boxName = Input(280, 34, false, "Document name (e.g. Signed PDF)", out tbName);
                    var boxUrl = Input(340, 34, false, "Link / URL or network path (optional)", out tbUrl);
                    var bAttach = MakeBtn("Attach", "ghost", 34);
                    boxName.Location = new Point(18, y);
                    boxUrl.Location = new Point(18 + 280 + 8, y);
                    // the link box stretches with the window; Attach stays pinned to the right edge
                    Action place = () =>
                    {
                        bAttach.Location = new Point(body.Width - 18 - bAttach.Width, boxName.Top);
                        boxUrl.Width = Math.Max(140, bAttach.Left - 8 - boxUrl.Left);
                    };
                    place();
                    body.Resize += (s, e) => place();
                    bAttach.Click += (s, e) =>
                    {
                        if (tbName.Text.Trim().Length == 0)
                        {
                            flashError = "Document name is required.";
                            Defer();
                            return;
                        }
                        a.Documents.Add(new DocLink { Name = tbName.Text.Trim(), Url = tbUrl.Text.Trim(), AddedBy = AppSession.UserName });
                        AgreementStore.Save(a);
                        flashOk = "Document attached.";
                        Defer();
                    };
                    body.Controls.Add(boxName);
                    body.Controls.Add(boxUrl);
                    body.Controls.Add(bAttach);
                    y += 34;
                }
                return y;
            });
        }

        private void OpenDoc(DocLink d)
        {
            string u = (d.Url ?? "").Trim();
            if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                Process.Start(u);
            else
                MessageBox.Show("Location:\r\n\r\n" + u, d.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================= one card per tab (only fields that have a value)
        private Control BuildFieldsCard(TabDef tab, List<FieldDef> shown)
        {
            return Card(tab.Name.ToUpper(), body =>
            {
                int y = 0;
                const int valW = 440;
                var valFont = Theme.F(9.5f, FontStyle.Bold);

                foreach (var f in shown)
                {
                    string val = AgreementSchema.Display(f, a);
                    int h = Math.Max(20, TextRenderer.MeasureText(val, valFont, new Size(valW, 0),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);

                    var row = new Panel { Location = new Point(18, y), Size = new Size(body.Width - 36, h + 14), Anchor = TLR, BackColor = Color.White };
                    row.Paint += (s, e) =>
                    {
                        using (var pen = new Pen(Line2))
                            e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
                    };
                    row.Controls.Add(new Label
                    {
                        Text = f.Label,
                        Font = Theme.F(9.5f),
                        ForeColor = Theme.Ink2,
                        BackColor = Color.Transparent,
                        AutoSize = false,
                        UseMnemonic = false,
                        Location = new Point(0, 7),
                        Size = new Size(250, 20)
                    });
                    row.Controls.Add(new Label
                    {
                        Text = val,
                        Font = valFont,
                        ForeColor = Theme.Ink,
                        BackColor = Color.Transparent,
                        AutoSize = false,
                        UseMnemonic = false,
                        TextAlign = ContentAlignment.TopRight,
                        Anchor = TR,
                        Size = new Size(valW, h),
                        Location = new Point(row.Width - valW, 7)
                    });
                    body.Controls.Add(row);
                    y += h + 14;
                }
                return y;
            });
        }
    }
}