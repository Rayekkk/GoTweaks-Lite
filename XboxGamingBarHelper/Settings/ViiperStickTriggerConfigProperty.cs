using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// VIIPER per-stick and per-trigger shaping config — deadzone shape,
    /// per-axis deadzones, anti-deadzones, and sensitivity curves for both
    /// sticks (LS/RS) and both triggers (LT/RT). Persisted as a single
    /// flat string via StickTriggerConfigBundle's hand-rolled serializer
    /// (one Function enum entry instead of 24).
    /// </summary>
    internal class ViiperStickTriggerConfigProperty : HelperProperty<string, SettingsManager>
    {
        public const string DefaultSerialized = ""; // empty → Bundle.Default on deserialize
        private const string SettingsKey = "ViiperStickTriggerConfig";

        public ViiperStickTriggerConfigProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_StickTriggerConfig, inManager)
        {
            Logger.Info($"ViiperStickTriggerConfig loaded: '{Value}'");
        }

        private static string LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var value) && value != null)
            {
                return value;
            }
            return DefaultSerialized;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value ?? DefaultSerialized);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperStickTriggerConfig changed: '{Value}'");
            SaveToSettings();
        }
    }
}
