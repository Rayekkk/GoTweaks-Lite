namespace Shared.Data
{
    /// <summary>
    /// Pure validation rules for a custom quick-action definition (name/shortcut length,
    /// required fields). Extracted from the helper's Program.HotkeyHandlers.cs so the rules
    /// can be exercised by Tests/Shared.Tests without a WinRT/helper host.
    /// </summary>
    public static class CustomQuickSettingValidator
    {
        public const int MaxIdLength = 128;
        public const int MaxNameLength = 80;
        public const int MaxShortcutLength = 256;

        public static bool Validate(string id, string name, string shortcut, bool delete, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength)
            {
                reason = "Invalid custom tile identifier.";
                return false;
            }
            if (!delete && (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(shortcut)))
            {
                reason = "A name and keyboard shortcut are required.";
                return false;
            }
            if (!delete && (name.Length > MaxNameLength || shortcut.Length > MaxShortcutLength))
            {
                reason = "The custom tile name or shortcut is too long.";
                return false;
            }
            return true;
        }
    }
}
