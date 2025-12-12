using System;
using System.Windows.Forms;

namespace TransparencyMode.App
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Global exception handling
            Application.ThreadException += new System.Threading.ThreadExceptionEventHandler(Application_ThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            try 
            {
                // Run as system tray application
                Application.Run(new TrayApplication());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical Error: {ex.Message}\n\n{ex.StackTrace}", "Transparency Mode Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            MessageBox.Show($"Application Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Transparency Mode Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            MessageBox.Show($"Fatal Error: {ex.Message}\n\n{ex.StackTrace}", "Transparency Mode Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
