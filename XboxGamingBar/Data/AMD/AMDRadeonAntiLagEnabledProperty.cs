using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonAntiLagEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonAntiLagEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonAntiLagEnabled, inUI, inOwner)
        {
        }

        // See AMDRadeonSuperResolutionEnabledProperty for the full rationale - same double-send
        // bug (races the SetProfileField intent from AMDRadeonAntiLagToggle_Toggled -> SettingChanged).
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
