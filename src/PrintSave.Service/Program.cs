using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using PrintSave.Conversion;
using PrintSave.Core;
using PrintSave.Core.Logging;

namespace PrintSave.Service
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (HasArg(args, "--restore-flags")) return RestoreFlags();
            if (HasArg(args, "--console")) return RunConsole();

            // Started by the Service Control Manager.
            ServiceBase.Run(new PrintSaveService());
            return 0;
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
                if (a.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Run the engine in the foreground for debugging (no service install needed).</summary>
        private static int RunConsole()
        {
            var opt = new CaptureOptions { WriteJsonSidecar = false };
            var log = new Log(Path.Combine(opt.RootDir, "logs"));
            log.OnMessage += (level, line) => Console.WriteLine(line);

            var engine = new CaptureEngine(opt, log, new XpsToPdfConverter());
            engine.Start();
            Console.WriteLine("PrintSave.Service running in console mode. Press Enter to stop.");
            Console.ReadLine();
            engine.Stop();
            return 0;
        }

        /// <summary>Clear the keep-printed-jobs flag on all printers (used by the uninstaller).</summary>
        private static int RestoreFlags()
        {
            var pm = new PrinterManager();
            foreach (string printer in pm.EnumeratePrinters())
            {
                try
                {
                    pm.SetKeepPrintedJobs(printer, false);
                    Console.WriteLine("Cleared keep-printed-jobs: " + printer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed on '" + printer + "': " + ex.Message);
                }
            }
            return 0;
        }
    }
}
