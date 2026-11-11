// PrintSave - vector XPS->PDF validation CLI
//
// Throwaway proof: uses the community PdfSharp.Xps DLLs ONLY to confirm that a vector,
// selectable-text PDF can be produced from the captured spool file. The real product will
// build this from authoritative MIT source (likely via the .NET SDK), not this binary.
//
// Usage: ConvertXps <input.xps|.spl> <output.pdf>

using System;
using System.IO;
using System.IO.Packaging;
using PdfSharp.Xps;

namespace PrintSave.ConvertSpike
{
    internal static class ConvertXps
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: ConvertXps <input.xps|.spl> <output.pdf>");
                return 2;
            }
            string inPath = args[0];
            string outPath = args[1];
            if (!File.Exists(inPath))
            {
                Console.WriteLine("ERROR: input not found: " + inPath);
                return 2;
            }

            Console.WriteLine("Input : " + inPath + " (" + new FileInfo(inPath).Length + " bytes)");
            Console.WriteLine("Output: " + outPath);
            Console.WriteLine();

            // PdfSharp.Xps keys off the .xps extension; give it one.
            string xps = inPath;
            bool tempXps = false;
            if (!inPath.EndsWith(".xps", StringComparison.OrdinalIgnoreCase))
            {
                xps = Path.Combine(Path.GetTempPath(), "cvt_" + Guid.NewGuid().ToString("N") + ".xps");
                File.Copy(inPath, xps, true);
                tempXps = true;
            }

            try
            {
                return TryConvert(xps, outPath);
            }
            finally
            {
                if (tempXps) { try { File.Delete(xps); } catch { } }
            }
        }

        private static int TryConvert(string xps, string outPath)
        {
            try
            {
                XpsConverter.Convert(xps, outPath, 0);
                Report(outPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Direct convert failed: " + ex.GetType().Name + ": " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("  inner: " + ex.InnerException.Message);
            }

            // Fallback: the spool package is interleaved; rebuild it as a flat package and retry.
            Console.WriteLine();
            Console.WriteLine("Retrying after normalizing the (interleaved) package to a flat .xps...");
            string flat = Path.Combine(Path.GetTempPath(), "flat_" + Guid.NewGuid().ToString("N") + ".xps");
            try
            {
                NormalizePackage(xps, flat);
                XpsConverter.Convert(flat, outPath, 0);
                Report(outPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Normalized convert failed: " + ex.GetType().Name + ": " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("  inner: " + ex.InnerException.Message);
                return 1;
            }
            finally
            {
                try { File.Delete(flat); } catch { }
            }
        }

        // Copy every part of an (interleaved) OPC package into a fresh flat package.
        private static void NormalizePackage(string src, string dst)
        {
            using (Package inPkg = Package.Open(src, FileMode.Open, FileAccess.Read))
            using (Package outPkg = Package.Open(dst, FileMode.Create, FileAccess.ReadWrite))
            {
                foreach (PackagePart part in inPkg.GetParts())
                {
                    PackagePart np = outPkg.CreatePart(part.Uri, part.ContentType, CompressionOption.Normal);
                    using (Stream si = part.GetStream(FileMode.Open, FileAccess.Read))
                    using (Stream so = np.GetStream(FileMode.Create, FileAccess.Write))
                    {
                        si.CopyTo(so);
                    }
                }
                // Recreate package-level relationships (e.g. the StartPart to the FixedDocumentSequence).
                foreach (PackageRelationship rel in inPkg.GetRelationships())
                {
                    outPkg.CreateRelationship(rel.TargetUri, rel.TargetMode, rel.RelationshipType, rel.Id);
                }
            }
        }

        private static void Report(string outPath)
        {
            Console.WriteLine();
            if (File.Exists(outPath))
            {
                Console.WriteLine("SUCCESS: wrote " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");
                Console.WriteLine("Open it and try selecting text -- if the text highlights, it is true vector output.");
            }
            else
            {
                Console.WriteLine("Convert returned but no output file was produced.");
            }
        }
    }
}
