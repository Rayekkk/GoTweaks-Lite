using System;
using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.RTSS
{
    /// <summary>
    /// Property for receiving display/OSD configuration from the widget.
    /// Routes position shift settings to RTSSManager and adaptive brightness to SystemManager.
    /// </summary>
    internal class DisplayOSDConfigProperty : HelperProperty<string, RTSSManager>
    {
        private const string SettingsKey = "DisplayOSDConfig";
        private readonly Action<bool> setAdaptiveBrightness;

        public DisplayOSDConfigProperty(RTSSManager inManager, Action<bool> adaptiveBrightnessCallback)
            : base("", null, Function.OLEDConfig, inManager)
        {
            setAdaptiveBrightness = adaptiveBrightnessCallback;
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var saved) && !string.IsNullOrWhiteSpace(saved))
            {
                SetValueSilent(saved);
                manager.ParseDisplayOSDConfig(saved, setAdaptiveBrightness);
            }
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is string configString)
            {
                manager.ParseDisplayOSDConfig(configString, setAdaptiveBrightness);
                LocalSettingsHelper.SetValue(SettingsKey, configString);
            }
            return result;
        }
    }
}
