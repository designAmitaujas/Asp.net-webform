using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ProcurePay
{
    // ProcurePay palette: petrol teal base, deeper teal action colour, marigold accent
    public static class Theme
    {
        public static readonly Color NavyDark = ColorTranslator.FromHtml("#08222B");
        public static readonly Color Brand = ColorTranslator.FromHtml("#0F3B47");
        public static readonly Color Brand2 = ColorTranslator.FromHtml("#0B7A85");
        public static readonly Color BrandSoft = ColorTranslator.FromHtml("#E1F1F2");
        public static readonly Color Gold = ColorTranslator.FromHtml("#F2A93B");
        public static readonly Color Bg = ColorTranslator.FromHtml("#F2F5F6");
        public static readonly Color Ink = ColorTranslator.FromHtml("#0E1D24");
        public static readonly Color Ink2 = ColorTranslator.FromHtml("#4E626C");
        public static readonly Color Ink3 = ColorTranslator.FromHtml("#6B7F89");
        public static readonly Color Line = ColorTranslator.FromHtml("#D9E3E6");
        public static readonly Color Good = ColorTranslator.FromHtml("#17855A");
        public static readonly Color GoodSoft = ColorTranslator.FromHtml("#DFF3EA");
        public static readonly Color Warn = ColorTranslator.FromHtml("#B86E00");
        public static readonly Color WarnSoft = ColorTranslator.FromHtml("#FFF0D6");
        public static readonly Color Bad = ColorTranslator.FromHtml("#C8423A");
        public static readonly Color BadSoft = ColorTranslator.FromHtml("#FCE8E6");
        public static readonly Color LightText = ColorTranslator.FromHtml("#D3E8EA");
        public static readonly Color SoftText = ColorTranslator.FromHtml("#A9C6CA");

        // extra tones used by the shared controls
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#085F68");
        public static readonly Color Line2 = ColorTranslator.FromHtml("#E8EEF0");
        public static readonly Color Hover = ColorTranslator.FromHtml("#F6FAFA");
        public static readonly Color GoodDark = ColorTranslator.FromHtml("#126B48");
        public static readonly Color BadDark = ColorTranslator.FromHtml("#A8332C");
        public static readonly Color GoodLine = ColorTranslator.FromHtml("#B5DEC9");
        public static readonly Color BadLine = ColorTranslator.FromHtml("#F1C2BE");
        public static readonly Color WarnLine = ColorTranslator.FromHtml("#EFD39A");
        public static readonly Color BrandLine = ColorTranslator.FromHtml("#C3DEE0");
        public static readonly Color NavText = ColorTranslator.FromHtml("#C9E0E3");
        public static readonly Color NavHover = ColorTranslator.FromHtml("#17505E");
        public static readonly Color NavActive = ColorTranslator.FromHtml("#0A2D37");
        public static readonly Color SideSub = ColorTranslator.FromHtml("#8FB3B8");
        public static readonly Color SoonText = ColorTranslator.FromHtml("#6C979D");
        public static readonly Color SideLine = ColorTranslator.FromHtml("#2C5A66");

        // Window / taskbar icon: taken from the exe, which carries ProcurePay.ico
        public static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return null; }
        }

        public static Font F(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style);
        }
    }

    // Who is signed in (filled by the login screen)
    public static class AppSession
    {
        public static string UserName;
        public static string FullName;
        public static string Role;
    }
}