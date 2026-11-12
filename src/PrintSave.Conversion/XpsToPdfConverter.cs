using System;
using System.IO;
using System.Threading;
using PrintSave.Core;

namespace PrintSave.Conversion
{
    /// <summary>
    /// Converts captured XPS spool data to a vector PDF using the MIT-licensed PdfSharp.Xps lineage.
    /// Output preserves embedded fonts and selectable/searchable text (Type0 + ToUnicode).
    /// </summary>
    public sealed class XpsToPdfConverter : IDocumentConverter
    {
        public bool CanConvert(string format)
        {
            return string.Equals(format, "XPS", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryConvertToPdf(string inputSpoolPath, string outputPdfPath, string format, out string error)
        {
            error = null;
            string xpsTemp = null;
            try
            {
                // PdfSharp.Xps keys off the .xps extension, so give the spool data one.
                xpsTemp = Path.Combine(Path.GetTempPath(), "ps_" + Guid.NewGuid().ToString("N") + ".xps");
                File.Copy(inputSpoolPath, xpsTemp, true);

                // The underlying library uses WPF/ReachFramework, which wants an STA thread.
                RunOnStaThread(() => PdfSharp.Xps.XpsConverter.Convert(xpsTemp, outputPdfPath, 0));

                if (File.Exists(outputPdfPath) && new FileInfo(outputPdfPath).Length > 0)
                    return true;

                error = "converter produced no output";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message
                        + (ex.InnerException != null ? " / " + ex.InnerException.Message : "");
                TryDelete(outputPdfPath);
                return false;
            }
            finally
            {
                TryDelete(xpsTemp);
            }
        }

        private static void RunOnStaThread(Action action)
        {
            Exception captured = null;
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { captured = ex; }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (captured != null) throw captured;
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
