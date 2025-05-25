using System;
using System.IO;

namespace VibrationAnalysis.UI
{
    /// <summary>
    /// Clase singleton para manejar el registro de logs en la aplicación.
    /// </summary>
    /// <remarks>
    /// Esta clase permite registrar mensajes de información, error, sistema, advertencia y depuración en un archivo de log.
    /// El archivo se encuentra en la ruta "Data/Logs/app.log".
    /// Incluye manejo de errores para problemas de escritura en el log.
    /// </remarks>
    public class LogManager
    {
        private static readonly Lazy<LogManager> instance = new Lazy<LogManager>(() => new LogManager());
        private readonly string logPath = Path.Combine("Data", "Logs", "app.log");
        private readonly string backupLogPath = Path.Combine("Data", "Logs", "backup_app.log"); // Ruta para log de respaldo

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
                // Crear directorio si no existe
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                // Escribir en el archivo de log principal
                File.AppendAllText(logPath, $"{message}{Environment.NewLine}");
            }
            catch (IOException ex)
            {
                // Manejar errores específicos de E/S (como disco lleno o archivo bloqueado)
                WriteToBackupLog($"[ERROR] No se pudo escribir en el log principal ({logPath}): {ex.Message}");
                WriteToConsole($"[ERROR] No se pudo escribir en el log principal: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // Manejar errores de permisos
                WriteToBackupLog($"[ERROR] Permiso denegado al escribir en el log ({logPath}): {ex.Message}");
                WriteToConsole($"[ERROR] Permiso denegado al escribir en el log: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Manejar cualquier otro error inesperado
                WriteToBackupLog($"[ERROR] Error inesperado al escribir en el log ({logPath}): {ex.Message}");
                WriteToConsole($"[ERROR] Error inesperado al escribir en el log: {ex.Message}");
            }
        }

        private void WriteToBackupLog(string message)
        {
            try
            {
                // Intentar escribir en un archivo de respaldo
                Directory.CreateDirectory(Path.GetDirectoryName(backupLogPath));
                File.AppendAllText(backupLogPath, $"[BACKUP] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}{Environment.NewLine}");
            }
            catch
            {
                // Si falla el log de respaldo, escribir solo en consola para no entrar en un bucle
                WriteToConsole($"[ERROR] No se pudo escribir en el log de respaldo ({backupLogPath})");
            }
        }

        private void WriteToConsole(string message)
        {
            // Escribir en consola para depuración o notificación
            Console.WriteLine(message);
        }
    }
}
