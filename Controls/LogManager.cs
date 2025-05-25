
using System;
using System.IO;

namespace VibrationAnalysis.UI
{   /// <summary>
    /// Clase singleton para manejar el registro de logs en la aplicación.
    /// </summary>
    /// <remarks>
    /// Esta clase permite registrar mensajes de información, error, sistema, advertencia y depuración en un archivo de log.
    /// El archivo se encuentra en la ruta "Data/Logs/app.log".
    /// </remarks>
    public class LogManager
    {
        private static readonly Lazy<LogManager> instance = new Lazy<LogManager>(() => new LogManager());
        private readonly string logPath = Path.Combine("Data", "Logs", "app.log");

        public static LogManager Instance => instance.Value;

        private LogManager() { }

        public void LogInfo(string message)
        {
            Log($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }

        public void LogError(string message)
        {
            Log($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }

        public void LogSystem(string message)
        {
            Log($"[SYSTEM] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }

        public void LogWarning(string message)
        {
            Log($"[WARNING] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }
        public void LogDebug(string message)
        {
            Log($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }


        private void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"{message}{Environment.NewLine}");
            }
            catch
            {
                // Ignorar errores de escritura en el log
            }
        }
    }
}
