using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Single delimited string holding the GoTweaks Haptics config. Format:
    ///   "&lt;masterOn0/1&gt;|face:&lt;on&gt;,&lt;intensity0-100&gt;|front:&lt;on&gt;,&lt;i&gt;|back:&lt;on&gt;,&lt;i&gt;|trigger:&lt;on&gt;,&lt;i&gt;"
    /// Parsed by GoTweaksHapticManager wiring in Program startup.
    /// </summary>
    internal class GoTweaksHapticsConfigProperty : HelperProperty<string, SettingsManager>
    {
        // Master off by default; all groups on at full intensity so enabling the master
        // immediately produces haptics on every button without further setup.
        public const string Default = "0|face:1,100|front:1,100|back:1,100|trigger:1,100|rel:0|pulse:10";
        private const string SettingsKey = "GoTweaksHapticsConfig";

        public GoTweaksHapticsConfigProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.GoTweaksHapticsConfig, inManager)
        {
            Logger.Info($"GoTweaksHapticsConfig loaded: {Value}");
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
            Logger.Info($"GoTweaksHapticsConfig changed: {Value}");
            SaveToSettings();
        }
    }
}
