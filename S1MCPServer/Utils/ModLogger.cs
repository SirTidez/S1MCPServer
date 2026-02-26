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
                () => MelonLogger.Msg($"[ERROR] {message}"),
                () => Console.Error.WriteLine($"[ERROR] {message}")
            );
        }

        public static void Error(string message, Exception exception)
        {
            SafeLog(
                () =>
                {
                    MelonLogger.Error($"{message}: {exception.Message}");
                    MelonLogger.Error($"Stack trace: {exception.StackTrace}");
                },
                () =>
                {
                    Console.Error.WriteLine($"[ERROR] {message}: {exception.Message}");
                    Console.Error.WriteLine($"[ERROR] Stack trace: {exception.StackTrace}");
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
