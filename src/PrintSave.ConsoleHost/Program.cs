using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using PrintSave.Core;
using PrintSave.Core.Logging;

namespace PrintSave.ConsoleHost
{
    internal static class Program
    {
        private static readonly ManualResetEventSlim Exit = new ManualResetEventSlim(false);

        private static int Main(string[] args)
        {
            Console.WriteLine("PrintSave - Phase 1 capture (console host)");
            Console.WriteLine("==========================================");

            var opt = new CaptureOptions();
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--root" || args[i] == "-r") && i + 1 < args.Length)
                    opt.RootDir = args[++i];
            }

            if (!IsElevated())
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: PrintSave must run as Administrator.");
                Console.WriteLine("The spooler directory and printer admin access require it.");
                Console.WriteLine("Run from an elevated terminal, or use run-console.ps1.");
                return 2;
            }

            var log = new Log(Path.Combine(opt.RootDir, "logs"));
            log.OnMessage += (level, line) =>
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = level == LogLevel.Error ? ConsoleColor.Red
                    : level == LogLevel.Warn ? ConsoleColor.Yellow
                    : ConsoleColor.Gray;
                Console.WriteLine(line);
                Console.ForegroundColor = prev;
            };

            var engine = new CaptureEngine(opt, log);
            engine.JobCaptured += job =>
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">>> SAVED: " + job.SavedPath);
                Console.ForegroundColor = prev;
            };

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Exit.Set(); };

            try
            {
                engine.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start: " + ex.Message);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("Running. Print anything to any printer and watch it get saved under:");
            Console.WriteLine("  " + opt.RootDir);
            Console.WriteLine("Press Ctrl+C to stop (restores printer settings).");
            Console.WriteLine();

            Exit.Wait();

            Console.WriteLine();
            Console.WriteLine("Stopping...");
            engine.Stop();
            return 0;
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
