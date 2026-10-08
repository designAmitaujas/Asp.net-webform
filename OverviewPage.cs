using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // A white box with a thin border. Used for cards and tiles everywhere.
    public class CardPanel : Panel
    {
        public bool Dashed;
        public Color BorderColor = Theme.Line;

        public CardPanel()
        {
            BackColor = Color.White;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(BorderColor))
            {
                if (Dashed) pen.DashStyle = DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    public class OverviewPage : Panel
    {
        public event Action<Agreement> ActionClicked;

        private static readonly Color Line2 = Theme.Line2;
        private static readonly Color HoverBg = Theme.Hover;
        private static readonly Color DoneBorder = Theme.GoodLine;

        private class ActionItem
        {
            public string Text;
            public string Sub;
            public string Tone;   // warn | good | brand
            public Agreement A;
        }

        public OverviewPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Build();
        }

        // ---------- helpers
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

        // Stacks controls top to bottom. (WinForms docks the LAST added control highest, so we add in reverse.)
        private static void Stack(Control parent, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                topToBottom[i].Dock = DockStyle.Top;
                parent.Controls.Add(topToBottom[i]);
            }
        }

        private static Panel Gap()
        {
            return new Panel { Height = 16, BackColor = Color.Transparent };
        }

        private static void LayoutEven(Control row, List<CardPanel> boxes, int gap, int padX, int top, int height)
        {
            int n = boxes.Count;
            int w = (row.Width - 2 * padX - gap * (n - 1)) / n;
            if (w <= 0) return;
            for (int i = 0; i < n; i++)
                boxes[i].SetBounds(padX + i * (w + gap), top, w, height);
        }

        private static Panel Header(string text)
        {
            var h = new Panel { Height = 40, BackColor = Color.White };
            h.Controls.Add(L(text, Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 16, 13));
            h.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line))
                    e.Graphics.DrawLine(pen, 0, h.Height - 1, h.Width, h.Height - 1);
            };
            return h;
        }

        // ---------- page
        private void Build()
        {
            var list = AgreementStore.ForEntity(Current.Entity.Code);

            var actions = new List<ActionItem>();
            foreach (var a in list.OrderByDescending(x => x.CreatedAt))
            {
                string sub = a.Supplier + " · " + Fmt.Money(a.Value, a.Currency);

                if (a.Status == "Draft" && AgreementRules.CanSubmit(Current.Role))
                    actions.Add(new ActionItem { A = a, Text = "Submit " + a.No + " for approval", Sub = sub, Tone = "warn" });

                if (AgreementRules.CanApprove(a, Current.Role, AppSession.UserName))
                    actions.Add(new ActionItem { A = a, Text = "Approve " + a.No + " — " + a.PendingLabel, Sub = sub, Tone = "good" });

                if (AgreementRules.CanSign(a, Current.Role))
                    actions.Add(new ActionItem { A = a, Text = "Sign & execute " + a.No, Sub = sub, Tone = "warn" });
            }

            Stack(this,
                BuildKpis(list.Count,
                          list.Count(a => a.Status == "Signed"),
                          list.Count(a => a.Status == "Pending Approval"),
                          AgreementStore.SupplierCount(Current.Entity.Code)),
                Gap(),
                BuildActions(actions),
                Gap(),
                BuildProcess());
        }

        // ---------- four count tiles
        private Control BuildKpis(int total, int signed, int pending, int suppliers)
        {
            var row = new Panel { Height = 84, BackColor = Color.Transparent };
            string[] caps = { "Agreements", "Signed", "Awaiting approval", "Suppliers" };
            int[] nums = { total, signed, pending, suppliers };
            string[] kpiIcons = { "agreements", "check", "clock", "building" };
            Color[] kpiFore = { Theme.Brand2, Theme.Good, Theme.Warn, Theme.Brand };
            Color[] kpiBack = { Theme.BrandSoft, Theme.GoodSoft, Theme.WarnSoft, Theme.Bg };
            var boxes = new List<CardPanel>();

            for (int i = 0; i < 4; i++)
            {
                var box = new CardPanel();
                box.Controls.Add(L(caps[i], Theme.F(9), Theme.Ink2, 16, 12));
                box.Controls.Add(L(nums[i].ToString(), Theme.F(22, FontStyle.Bold), Theme.Ink, 14, 32));
                string kIcon = kpiIcons[i];
                Color kFore = kpiFore[i], kBack = kpiBack[i];
                box.Paint += (s2, e2) =>
                {
                    var g = e2.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var chip = new Rectangle(((CardPanel)s2).Width - 16 - 40, 22, 40, 40);
                    using (var path = Gfx.RoundRect(chip, 11))
                    using (var br = new SolidBrush(kBack))
                        g.FillPath(br, path);
                    Icons.Draw(g, kIcon, new Rectangle(chip.X + 10, chip.Y + 10, 20, 20), kFore);
                };
                row.Controls.Add(box);
                boxes.Add(box);
            }
            row.Resize += (s, e) => LayoutEven(row, boxes, 12, 0, 0, row.Height);
            return row;
        }

        // ---------- needs your action
        private Control BuildActions(List<ActionItem> actions)
        {
            const int rowH = 58;
            int bodyH = actions.Count == 0 ? 84 : actions.Count * rowH;

            var card = new CardPanel { Height = 40 + bodyH + 2, Padding = new Padding(1) };
            var body = new Panel { Height = bodyH, BackColor = Color.White };

            if (actions.Count == 0)
            {
                body.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseMnemonic = false,
                    Font = Theme.F(10),
                    ForeColor = Theme.Ink3,
                    Text = "Nothing waiting on you in " + Current.Entity.Name + "."
                });
            }
            else
            {
                Stack(body, actions.Select(a => BuildActionRow(a, rowH)).ToArray());
            }

            Stack(card, Header("NEEDS YOUR ACTION"), body);
            return card;
        }

        private Control BuildActionRow(ActionItem a, int height)
        {
            var row = new Panel { Height = height, BackColor = Color.White, Cursor = Cursors.Hand };
            Color tone = a.Tone == "good" ? Theme.Good : (a.Tone == "warn" ? Theme.Warn : Theme.Brand);

            var bar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = tone };
            var t = L(a.Text, Theme.F(10, FontStyle.Bold), Theme.Ink, 66, 10);
            var s = L(a.Sub, Theme.F(9), Theme.Ink2, 66, 31);
            string rIcon = a.Text.StartsWith("Approve") ? "check" : (a.Text.StartsWith("Sign") ? "sign" : "send");
            Color rBack = a.Tone == "good" ? Theme.GoodSoft : (a.Tone == "warn" ? Theme.WarnSoft : Theme.BrandSoft);
            row.Controls.Add(t);
            row.Controls.Add(s);
            row.Controls.Add(bar);

            row.Paint += (x, e) =>
            {
                var g = e.Graphics;
                using (var pen = new Pen(Line2))
                    g.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var chip = new Rectangle(20, (row.Height - 34) / 2, 34, 34);
                using (var path = Gfx.RoundRect(chip, 10))
                using (var br = new SolidBrush(rBack))
                    g.FillPath(br, path);
                Icons.Draw(g, rIcon, new Rectangle(chip.X + 8, chip.Y + 8, 18, 18), tone);
                Icons.Draw(g, "arrow", new Rectangle(row.Width - 38, (row.Height - 16) / 2, 16, 16), Theme.Ink3);
            };

            foreach (Control c in new Control[] { row, t, s })
            {
                c.Cursor = Cursors.Hand;
                c.MouseEnter += (x, e) => row.BackColor = HoverBg;
                c.MouseLeave += (x, e) => row.BackColor = Color.White;
                c.Click += (x, e) =>
                {
                    var h = ActionClicked;
                    if (h != null) h(a.A);
                };
            }
            return row;
        }

        // ---------- process steps
        private Control BuildProcess()
        {
            var card = new CardPanel { Height = 40 + 104 + 2, Padding = new Padding(1) };
            var body = new Panel { Height = 104, BackColor = Color.White };

            string[] picons = { "agreements", "po", "check", "invoices", "vouchers", "tally" };
            string[] names = { "Agreements", "Purchase Orders", "Milestones", "Invoices", "Vouchers", "Tally" };
            string[] states = { "built", "next", "next", "planned", "planned", "planned" };
            var boxes = new List<CardPanel>();

            for (int i = 0; i < 6; i++)
            {
                bool done = i == 0;
                var box = new CardPanel { Padding = new Padding(4, 10, 4, 0) };
                Color fore = done ? Theme.Good : Theme.Ink3;
                if (done) { box.BackColor = Theme.GoodSoft; box.BorderColor = DoneBorder; }
                else { box.Dashed = true; }

                Func<string, float, FontStyle, int, Label> mk = (text, size, style, h) => new Label
                {
                    Text = text,
                    Font = Theme.F(size, style),
                    ForeColor = fore,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Height = h,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseMnemonic = false
                };
                Stack(box, mk("", 8, FontStyle.Bold, 26), mk(names[i], 10, FontStyle.Regular, 22), mk(states[i], 8.5f, FontStyle.Regular, 18));
                Color iconCol = fore;
                string pic = picons[i];
                box.Paint += (s2, e2) =>
                    Icons.Draw(e2.Graphics, pic, new Rectangle(((CardPanel)s2).Width / 2 - 10, 10, 20, 20), iconCol);

                body.Controls.Add(box);
                boxes.Add(box);
            }
            body.Resize += (s, e) => LayoutEven(body, boxes, 8, 14, 14, 76);

            Stack(card, Header("PROCESS"), body);
            return card;
        }
    }
}