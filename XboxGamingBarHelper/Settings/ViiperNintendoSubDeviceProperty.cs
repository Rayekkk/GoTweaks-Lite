using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Sub-device selector used when the VIIPER device type is "nintendo" — the
    /// widget's parent grouping for Switch-family virtual devices.
    /// Valid tags and their libviiper resolution:
    ///   "switchpro"      → switchpro (Nintendo Switch Pro Controller, full pad)
    ///   "switchpro2"     → ns2pro (Nintendo Switch 2 Pro Controller; ported
    ///                       from Cookiekira/VIIPER#ns2pro into our local
    ///                       libviiper alongside the usb.Descriptor additions
    ///                       for MS OS 1.0 string probe + slim
    ///                       ConfigurationDescriptor)
    ///   "joycon-left"    → joycon-left (libviiper alias for switchpro + left
    ///                       profile, single USB device on the bus)
    ///   "joycon-right"   → joycon-right (same idea, right-side profile)
    ///   "joycon-pair"    → both joycon-left AND joycon-right published on the
    ///                       bus simultaneously. The manager owns this dual-add
    ///                       path; this property only signals intent.
    /// </summary>
    internal class ViiperNintendoSubDeviceProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "switchpro";
        private const string SettingsKey = "ViiperNintendoSubDevice";

        public ViiperNintendoSubDeviceProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_NintendoSubDevice, inManager)
        {
            Logger.Info($"ViiperNintendoSubDevice loaded: {Value}");
        }

        private static string LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            return Default;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value ?? Default);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperNintendoSubDevice changed: {Value}");
            SaveToSettings();
        }

        /// <summary>
        /// Returns the libviiper target alias for the primary device.
        /// For "joycon-pair" this is the left Joy-Con; the manager adds a
        /// second device (joycon-right) on top to complete the pair.
        /// </summary>
        public static string ResolvePrimaryLibViiperTarget(string subDevice)
        {
            switch (subDevice)
            {
                case "switchpro":      return "switchpro";
                case "switchpro2":     return "ns2pro";
                case "joycon-left":    return "joycon-left";
                case "joycon-right":   return "joycon-right";
                case "joycon-pair":    return "joycon-left";
                default:               return "switchpro";
            }
        }

        /// <summary>
        /// Returns the secondary libviiper target when the sub-device requires
        /// publishing TWO devices on the bus (currently only joycon-pair).
        /// Empty string means "single-device, no secondary needed".
        /// </summary>
        public static string ResolveSecondaryLibViiperTarget(string subDevice)
        {
            return subDevice == "joycon-pair" ? "joycon-right" : string.Empty;
        }
    }
}
