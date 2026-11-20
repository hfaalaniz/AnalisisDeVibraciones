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
        private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "Logs");
        private readonly string logPath = Path.Combine(LogDir, "app.log");
        private readonly object sync = new object();
        /// <summary>Si es false (por defecto) los mensajes de depuración no se escriben: evita escribir en disco en cada repintado.</summary>
        public bool DebugEnabled { get; set; } = Environment.GetEnvironmentVariable("VA_DEBUG") == "1";
        public string LogFilePath => logPath;
        private readonly string backupLogPath = Path.Combine(Path.GetTempPath(), "AnalisisVibraciones_backup.log"); // Ruta para log de respaldo

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
            if (!DebugEnabled) return;
            Log($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }

        private void Log(string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(LogDir);
                    // Rotación simple: al superar 5 MB se renombra a app.1.log
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                    {
                        string old = Path.Combine(LogDir, "app.1.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(logPath, old);
                    }
                    File.AppendAllText(logPath, $"{message}{Environment.NewLine}");
                }
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
