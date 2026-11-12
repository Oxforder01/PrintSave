using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PrintSave.Core
{
    /// <summary>Applies or removes a restrictive ACL on the archive folder.</summary>
    public static class FolderSecurity
    {
        /// <summary>
        /// Locks the folder (and its contents, by inheritance) to SYSTEM, Administrators and the given
        /// owner SID, each Full Control, with inheritance from the parent disabled. Requires the caller
        /// to own the folder or hold WRITE_DAC (SYSTEM always does; an elevated admin does).
        /// </summary>
        public static void Apply(string path, string ownerSid)
        {
            Directory.CreateDirectory(path);
            var di = new DirectoryInfo(path);
            var sec = new DirectorySecurity();

            // Protect from inheritance and drop any inherited rules (do not copy them in).
            sec.SetAccessRuleProtection(true, false);

            AddFull(sec, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            AddFull(sec, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            if (!string.IsNullOrEmpty(ownerSid))
            {
                try { AddFull(sec, new SecurityIdentifier(ownerSid)); } catch { }
            }

            di.SetAccessControl(sec);
        }

        /// <summary>
        /// Restores "easiest access": re-enables inheritance from the parent and removes the explicit
        /// rules we added, so normal inherited permissions govern again.
        /// </summary>
        public static void Relax(string path)
        {
            if (!Directory.Exists(path)) return;
            var di = new DirectoryInfo(path);
            DirectorySecurity sec = di.GetAccessControl();

            // Re-enable inheritance (and keep current explicit rules for now so we can remove them cleanly).
            sec.SetAccessRuleProtection(false, true);
            foreach (FileSystemAccessRule rule in
                     sec.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                sec.RemoveAccessRule(rule);
            }

            di.SetAccessControl(sec);
        }

        private static void AddFull(DirectorySecurity sec, SecurityIdentifier sid)
        {
            sec.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
    }
}
