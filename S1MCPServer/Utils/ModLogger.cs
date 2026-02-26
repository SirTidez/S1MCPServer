using MelonLoader;
using System;

namespace S1MCPServer.Utils
{
    public static class ModLogger 
    {
        private static void SafeLog(Action melonLog, Action fallback)
        {
            try
            {
                melonLog();
            }
            catch
            {
                fallback();
            }
        }

        public static void Info(string message)
        {
            SafeLog(
                () => MelonLogger.Msg(message),
                () => Console.WriteLine(message)
            );
        }

        public static void Debug(string message)
        {
            // Debug logging is always enabled for development
            SafeLog(
                () => MelonLogger.Msg($"[DEBUG] {message}"),
                () => Console.WriteLine($"[DEBUG] {message}")
            );
        }

        public static void Error(string message)
        {
            SafeLog(
                () => MelonLogger.Error(message),
                () => Console.Error.WriteLine($"[ERROR] {message}")
            );
        }

        public static void Error(string message, Exception exception)
        {
            string exceptionMessage = exception != null ? exception.Message : "exception is null";
            string stackTrace = exception != null ? exception.StackTrace : "exception is null";

            SafeLog(
                () =>
                {
                    MelonLogger.Error($"{message}: {exceptionMessage}");
                    MelonLogger.Error($"Stack trace: {stackTrace}");
                },
                () =>
                {
                    Console.Error.WriteLine($"[ERROR] {message}: {exceptionMessage}");
                    Console.Error.WriteLine($"[ERROR] Stack trace: {stackTrace}");
                }
            );
        }
        
        public static void Warn(string message)
        {
            SafeLog(
                () => MelonLogger.Warning(message),
                () => Console.WriteLine($"[WARN] {message}")
            );
        }
    }
}
