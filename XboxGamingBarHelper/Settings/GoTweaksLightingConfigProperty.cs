using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Single delimited string holding the Legion lighting config so the property/pipe
    /// surface stays small. Format:
    ///   "&lt;mode&gt;|&lt;baseHex&gt;|&lt;flashHex&gt;|&lt;decayMs&gt;|&lt;brightness0-100&gt;|&lt;speed0-100&gt;"
    /// mode is one of: disabled, solid, pulse, rainbow, spiral, flash, cycle, perbutton.
    /// Parsed by LegionLightingManager wiring in Program startup.
    /// </summary>
    internal class GoTweaksLightingConfigProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "disabled|00AAFF|FFFFFF|500|100|100|pal:FF0000,FF8000,FFFF00,00FF00,00FFFF,0000FF,B400FF,FF00B4";
        private const string SettingsKey = "GoTweaksLightingConfig";

        public GoTweaksLightingConfigProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.GoTweaksLightingConfig, inManager)
        {
            Logger.Info($"GoTweaksLightingConfig loaded: {Value}");
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
            Logger.Info($"GoTweaksLightingConfig changed: {Value}");
            SaveToSettings();
        }
    }
}
