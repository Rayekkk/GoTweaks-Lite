namespace Shared.Constants
{
    /// <summary>
    /// MSIX package identity for GoTweaks.
    ///
    /// <see cref="PackageFamilyName"/> is the single source of truth for the
    /// package family name. It is used to reconstruct the per-package
    /// <c>%LocalAppData%\Packages\&lt;family&gt;\LocalState|LocalCache</c> paths when
    /// the helper runs OUTSIDE package context (elevated / scheduled task), where
    /// <c>ApplicationData.Current</c> is unavailable.
    ///
    /// It MUST match the manifest <c>&lt;Identity Name&gt;</c> + publisher hash in
    /// <c>XboxGamingBarPackage\Package.appxmanifest</c>
    /// (Name "PlayandBuildCustom.10365195AA1EC", Publisher "CN=dg" → hash
    /// "8edemd50ez3gg"). If you change the manifest Identity/Publisher, update
    /// this constant too.
    /// </summary>
    public static class PackageConstants
    {
        /// <summary>The MSIX package family name (manifest Name + publisher hash).</summary>
        public const string PackageFamilyName = "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg";
    }
}
