using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonBoostEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonBoostEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonBoostEnabled, inUI, inOwner)
        {
        }

        // See AMDRadeonSuperResolutionEnabledProperty for the full rationale - same double-send
        // bug (races the SetProfileField intent from AMDRadeonBoostToggle_Toggled -> SettingChanged).
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
