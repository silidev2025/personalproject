using System;
using System.Threading;
using System.Windows.Forms;

namespace RoseBuild
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig cfg;
            try
            {
                cfg = AppConfig.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load settings: " + ex.Message, "Rose",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception error = e.ExceptionObject as Exception;
                try
                {
                    MessageBox.Show(error == null ? "unknown error" : error.ToString(), "Rose",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                try
                {
                    MessageBox.Show(e.Exception.ToString(), "Rose",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };

            Application.Run(new BuilderForm(cfg));
        }
    }

    internal static class BuildStamp
    {
        public const string Name = "Rose";
        public const string Version = "1.0.0";
    }
}
