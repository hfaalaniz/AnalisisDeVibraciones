using System;
using System.Threading;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
            Application.Run(new MainForm());
        }

        private static void Report(Exception ex)
        {
            if (ex == null) return;
            LogManager.Instance.LogError("Excepción no controlada: " + ex);
            MessageBox.Show("Ocurrió un error inesperado. El detalle quedó en el registro:\n" + LogManager.Instance.LogFilePath + "\n\n" + ex.Message,
                "Análisis de Vibraciones", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
