namespace Shared.Enums
{
    /// <summary>
    /// Known device types with specific feature support.
    /// </summary>
    public enum DeviceType
    {
        /// <summary>
        /// Unknown or generic device - uses basic TDP presets only
        /// </summary>
        Generic = 0,

        /// <summary>
        /// Lenovo Legion Go (Z1 Extreme) - Models: 83E1
        /// Full feature support: WMI TDP modes, controller remapping, RGB lighting
        /// </summary>
        LegionGo = 1,

        /// <summary>
        /// Lenovo Legion Go 2 (Ryzen AI) - Models: 83N0 (8ASP2, 8AHP2 variants)
        /// Full feature support similar to Legion Go
        /// </summary>
        LegionGo2 = 2,

        /// <summary>
        /// Lenovo Legion Go S (budget model) - Different hardware, may have limited features
        /// </summary>
        LegionGoS = 3,

        // RESERVED - GPD support removed 2026-07-20 (Legion-only build). Members kept: explicit
        // numeric values, no longer detected by DeviceDetector but preserved for wire/profile compat.
        GPDWinMini = 50,
        GPDWin4 = 51,
        GPDWin5 = 52,

        // Future device types can be added here:
        // AyaNeo = 10,
        // SteamDeck = 20,
        // ROGAlly = 30,
        // MSIClaw = 40,
    }
}
