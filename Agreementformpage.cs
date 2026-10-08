using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ProcurePay
{
    // Suppliers per entity, from the Vendors table
    public static class VendorStore
    {
        public static List<VendorRow> ForEntity(string code) { return Db.VendorChoices(code); }
    }

    // A drop-down that ignores the mouse wheel unless it is open, so scrolling the page never changes a value by accident
    internal class QuietCombo : ComboBox
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (DroppedDown) { base.OnMouseWheel(e); return; }
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }
    }

    // The 8-tab agreement form. Used for both "New agreement" (agreement == null) and "Edit fields".
    // Every field comes from AgreementSchema.Tabs, so the form, the details screen and the web version stay in step.
    public class AgreementFormPage : Panel
    {
        public event Action<Agreement, string> Saved;   // the saved agreement + a message to show on the details screen
        public event EventHandler Cancelled;

        // one card per tab; it lays its fields out in two columns ("Full" fields span both)
        private class FieldsCard : CardPanel
        {
            public readonly List<Control> Cells = new List<Control>();
            public readonly List<bool> Full = new List<bool>();
        }

        private class Opt
        {
            public string Value, Text;
            public override string ToString() { return Text; }
        }

        private static readonly HashSet<string> CoreProps = new HashSet<string> { "Type", "Title", "VendorId", "Value", "Currency" };

        private readonly Agreement existing;
        private readonly bool isEdit;
        private readonly Dictionary<string, Func<string>> readers = new Dictionary<string, Func<string>>();
        private readonly Dictionary<string, int> tabOf = new Dictionary<string, int>();
        private readonly List<PillTab> tabButtons = new List<PillTab>();
        private readonly List<FieldsCard> cards = new List<FieldsCard>();
        private Panel scroll, errorHost, tabStrip;

        public AgreementFormPage(Agreement agreement)
        {
            existing = agreement;
            isEdit = agreement != null;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Build();
            ShowTab(0);
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

        // WinForms docks the LAST added control highest, so we add in reverse
        private static void Stack(Control parent, params Control[] topToBottom)
        {
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

        private static Button MakeBtn(string text, bool primary)
        {
            var b = AppButton.Make(text, primary ? "primary" : "ghost", primary ? "check" : "close", 36, false, 96);
            b.Font = Theme.F(9.5f, FontStyle.Bold);
            return ((AppButton)b).Fit(36, 96);
        }

        // ================= editors (bordered boxes that turn blue when focused)
        private static Panel Frame(int h, Padding pad, out Panel inner)
        {
            var outer = new Panel { Height = h, BackColor = Theme.Line, Padding = new Padding(1) };
            inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = pad };
            outer.Controls.Add(inner);
            return outer;
        }

        private static void Highlight(Panel outer, Control c)
        {
            c.Enter += (s, e) => outer.BackColor = Theme.Brand2;
            c.Leave += (s, e) => outer.BackColor = Theme.Line;
        }

        private static Panel TextFrame(int h, bool multi, string val, out TextBox textBox)
        {
            Panel inner;
            var outer = Frame(h, multi ? new Padding(8, 6, 8, 6) : new Padding(8, 7, 8, 0), out inner);
            var tb = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.F(9.5f), BackColor = Color.White, Dock = DockStyle.Fill, Text = val };
            if (multi) { tb.Multiline = true; tb.ScrollBars = ScrollBars.Vertical; tb.AcceptsReturn = true; }
            inner.Controls.Add(tb);
            Highlight(outer, tb);
            inner.Click += (s, e) => tb.Focus();
            textBox = tb;
            return outer;
        }

        private static Panel ComboFrame(List<Opt> items, int selected, out QuietCombo combo)
        {
            Panel inner;
            var outer = Frame(32, new Padding(6, 4, 6, 0), out inner);
            var cb = new QuietCombo
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = Theme.F(9.5f),
                Dock = DockStyle.Fill
            };
            cb.Items.AddRange(items.ToArray());
            cb.SelectedIndex = selected;
            inner.Controls.Add(cb);
            Highlight(outer, cb);
            combo = cb;
            return outer;
        }

        private string InitialValue(FieldDef f)
        {
            if (isEdit) return existing.Get(f.Prop) ?? "";
            switch (f.Prop)
            {
                case "Currency": return Current.Entity.Currency;
                case "GstRate": return Current.Entity.Code == "IN" ? "18" : "";
                case "RequestedBy": return AppSession.UserName;
            }
            return "";
        }

        // Builds the right editor for a field type and tells us how to read its value back as text
        private Control MakeEditor(FieldDef f, string val, out Func<string> read, out int height)
        {
            switch (f.Type)
            {
                case "textarea":
                    {
                        TextBox tb;
                        var box = TextFrame(76, true, val, out tb);
                        height = 76;
                        read = () => tb.Text.Trim();
                        return box;
                    }
                case "number":
                    {
                        TextBox tb;
                        var box = TextFrame(32, false, val, out tb);
                        tb.KeyPress += (s, e) =>
                        {
                            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',')
                                e.Handled = true;
                        };
                        height = 32;
                        read = () => tb.Text.Trim();
                        return box;
                    }
                case "date":
                    {
                        Panel inner;
                        var box = Frame(32, new Padding(6, 4, 6, 0), out inner);
                        var dtp = new DateTimePicker
                        {
                            Dock = DockStyle.Fill,
                            Format = DateTimePickerFormat.Custom,
                            CustomFormat = "dd MMM yyyy",
                            ShowCheckBox = true,
                            Font = Theme.F(9.5f)
                        };
                        DateTime d;
                        if (DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                        {
                            dtp.Value = d;
                            dtp.Checked = true;
                        }
                        else dtp.Checked = false;   // tick the box to set a date; untick to leave it blank
                        inner.Controls.Add(dtp);
                        Highlight(box, dtp);
                        height = 32;
                        read = () => dtp.Checked ? dtp.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
                        return box;
                    }
                case "bool":
                    {
                        var host = new Panel { Height = 32, BackColor = Color.White };
                        var cb = new CheckBox
                        {
                            Text = "Yes",
                            AutoSize = true,
                            Font = Theme.F(9.5f),
                            ForeColor = Theme.Ink,
                            Cursor = Cursors.Hand,
                            UseMnemonic = false,
                            Location = new Point(2, 6),
                            Checked = val == "true"
                        };
                        host.Controls.Add(cb);
                        height = 32;
                        read = () => cb.Checked ? "true" : "";
                        return host;
                    }
                case "vendor":
                    {
                        var vendors = VendorStore.ForEntity(Current.Entity.Code);
                        var items = new List<Opt> { new Opt { Value = "", Text = "\u2014 Select supplier \u2014" } };
                        items.AddRange(vendors.Select(v => new Opt { Value = v.Id.ToString(), Text = v.Name }));
                        // editing: the saved vendor may since have been deactivated, or be an old text-only supplier
                        if (!string.IsNullOrWhiteSpace(val) && !items.Any(x => x.Value == val))
                        {
                            int vid;
                            string nm = int.TryParse(val, out vid) ? Db.VendorName(vid) : (existing != null ? existing.Supplier : null);
                            items.Add(new Opt { Value = val, Text = (nm ?? val) + (val == "L" ? "  (not in vendor list)" : "  (inactive)") });
                        }
                        int idx = Math.Max(0, items.FindIndex(x => x.Value == val));
                        QuietCombo cb;
                        var box = ComboFrame(items, idx, out cb);
                        height = 32;
                        read = () => ((Opt)cb.SelectedItem).Value;
                        return box;
                    }
                case "select":
                    {
                        var items = new List<Opt>();
                        bool hasBlank = f.Options.Any(o => o == "" || o == "\u2014");
                        // optional lists get a blank entry so an untouched field stays empty (the currency always has a value)
                        if (!f.Required && f.Prop != "Currency" && !hasBlank) items.Add(new Opt { Value = "", Text = "\u2014" });
                        foreach (var o in f.Options)
                            items.Add(new Opt { Value = o, Text = o == "" ? "\u2014" : (f.Prop == "GstRate" ? o + "%" : o) });

                        int idx = items.FindIndex(x => x.Value == val);
                        if (idx < 0 && val == "") idx = items.FindIndex(x => x.Value == "" || x.Value == "\u2014");
                        if (idx < 0 && val != "") { items.Add(new Opt { Value = val, Text = val }); idx = items.Count - 1; }   // keep an older value that is no longer in the list
                        if (idx < 0) idx = 0;

                        QuietCombo cb;
                        var box = ComboFrame(items, idx, out cb);
                        height = 32;
                        read = () => ((Opt)cb.SelectedItem).Value;
                        return box;
                    }
                default:
                    {
                        TextBox tb;
                        var box = TextFrame(32, false, val, out tb);
                        height = 32;
                        read = () => tb.Text.Trim();
                        return box;
                    }
            }
        }

        // ================= screen
        private void Build()
        {
            // the scrolling area fills what is left, so it goes in first
            scroll = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true, Padding = new Padding(0, 0, 0, 16) };
            Controls.Add(scroll);

            Controls.Add(BuildFooter());

            for (int i = 0; i < AgreementSchema.Tabs.Length; i++)
            {
                var card = BuildCard(i);
                card.Visible = false;
                cards.Add(card);
                scroll.Controls.Add(card);
            }

            // heading
            string eyebrow = Current.Entity.Name + " \u00B7 " + (isEdit ? existing.No + " \u00B7 " + existing.Status : "number is assigned on save");
            if (isEdit && existing.Status == "Rejected") eyebrow += " \u00B7 saving resets it to draft";
            var head = new Panel { Height = 62, BackColor = Color.Transparent };
            head.Controls.Add(L(eyebrow, Theme.F(8.5f, FontStyle.Bold), Theme.Ink3, 0, 2));
            head.Controls.Add(L(isEdit ? "Edit agreement" : "New agreement", Theme.F(14, FontStyle.Bold), Theme.Ink, 0, 22));

            errorHost = new Panel { Height = 0, BackColor = Color.Transparent };

            // tab buttons (wrap onto a second row on a narrow window)
            tabStrip = new Panel { Height = 42, BackColor = Color.Transparent };
            for (int i = 0; i < AgreementSchema.Tabs.Length; i++)
            {
                int index = i;
                var tab = AgreementSchema.Tabs[i];
                bool req = tab.Fields.Any(f => f.Required);
                var pill = new PillTab(tab.Name + (req ? " *" : ""));
                pill.Click += (s, e) => ShowTab(index);
                tabStrip.Controls.Add(pill);
                tabButtons.Add(pill);
            }
            tabStrip.Resize += (s, e) => LayoutTabs();
            LayoutTabs();

            Stack(this, head, errorHost, tabStrip, Gap(6));
        }

        private void LayoutTabs()
        {
            int w = tabStrip.ClientSize.Width;
            int x = 0, row = 0;
            foreach (var t in tabButtons)
            {
                if (x > 0 && x + t.Width > w) { x = 0; row++; }
                t.Location = new Point(x, row * 36 + 6);
                x += t.Width + 6;
            }
            int h = (row + 1) * 36 + 6;
            if (tabStrip.Height != h) tabStrip.Height = h;
        }

        private FieldsCard BuildCard(int tabIndex)
        {
            var tab = AgreementSchema.Tabs[tabIndex];
            var card = new FieldsCard { Dock = DockStyle.Top, Height = 100 };
            card.Controls.Add(L(tab.Name.ToUpper(), Theme.F(8.5f, FontStyle.Bold), Theme.Brand2, 18, 16));

            foreach (var f in tab.Fields)
            {
                Func<string> read;
                int editorH;
                var editor = MakeEditor(f, InitialValue(f), out read, out editorH);
                readers[f.Prop] = read;
                tabOf[f.Prop] = tabIndex;

                var label = new Label
                {
                    Text = f.Label + (f.Required ? " *" : ""),
                    Font = Theme.F(8.5f, FontStyle.Bold),
                    ForeColor = Theme.Ink2,
                    BackColor = Color.Transparent,
                    UseMnemonic = false,
                    Height = 22,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var cell = new Panel { BackColor = Color.White, Height = 22 + editorH + 14 };
                Stack(cell, label, editor);
                card.Cells.Add(cell);
                card.Full.Add(f.Full);
                card.Controls.Add(cell);
            }

            card.Resize += (s, e) => LayoutCard(card);
            LayoutCard(card);
            return card;
        }

        // Two columns; "Full" fields take the whole row
        private static void LayoutCard(FieldsCard card)
        {
            const int padX = 18, gap = 20, top = 44;
            int w = card.ClientSize.Width;
            int colW = (w - 2 * padX - gap) / 2;
            int y = top, rowH = 0, placed = 0;

            for (int i = 0; i < card.Cells.Count; i++)
            {
                var c = card.Cells[i];
                if (card.Full[i])
                {
                    if (placed == 1) { y += rowH; rowH = 0; placed = 0; }
                    c.SetBounds(padX, y, w - 2 * padX, c.Height);
                    y += c.Height;
                    continue;
                }
                if (placed == 0)
                {
                    c.SetBounds(padX, y, colW, c.Height);
                    rowH = c.Height;
                    placed = 1;
                }
                else
                {
                    c.SetBounds(padX + colW + gap, y, colW, c.Height);
                    rowH = Math.Max(rowH, c.Height);
                    y += rowH; rowH = 0; placed = 0;
                }
            }
            if (placed == 1) y += rowH;

            int h = y + 8;
            if (card.Height != h) card.Height = h;
        }

        private Panel BuildFooter()
        {
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Bg };
            foot.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line))
                    e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0);
            };

            var note = new Label
            {
                Text = "Fields marked * are required: agreement type, title, contract value and supplier.",
                Font = Theme.F(8.5f),
                ForeColor = Theme.Ink3,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
                Location = new Point(0, 14),
                Height = 34
            };
            var cancel = MakeBtn("Cancel", false);
            var save = MakeBtn(isEdit ? "Save changes" : "Save as draft", true);
            cancel.Click += (s, e) => BeginInvoke(new Action(() => { var h = Cancelled; if (h != null) h(this, EventArgs.Empty); }));
            save.Click += (s, e) => Save();

            foot.Controls.Add(note);
            foot.Controls.Add(cancel);
            foot.Controls.Add(save);
            foot.Resize += (s, e) =>
            {
                save.Location = new Point(foot.Width - save.Width, 14);
                cancel.Location = new Point(save.Left - 8 - cancel.Width, 14);
                note.Width = Math.Max(100, cancel.Left - 16);
            };
            return foot;
        }

        private void ShowTab(int index)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].Visible = i == index;
                tabButtons[i].On = i == index;
                tabButtons[i].Invalidate();
            }
            scroll.AutoScrollPosition = new Point(0, 0);
        }

        // ================= validation + save
        private void ShowErrors(List<KeyValuePair<int, string>> errors)
        {
            foreach (var c in errorHost.Controls.Cast<Control>().ToList()) { errorHost.Controls.Remove(c); c.Dispose(); }
            if (errors.Count == 0) { errorHost.Height = 0; return; }

            int h = 22 + errors.Count * 19;
            var box = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = h,
                Padding = new Padding(1),
                BackColor = Theme.BadSoft,
                BorderColor = Theme.BadLine
            };
            box.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 10, 14, 6),
                Font = Theme.F(9.5f),
                ForeColor = Theme.Bad,
                BackColor = Theme.BadSoft,
                UseMnemonic = false,
                Text = string.Join("\r\n", errors.Select(x => "\u2022 " + x.Value + "  (" + AgreementSchema.Tabs[x.Key].Name + " tab)"))
            });
            errorHost.Controls.Add(box);
            errorHost.Height = h + 12;
        }

        private void Save()
        {
            // read every field back as text
            var vals = new Dictionary<string, string>();
            foreach (var kv in readers)
            {
                string v = kv.Value() ?? "";
                vals[kv.Key] = v == "\u2014" ? "" : v;
            }

            // all the rules live in AgreementValidator; here we only map each problem to its tab
            var errors = AgreementValidator.Validate(vals, Current.Entity.Code)
                .Select(x => new KeyValuePair<int, string>(tabOf[x.Prop], x.Message)).ToList();

            ShowErrors(errors);
            for (int i = 0; i < tabButtons.Count; i++) { tabButtons[i].HasError = errors.Any(x => x.Key == i); tabButtons[i].Invalidate(); }
            if (errors.Count > 0)
            {
                ShowTab(errors.Min(x => x.Key));   // jump to the first tab that needs attention
                return;
            }

            // ---- apply
            decimal value;
            AgreementValidator.ParseNumber(vals["Value"], out value);   // already checked above
            bool wasRejected = isEdit && existing.Status == "Rejected";
            var a = existing ?? new Agreement { EntityCode = Current.Entity.Code, CreatedBy = AppSession.UserName, Status = "Draft" };

            a.Type = vals["Type"];
            a.Title = vals["Title"];
            if (vals["VendorId"] != "L")   // "L" = old text-only supplier, left as it was
            {
                int vendorId = int.Parse(vals["VendorId"]);
                a.VendorId = vendorId;
                a.Supplier = Db.VendorName(vendorId) ?? a.Supplier;
            }
            a.Value = value;
            a.Currency = vals["Currency"];
            foreach (var kv in vals)
            {
                if (CoreProps.Contains(kv.Key)) continue;
                if (kv.Value.Length == 0) a.Extra.Remove(kv.Key); else a.Extra[kv.Key] = kv.Value;
            }

            string msg;
            if (isEdit)
            {
                if (wasRejected) AgreementWorkflow.Reopen(a);   // editing a rejected agreement sends it back to draft
                AgreementStore.Save(a);
                msg = wasRejected ? "Changes saved. The agreement is back in draft - submit it again when ready." : "Changes saved.";
            }
            else
            {
                AgreementStore.Add(a);
                msg = "Agreement " + a.No + " saved as draft.";
            }

            BeginInvoke(new Action(() => { var h = Saved; if (h != null) h(a, msg); }));   // leave the click first, then switch screens
        }
    }
}
