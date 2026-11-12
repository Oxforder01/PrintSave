namespace PrintSave.Core
{
    /// <summary>
    /// User-facing settings, persisted as JSON (see <see cref="ConfigStore"/>).
    /// The future tray settings UI edits the same file.
    /// </summary>
    public sealed class Config
    {
        /// <summary>Archive root. Default C:\PrintSave.</summary>
        public string RootDir { get; set; } = @"C:\PrintSave";

        /// <summary>
        /// When true, the archive folder is locked to the owner, SYSTEM and Administrators.
        /// Default false: easiest access (normal inherited permissions).
        /// </summary>
        public bool SecureArchiveFolder { get; set; } = false;

        /// <summary>SID of the user granted access when the folder is secured (set when security is enabled).</summary>
        public string OwnerSid { get; set; } = null;
    }
}
