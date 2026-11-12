using System;
using System.IO;

namespace PrintSave.Core
{
    /// <summary>Free-space lookup for the drive that holds the archive folder.</summary>
    public static class DiskSpace
    {
        private const double BytesPerGB = 1024.0 * 1024.0 * 1024.0;

        /// <summary>
        /// Gets free space (in GB) on the volume containing <paramref name="path"/>.
        /// Returns false for paths we can't measure (e.g. a UNC share) so callers skip the check.
        /// </summary>
        public static bool TryGetFreeGB(string path, out double freeGB)
        {
            freeGB = 0;
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return false; // UNC: not a local drive
                var di = new DriveInfo(root);
                if (!di.IsReady) return false;
                freeGB = di.AvailableFreeSpace / BytesPerGB;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
