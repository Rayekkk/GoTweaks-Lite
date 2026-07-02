namespace Shared.Constants
{
    /// <summary>
    /// Self-update configuration.
    ///
    /// <see cref="Repo"/> is the GitHub "owner/repo" the app checks for new
    /// releases. It is intentionally EMPTY in this fork so the app NEVER
    /// auto-updates to the upstream (corando98/GoTweaks) build: every update
    /// check short-circuits to "up to date" and every self-install is refused
    /// while it is empty.
    ///
    /// Once this project lives in your own repository, set <see cref="Repo"/>
    /// to your own "owner/name" (e.g. "yourname/GoTweaks") to re-enable the
    /// "Check for Update" button, the check-on-start probe, and self-install.
    /// </summary>
    public static class UpdateConstants
    {
        /// <summary>GitHub "owner/repo" to query for releases. Empty = updates disabled.</summary>
        public const string Repo = "";

        /// <summary>True when a release repo is configured (update checks/installs allowed).</summary>
        public static bool UpdatesEnabled => !string.IsNullOrWhiteSpace(Repo);

        /// <summary>The GitHub "latest release" API endpoint, or empty when disabled.</summary>
        public static string LatestReleaseApiUrl =>
            UpdatesEnabled ? $"https://api.github.com/repos/{Repo}/releases/latest" : "";
    }
}
