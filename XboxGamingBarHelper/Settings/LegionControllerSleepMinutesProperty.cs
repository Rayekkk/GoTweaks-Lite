using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Legion controller auto-sleep (idle power-off) timeout in minutes. 0 = never sleep.
    /// Applied to the controllers via HID sub-command 0x09 (see LegionButtonMonitor.SetAutoSleepTime).
    /// </summary>
    internal class LegionControllerSleepMinutesProperty : HelperProperty<int, SettingsManager>
    {
        public const int Default = 15;
        public const int Minimum = 0;
        public const int Maximum = 255;
        private const string SettingsKey = "LegionControllerSleepMinutes";

        public LegionControllerSleepMinutesProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.LegionControllerSleepMinutes, inManager)
        {
            Logger.Info($"LegionControllerSleepMinutes loaded: {Value}");
        }

        private static int LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value))
            {
                if (value < Minimum) value = Minimum;
                if (value > Maximum) value = Maximum;
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
            Logger.Info($"LegionControllerSleepMinutes changed: {Value}");
            SaveToSettings();
        }
    }
}
