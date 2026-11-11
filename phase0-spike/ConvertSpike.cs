// PrintSave - Phase 0.5 conversion check
//
// Purpose: confirm the captured (interleaved) XPS spool file opens with Windows' own
// XPS stack, how many pages it has, and that the content renders correctly.
// It writes a PNG of each page so we can eyeball fidelity.
//
// This uses the in-box WPF XPS APIs. It is ONLY a verification tool: rendering this way
// needs a desktop/STA thread, which a background service does not have -- so the real
// product will use a non-interactive converter. This just proves the captured data is good.
//
// Compiled with the in-box .NET Framework compiler; older C# syntax only.

using System;
using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;

namespace PrintSave.Phase0
{
    internal static class ConvertSpike
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.WriteLine("PrintSave - Phase 0.5 XPS open + render check");
            Console.WriteLine("=============================================");
            Console.WriteLine();

            string splPath = args.Length > 0 ? args[0] : FindOnlyCapturedSpl();
            if (string.IsNullOrEmpty(splPath) || !File.Exists(splPath))
            {
                Console.WriteLine("ERROR: no .SPL file given and could not find one in captured\\.");
                Console.WriteLine("Usage: PrintSaveConvert.exe <path-to-.SPL>");
                return 2;
            }

            Console.WriteLine("Input spool file: " + splPath);
            long inLen = new FileInfo(splPath).Length;
            Console.WriteLine("Size            : " + inLen + " bytes");
            Console.WriteLine();

            // Work on a .xps copy in temp (keeps interleaving; avoids touching the capture).
            string xpsCopy = Path.Combine(Path.GetTempPath(),
                "printsave_" + Guid.NewGuid().ToString("N") + ".xps");
            File.Copy(splPath, xpsCopy, true);

            string outDir = Path.Combine(
                Path.GetDirectoryName(splPath), "render");
            Directory.CreateDirectory(outDir);

            try
            {
                if (TryRenderWithXpsDocument(xpsCopy, outDir))
                    return 0;

                Console.WriteLine();
                Console.WriteLine("XpsDocument could not read it directly. Inspecting as an OPC package...");
                InspectPackage(xpsCopy);
                return 1;
            }
            finally
            {
                try { File.Delete(xpsCopy); } catch { }
            }
        }

        private static bool TryRenderWithXpsDocument(string xpsPath, string outDir)
        {
            XpsDocument doc = null;
            try
            {
                doc = new XpsDocument(xpsPath, FileAccess.Read);
                FixedDocumentSequence seq = doc.GetFixedDocumentSequence();
                if (seq == null)
                {
                    Console.WriteLine("Opened the package but found no fixed-document sequence.");
                    return false;
                }

                DocumentPaginator paginator = seq.DocumentPaginator;
                int pageCount = paginator.PageCount;
                Console.WriteLine("SUCCESS: opened as XPS.");
                Console.WriteLine("Pages: " + pageCount);
                Console.WriteLine();

                for (int i = 0; i < pageCount; i++)
                {
                    DocumentPage page = paginator.GetPage(i);
                    Size size = page.Size;
                    double wIn = size.Width / 96.0;
                    double hIn = size.Height / 96.0;
                    string pngPath = Path.Combine(outDir,
                        "page_" + (i + 1).ToString("D3") + ".png");
                    RenderPageToPng(page, pngPath, 150.0);
                    Console.WriteLine(string.Format(
                        "  page {0}: {1} x {2} DIP  (~{3:0.00}\" x {4:0.00}\")  -> {5}",
                        i + 1, (int)size.Width, (int)size.Height, wIn, hIn,
                        Path.GetFileName(pngPath)));
                    page.Dispose();
                }

                Console.WriteLine();
                Console.WriteLine("Rendered PNGs written to:");
                Console.WriteLine("  " + outDir);
                Console.WriteLine();
                Console.WriteLine("Open page_001.png and check it matches what you printed.");
                Console.WriteLine("If it looks right, the captured XPS is intact and XPS->PDF is viable.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("XpsDocument render failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                if (doc != null) doc.Close();
            }
        }

        private static void RenderPageToPng(DocumentPage page, string pngPath, double dpi)
        {
            double scale = dpi / 96.0;
            int pw = Math.Max(1, (int)Math.Ceiling(page.Size.Width * scale));
            int ph = Math.Max(1, (int)Math.Ceiling(page.Size.Height * scale));

            RenderTargetBitmap rtb = new RenderTargetBitmap(pw, ph, dpi, dpi, PixelFormats.Pbgra32);

            // White background first (XPS pages can be transparent), then the page on top.
            DrawingVisual bg = new DrawingVisual();
            using (DrawingContext dc = bg.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null,
                    new Rect(0, 0, page.Size.Width, page.Size.Height));
            }
            rtb.Render(bg);
            rtb.Render(page.Visual);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = new FileStream(pngPath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }

        private static void InspectPackage(string xpsPath)
        {
            try
            {
                using (Package pkg = Package.Open(xpsPath, FileMode.Open, FileAccess.Read))
                {
                    Console.WriteLine("It IS a valid OPC package. Parts:");
                    int n = 0;
                    foreach (PackagePart part in pkg.GetParts())
                    {
                        Console.WriteLine("  " + part.Uri + "   [" + part.ContentType + "]");
                        if (++n >= 40) { Console.WriteLine("  ..."); break; }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Not a readable OPC package either: "
                    + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine("This would mean the spool uses a format needing custom de-interleaving.");
            }
        }

        private static string FindOnlyCapturedSpl()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captured");
                if (!Directory.Exists(dir)) return null;
                string[] spls = Directory.GetFiles(dir, "*.SPL");
                if (spls.Length == 1) return spls[0];
                if (spls.Length > 1)
                {
                    // newest
                    Array.Sort(spls);
                    return spls[spls.Length - 1];
                }
                return null;
            }
            catch { return null; }
        }
    }
}
