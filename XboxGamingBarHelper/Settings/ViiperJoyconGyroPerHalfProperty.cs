using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// VIIPER joycon-pair gyro routing. Persisted globally.
    /// When true, each emulated Joy-Con half is driven by its matching physical
    /// Legion controller IMU (left half ← left controller, right half ← right
    /// controller). When false (default), both halves share the single selected
    /// gyro source. Only meaningful for the joycon-pair target.
    /// </summary>
    internal class ViiperJoyconGyroPerHalfProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = false;
        private const string SettingsKey = "ViiperJoyconGyroPerHalf";

        public ViiperJoyconGyroPerHalfProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_JoyconGyroPerHalf, inManager)
        {
            Logger.Info($"ViiperJoyconGyroPerHalf loaded: {Value}");
        }

        private static bool LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var value))
            {
                return value;
            }
            return Default;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperJoyconGyroPerHalf changed: {Value}");
            SaveToSettings();
        }
    }
}
