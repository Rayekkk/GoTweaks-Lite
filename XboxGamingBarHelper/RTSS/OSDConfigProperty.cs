using Shared.Enums;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.RTSS
{
    internal class OSDConfigProperty : HelperProperty<string, RTSSManager>
    {
        private const string SettingsKey = "OSDConfig";

        public OSDConfigProperty(RTSSManager inManager) : base("", null, Function.OSDConfig, inManager)
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var saved) && !string.IsNullOrWhiteSpace(saved))
            {
                SetValueSilent(saved);
                manager.ParseOSDConfig(saved);
            }
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is string configString)
            {
                manager.ParseOSDConfig(configString);
                LocalSettingsHelper.SetValue(SettingsKey, configString);
            }
            return result;
        }
    }
}
