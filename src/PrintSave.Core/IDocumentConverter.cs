namespace PrintSave.Core
{
    /// <summary>
    /// Converts a captured spool file to PDF. Implementations live outside Core so the
    /// capture engine stays free of heavy dependencies (and so conversion can later run in a
    /// different process/session than the SYSTEM capture service).
    /// </summary>
    public interface IDocumentConverter
    {
        /// <summary>True if this converter can turn the given spool format (e.g. "XPS") into PDF.</summary>
        bool CanConvert(string format);

        /// <summary>
        /// Converts <paramref name="inputSpoolPath"/> to a PDF at <paramref name="outputPdfPath"/>.
        /// Returns true on success; on failure returns false and sets <paramref name="error"/>.
        /// </summary>
        bool TryConvertToPdf(string inputSpoolPath, string outputPdfPath, string format, out string error);
    }
}
