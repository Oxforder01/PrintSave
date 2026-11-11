namespace PrintSave.Core
{
    public sealed class CaptureOptions
    {
        /// <summary>Root archive folder. Default C:\PrintSave.</summary>
        public string RootDir { get; set; } = @"C:\PrintSave";

        /// <summary>How often to poll the spooler for completed jobs (ms).</summary>
        public int PollIntervalMs { get; set; } = 1000;

        /// <summary>How often to re-enumerate printers and re-apply the keep-jobs flag (seconds).</summary>
        public int ReapplyFlagsEverySec { get; set; } = 60;

        /// <summary>Delete the retained job from the queue after we capture it.</summary>
        public bool DeleteAfterCapture { get; set; } = true;

        /// <summary>
        /// Restore each printer's keep-jobs flag to its original value when the engine stops.
        /// True for interactive/testing; a real service leaves the flag set so nothing is missed.
        /// </summary>
        public bool RestoreFlagsOnStop { get; set; } = true;
    }
}
