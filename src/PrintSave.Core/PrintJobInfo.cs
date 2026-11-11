using System;

namespace PrintSave.Core
{
    /// <summary>Metadata about a captured print job.</summary>
    public sealed class PrintJobInfo
    {
        public uint JobId { get; set; }
        public string Printer { get; set; }
        public string Machine { get; set; }
        public string User { get; set; }
        public string Document { get; set; }
        public string Datatype { get; set; }
        public uint Status { get; set; }
        public uint TotalPages { get; set; }
        public DateTime? SubmittedUtc { get; set; }
        public DateTime CapturedUtc { get; set; }

        /// <summary>Source spool file in the spooler directory.</summary>
        public string SpoolFilePath { get; set; }

        /// <summary>Detected spool format name (XPS, EMF, PDF, ...).</summary>
        public string Format { get; set; }

        /// <summary>Final saved path in the PrintSave archive.</summary>
        public string SavedPath { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] \"{1}\" user={2} printer={3} pages={4} fmt={5}",
                JobId, Document, User, Printer, TotalPages, Format);
        }
    }
}
