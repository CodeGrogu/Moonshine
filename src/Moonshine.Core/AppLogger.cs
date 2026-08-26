using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Moonshine.Core
{
    /// <summary>
    /// Thread-safe central application logger providing bounded in-memory log streaming,
    /// file persistence in %LOCALAPPDATA%\Moonshine\Logs, and UI event notification.
    /// </summary>
    public static class AppLogger
    {
        private const int MaxInMemoryLogEntries = 1000;

        private static readonly string LogDirectory;
        private static readonly string LogFilePath;
        private static readonly object _lock = new object();
        private static readonly Queue<string> _recentLogs = new Queue<string>(MaxInMemoryLogEntries);

        /// <summary>
        /// Event triggered whenever a new formatted log entry is committed.
        /// </summary>
        public static event Action<string>? OnLogMessage;

        static AppLogger()
        {
            try
            {
                LogDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Moonshine",
                    "Logs"
                );
                Directory.CreateDirectory(LogDirectory);
                LogFilePath = Path.Combine(LogDirectory, $"Moonshine_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }
            // ALLOWED_EXCEPTION: Fallback to local path if %LOCALAPPDATA% directory creation fails.
            catch (Exception)
            {
                LogDirectory = AppDomain.CurrentDomain.BaseDirectory;
                LogFilePath = Path.Combine(LogDirectory, "Moonshine_Fallback.log");
            }
        }

        /// <summary>
        /// Appends a message to the structured log file and memory stream.
        /// </summary>
        public static void Log(string message)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            lock (_lock)
            {
                if (_recentLogs.Count >= MaxInMemoryLogEntries)
                {
                    _recentLogs.Dequeue();
                }
                _recentLogs.Enqueue(formatted);

                try
                {
                    File.AppendAllText(LogFilePath, formatted + Environment.NewLine);
                }
                // ALLOWED_EXCEPTION: Ignore transient file append failures to prevent stream interruption.
                catch (Exception)
                {
                }
            }

            try
            {
                OnLogMessage?.Invoke(formatted);
            }
            // ALLOWED_EXCEPTION: Prevent external subscriber exceptions from disrupting log execution.
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Retrieves a snapshot of the most recent log entries.
        /// </summary>
        public static IReadOnlyList<string> GetRecentLogs()
        {
            lock (_lock)
            {
                return _recentLogs.ToArray();
            }
        }

        /// <summary>
        /// Clears the in-memory log buffer.
        /// </summary>
        public static void ClearRecentLogs()
        {
            lock (_lock)
            {
                _recentLogs.Clear();
            }
        }

        /// <summary>
        /// Gets the absolute path of the current session log file.
        /// </summary>
        public static string CurrentLogFilePath => LogFilePath;

        /// <summary>
        /// Gets the absolute path of the logs directory.
        /// </summary>
        public static string LogsDirectoryPath => LogDirectory;

        /// <summary>
        /// Opens the logs folder in Windows Explorer.
        /// </summary>
        public static void OpenLogDirectory()
        {
            try
            {
                if (Directory.Exists(LogDirectory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = LogDirectory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            // ALLOWED_EXCEPTION: Process launch error handled safely.
            catch (Exception)
            {
            }
        }
    }
}
