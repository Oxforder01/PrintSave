using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using PrintSave.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PrintSave.Conversion
{
    /// <summary>
    /// Converts EMF spool data (the "NT EMF" format most GDI printer drivers produce) to PDF.
    /// Each page is an Enhanced Metafile; we rasterize it at 300 DPI and place it on a PDF page
    /// sized to the page's physical dimensions. Raster (not vector/selectable like the XPS path),
    /// but pure .NET + the MIT PDFsharp lineage — no external tools, clean for the Store.
    /// </summary>
    public sealed class EmfToPdfConverter : IDocumentConverter
    {
        private const uint EmfSignature = 0x464D4520; // " EMF" (ENHMETA_SIGNATURE), at EMR_HEADER offset 40
        private const int RenderDpi = 300;

        public bool CanConvert(string format)
        {
            return string.Equals(format, "EMF", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryConvertToPdf(string inputSpoolPath, string outputPdfPath, string format, out string error)
        {
            error = null;
            var temps = new List<string>();
            try
            {
                byte[] data = File.ReadAllBytes(inputSpoolPath);
                List<byte[]> pages = ExtractEmfPages(data);
                if (pages.Count == 0) { error = "no EMF pages found in spool data"; return false; }

                Exception captured = null;
                // PDFsharp (WPF lineage) and GDI+ imaging are happiest on an STA thread.
                RunOnStaThread(() =>
                {
                    try { RenderToPdf(pages, outputPdfPath, temps); }
                    catch (Exception ex) { captured = ex; }
                });
                if (captured != null) throw captured;

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
                foreach (string t in temps) TryDelete(t);
            }
        }

        /// <summary>
        /// Pulls each page's Enhanced Metafile out of the spool by locating EMR_HEADER records
        /// (iType==1 and the " EMF" signature at offset 40) and reading nBytes (offset 48). This
        /// finds the real EMF blobs regardless of how the EMFSPOOL layer framed them (inline
        /// EMRI_METAFILE_DATA vs. offset-based EMRI_METAFILE_EXT).
        /// </summary>
        public static List<byte[]> ExtractEmfPages(byte[] data)
        {
            var pages = new List<byte[]>();
            if (data == null) return pages;

            int limit = data.Length - 52; // need to read nBytes at i+48..i+51
            int i = 0;
            while (i <= limit)
            {
                bool isHeader = data[i] == 0x01 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 0
                                && unchecked((uint)BitConverter.ToInt32(data, i + 40)) == EmfSignature;
                if (isHeader)
                {
                    long nBytes = unchecked((uint)BitConverter.ToInt32(data, i + 48));
                    if (nBytes >= 88 && i + nBytes <= data.Length)
                    {
                        var emf = new byte[nBytes];
                        Array.Copy(data, i, emf, 0, (int)nBytes);
                        pages.Add(emf);
                        i += (int)nBytes;
                        continue;
                    }
                }
                i++;
            }
            return pages;
        }

        private static void RenderToPdf(List<byte[]> pages, string outputPdfPath, List<string> temps)
        {
            using (var doc = new PdfDocument())
            {
                foreach (byte[] emf in pages)
                {
                    using (var ms = new MemoryStream(emf, false))
                    using (var mf = new Metafile(ms))
                    {
                        double wPt, hPt;
                        PhysicalSizePoints(emf, mf, out wPt, out hPt);

                        int pxW = Math.Max(1, (int)Math.Round(wPt / 72.0 * RenderDpi));
                        int pxH = Math.Max(1, (int)Math.Round(hPt / 72.0 * RenderDpi));

                        string png = Path.Combine(Path.GetTempPath(), "emf_" + Guid.NewGuid().ToString("N") + ".png");
                        temps.Add(png);
                        using (var bmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb))
                        {
                            bmp.SetResolution(RenderDpi, RenderDpi);
                            using (var g = Graphics.FromImage(bmp))
                            {
                                g.Clear(Color.White);
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                g.DrawImage(mf, new Rectangle(0, 0, pxW, pxH));
                            }
                            bmp.Save(png, ImageFormat.Png);
                        }

                        PdfPage page = doc.AddPage();
                        page.Width = XUnit.FromPoint(wPt);
                        page.Height = XUnit.FromPoint(hPt);
                        using (var gfx = XGraphics.FromPdfPage(page))
                        using (var img = XImage.FromFile(png))
                            gfx.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
                    }
                }
                doc.Save(outputPdfPath);
            }
        }

        /// <summary>Physical page size in points, from the EMF rclFrame (0.01 mm), with fallbacks.</summary>
        private static void PhysicalSizePoints(byte[] emf, Metafile mf, out double wPt, out double hPt)
        {
            // EMR_HEADER.rclFrame at offset 24: left, top, right, bottom as Int32 in 0.01 mm.
            try
            {
                int left = BitConverter.ToInt32(emf, 24);
                int top = BitConverter.ToInt32(emf, 28);
                int right = BitConverter.ToInt32(emf, 32);
                int bottom = BitConverter.ToInt32(emf, 36);
                double wMm = (right - left) / 100.0;
                double hMm = (bottom - top) / 100.0;
                if (wMm > 1 && hMm > 1)
                {
                    wPt = wMm / 25.4 * 72.0;
                    hPt = hMm / 25.4 * 72.0;
                    return;
                }
            }
            catch { }

            try
            {
                MetafileHeader h = mf.GetMetafileHeader();
                double wIn = h.Bounds.Width / h.DpiX;
                double hIn = h.Bounds.Height / h.DpiY;
                if (wIn > 0.1 && hIn > 0.1) { wPt = wIn * 72.0; hPt = hIn * 72.0; return; }
            }
            catch { }

            wPt = 8.5 * 72.0; hPt = 11.0 * 72.0; // US Letter default
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
