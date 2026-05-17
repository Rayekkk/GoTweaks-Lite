using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.Systems
{
    internal class AdaptiveBrightnessModeProperty : HelperProperty<int, SystemManager>
    {
        private const string SettingsKey = "AdaptiveBrightnessMode";

        public AdaptiveBrightnessModeProperty(SystemManager inManager)
            : base(LoadFromSettings(), null, Function.AdaptiveBrightnessMode, inManager)
        {
            Logger.Info($"AdaptiveBrightnessMode loaded: {(AdaptiveBrightnessMode)Value}");
        }

        private static int LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value)) return value;
            return (int)AdaptiveBrightnessMode.Windows;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            LocalSettingsHelper.SetValue(SettingsKey, Value);
            Logger.Info($"AdaptiveBrightnessMode changed to {(AdaptiveBrightnessMode)Value}");
            Manager?.OnAdaptiveBrightnessModeChanged((AdaptiveBrightnessMode)Value);
        }

        public AdaptiveBrightnessMode Mode => (AdaptiveBrightnessMode)Value;
    }
}
