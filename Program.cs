using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcurePay
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.ThreadException += (sender, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "ProcurePay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            while (true)
            {
                using (var login = new LoginForm())
                {
                    if (login.ShowDialog() != DialogResult.OK) break;   // closed the login window = exit
                }

                bool signedOut;
                using (var main = new MainForm())
                {
                    main.ShowDialog();
                    signedOut = main.SignedOut;
                }
                if (!signedOut) break;   // closed the main window = exit; Sign out = back to login
            }
        }
    }
}
