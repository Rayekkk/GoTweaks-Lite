using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Sub-device selector used when the VIIPER device type is set to "sony" (the
    /// parent grouping for Sony virtual devices in the widget). Valid values map
    /// 1:1 to libviiper registry aliases so ViiperEmulationManager can pass the
    /// resolved value directly as the target type:
    ///   "dualsense"       → DualSense (non-Edge)
    ///   "dualsense-edge"  → DualSense Edge
    ///   "dualshock4"      → DualShock 4
    /// </summary>
    internal class ViiperSonySubDeviceProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "dualsense-edge";
        private const string SettingsKey = "ViiperSonySubDevice";

        public ViiperSonySubDeviceProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_SonySubDevice, inManager)
        {
            Logger.Info($"ViiperSonySubDevice loaded: {Value}");
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
            Logger.Info($"ViiperSonySubDevice changed: {Value}");
            SaveToSettings();
        }

        /// <summary>
        /// Returns the libviiper target alias corresponding to the given Sony
        /// sub-device. Both "dualsense-edge" and the legacy "dualsenseedge"
        /// resolve to the same target so existing user settings keep working
        /// if the widget Tag ever drifts.
        /// </summary>
        public static string ResolveLibViiperTarget(string subDevice)
        {
            switch (subDevice)
            {
                case "dualsense":         return "dualsense";
                case "dualsense-edge":    return "dualsenseedge";
                case "dualsenseedge":     return "dualsenseedge";
                case "dualshock4":        return "dualshock4";
                case "ds4":               return "dualshock4";
                default:                  return "dualsenseedge";
            }
        }
    }
}
