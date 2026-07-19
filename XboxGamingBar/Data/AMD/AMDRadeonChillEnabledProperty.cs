using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonChillEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonChillEnabled, inUI, inOwner)
        {
        }

        // See AMDRadeonSuperResolutionEnabledProperty for the full rationale - same double-send
        // bug (races the SetProfileField intent from AMDRadeonChillToggle_Toggled -> SettingChanged).
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
