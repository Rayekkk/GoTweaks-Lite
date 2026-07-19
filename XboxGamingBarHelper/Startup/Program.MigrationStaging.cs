using Shared.Constants;
using System;
using System.IO;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Seeds a background migration snapshot at a fixed, package-identity-independent path,
    /// in preparation for a future MSIX package rebrand (Name/Publisher change → new Package
    /// Family Name → a fresh, empty %LocalAppData%\Packages\&lt;PFN&gt;\... on the next
    /// install). Windows has no in-place identity-rename, so a rebranded release needs a way
    /// to recover the user's profiles/settings without depending on the OLD package still
    /// being installed at that exact moment - this snapshot is that fallback.
    ///
    /// This release does NOT change the package identity - it only stages data. Nothing here
    /// is visible to the user; it's a no-op-if-it-fails background copy.
    /// </summary>
    internal partial class Program
    {
        // %LocalAppData%\GoTweaks\ (no Packages\<PFN>\ segment) is the same PFN-independent
        // root Settings\LocalSettingsHelper.cs already falls back to - reusing it keeps this
        // repo's "genuinely fixed location" convention to one place instead of two.
        private static string MigrationRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GoTweaks", "Migration");

        private static string PendingFolder => Path.Combine(MigrationRoot, "Pending");

        /// <summary>
        /// Copies the current profiles/settings into the fixed staging folder, overwriting
        /// whatever was there before. Safe to call on every helper startup - cheap, and keeps
        /// the snapshot from ever going stale across releases between now and the eventual
        /// rebrand. Never throws; any failure is logged at Debug and simply retried next start.
        /// </summary>
        internal static void StageMigrationSnapshot()
        {
            try
            {
                // Nothing to stage yet if there are no profiles at all (fresh install) -
                // skip creating an empty folder tree for no reason.
                var profilesFolder = Profile.ProfileManager.GetGameProfilesFolder();
                var globalProfilePath = Profile.ProfileManager.GetGlobalProfilePath();
                bool hasAnyProfiles = Directory.Exists(profilesFolder) && Directory.GetFiles(profilesFolder, "*.xml").Length > 0;
                bool hasGlobalProfile = File.Exists(globalProfilePath);
                if (!hasAnyProfiles && !hasGlobalProfile)
                {
                    Logger.Debug("StageMigrationSnapshot: nothing to stage yet (no profiles found), skipping.");
                    return;
                }

                if (Directory.Exists(PendingFolder))
                {
                    Directory.Delete(PendingFolder, recursive: true);
                }

                // Functional state is helper-owned in 2.0. Widget-only presentation choices
                // are intentionally outside the migration snapshot.
                ExportDataToFolder(PendingFolder);

                Logger.Debug($"StageMigrationSnapshot: staged to {PendingFolder}");
            }
            catch (Exception ex)
            {
                Logger.Debug($"StageMigrationSnapshot failed (will retry next startup): {ex.Message}");
            }
        }
    }
}
