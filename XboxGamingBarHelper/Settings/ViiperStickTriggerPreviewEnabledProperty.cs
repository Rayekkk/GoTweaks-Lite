using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Live-preview switch for the VIIPER Sticks &amp; Triggers panel. The
    /// widget flips this to true when the section is expanded so the helper
    /// starts pumping ~30 Hz raw input samples down
    /// <see cref="Function.Viiper_StickTriggerLiveSample"/>; flips it false
    /// on collapse to silence the stream.
    ///
    /// Persisted so a user who reloads the widget mid-tweak keeps the
    /// preview active without re-expanding the section, but defaults to
    /// false on first launch.
    /// </summary>
    internal class ViiperStickTriggerPreviewEnabledProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = false;
        private const string SettingsKey = "ViiperStickTriggerPreviewEnabled";

        public ViiperStickTriggerPreviewEnabledProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_StickTriggerPreviewEnabled, inManager)
        {
            Logger.Info($"ViiperStickTriggerPreviewEnabled loaded: {Value}");
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
            Logger.Info($"ViiperStickTriggerPreviewEnabled changed: {Value}");
            SaveToSettings();
        }
    }
}
