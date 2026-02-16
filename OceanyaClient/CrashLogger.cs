using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace OceanyaClient.Utilities
{
    public static class CrashLogger
    {
        private static readonly object LogLock = new object();

        public static string LogUnhandledException(
            Exception exception,
            string source,
            bool isTerminating,
            Dictionary<string, string>? additionalContext = null)
        {
            lock (LogLock)
            {
                try
                {
                    string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrashLogs");
                    Directory.CreateDirectory(logDirectory);

                    DateTime now = DateTime.Now;
                    string fileName = $"crash_{now:yyyyMMdd_HHmmss_fff}.log";
                    string filePath = Path.Combine(logDirectory, fileName);

                    string report = BuildCrashReport(exception, source, isTerminating, additionalContext);
                    File.WriteAllText(filePath, report, Encoding.UTF8);
                    return filePath;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private static string BuildCrashReport(
            Exception exception,
            string source,
            bool isTerminating,
            Dictionary<string, string>? additionalContext)
        {
            StringBuilder sb = new StringBuilder(16_384);
            DateTime now = DateTime.Now;
            Process currentProcess = Process.GetCurrentProcess();

            sb.AppendLine("=== OCEANYA CLIENT CRASH REPORT ===");
            sb.AppendLine();
            sb.AppendLine("[Time]");
            sb.AppendLine($"Local: {now:O}");
            sb.AppendLine($"UTC: {DateTime.UtcNow:O}");
            sb.AppendLine();

            sb.AppendLine("[Crash Context]");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"IsTerminating: {isTerminating}");
            sb.AppendLine($"UnhandledThreadId: {Environment.CurrentManagedThreadId}");
            sb.AppendLine();

            sb.AppendLine("[Application]");
            sb.AppendLine($"AppVersion: {GetAppVersion()}");
            sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"OSArchitecture: {RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"CommandLine: {Environment.CommandLine}");
            sb.AppendLine($"CurrentDirectory: {Environment.CurrentDirectory}");
            sb.AppendLine($"BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
            sb.AppendLine();

            sb.AppendLine("[Process]");
            sb.AppendLine($"ProcessName: {currentProcess.ProcessName}");
            sb.AppendLine($"ProcessId: {currentProcess.Id}");
            sb.AppendLine($"StartTime: {SafeDateTime(() => currentProcess.StartTime)}");
            sb.AppendLine($"MainModule: {SafeString(() => currentProcess.MainModule?.FileName ?? "N/A")}");
            sb.AppendLine($"WorkingSetBytes: {currentProcess.WorkingSet64}");
            sb.AppendLine($"PrivateMemoryBytes: {currentProcess.PrivateMemorySize64}");
            sb.AppendLine($"GCTotalMemoryBytes: {GC.GetTotalMemory(false)}");
            sb.AppendLine();

            sb.AppendLine("[Machine]");
            sb.AppendLine($"MachineName: {Environment.MachineName}");
            sb.AppendLine($"UserName: {Environment.UserName}");
            sb.AppendLine($"UserDomainName: {Environment.UserDomainName}");
            sb.AppendLine($"Is64BitOS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Is64BitProcess: {Environment.Is64BitProcess}");
            sb.AppendLine($"ProcessorCount: {Environment.ProcessorCount}");
            sb.AppendLine($"SystemUptimeMs: {Environment.TickCount64}");
            sb.AppendLine();

            AppendApplicationWindows(sb);
            AppendAdditionalContext(sb, additionalContext);
            AppendAssemblies(sb);
            AppendException(sb, exception, 0);

            return sb.ToString();
        }

        private static void AppendApplicationWindows(StringBuilder sb)
        {
            sb.AppendLine("[Windows]");
            try
            {
                if (Application.Current == null)
                {
                    sb.AppendLine("Application.Current is null");
                    sb.AppendLine();
                    return;
                }

                sb.AppendLine($"OpenWindowCount: {Application.Current.Windows.Count}");
                if (Application.Current.MainWindow != null)
                {
                    sb.AppendLine($"MainWindowType: {Application.Current.MainWindow.GetType().FullName}");
                    sb.AppendLine($"MainWindowTitle: {Application.Current.MainWindow.Title}");
                }

                foreach (Window window in Application.Current.Windows)
                {
                    sb.AppendLine(
                        $"Window: Type={window.GetType().FullName}, Title={window.Title}, IsVisible={window.IsVisible}, IsActive={window.IsActive}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Could not enumerate windows: {ex.Message}");
            }

            sb.AppendLine();
        }

        private static void AppendAdditionalContext(StringBuilder sb, Dictionary<string, string>? additionalContext)
        {
            sb.AppendLine("[Additional Context]");
            if (additionalContext == null || additionalContext.Count == 0)
            {
                sb.AppendLine("None");
                sb.AppendLine();
                return;
            }

            foreach (KeyValuePair<string, string> kvp in additionalContext)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }

            sb.AppendLine();
        }

        private static void AppendAssemblies(StringBuilder sb)
        {
            sb.AppendLine("[Loaded Assemblies]");
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (Assembly assembly in assemblies)
                {
                    AssemblyName name = assembly.GetName();
                    sb.AppendLine($"{name.Name} | Version={name.Version} | Location={SafeAssemblyLocation(assembly)}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Could not enumerate assemblies: {ex.Message}");
            }

            sb.AppendLine();
        }

        private static void AppendException(StringBuilder sb, Exception ex, int depth)
        {
            string indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}[Exception {depth}]");
            sb.AppendLine($"{indent}Type: {ex.GetType().FullName}");
            sb.AppendLine($"{indent}Message: {ex.Message}");
            sb.AppendLine($"{indent}HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"{indent}Source: {ex.Source ?? "N/A"}");
            sb.AppendLine($"{indent}TargetSite: {ex.TargetSite?.ToString() ?? "N/A"}");

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.AppendLine($"{indent}StackTrace:");
                foreach (string line in ex.StackTrace.Split(Environment.NewLine))
                {
                    sb.AppendLine($"{indent}  {line}");
                }
            }
            else
            {
                sb.AppendLine($"{indent}StackTrace: N/A");
            }

            if (ex.Data.Count > 0)
            {
                sb.AppendLine($"{indent}Data:");
                foreach (DictionaryEntry entry in ex.Data)
                {
                    sb.AppendLine($"{indent}  {entry.Key} = {entry.Value}");
                }
            }

            sb.AppendLine();

            if (ex.InnerException != null)
            {
                AppendException(sb, ex.InnerException, depth + 1);
            }
        }

        private static string GetAppVersion()
        {
            Assembly? entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                return "Unknown";
            }

            Version? version = entryAssembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }

        private static string SafeAssemblyLocation(Assembly assembly)
        {
            try
            {
                return string.IsNullOrWhiteSpace(assembly.Location) ? "dynamic/in-memory" : assembly.Location;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                return $"Unavailable ({ex.Message})";
            }
        }

        private static string SafeDateTime(Func<DateTime> getter)
        {
            try
            {
                return getter().ToString("O");
            }
            catch (Exception ex)
            {
                return $"Unavailable ({ex.Message})";
            }
        }
    }
}
