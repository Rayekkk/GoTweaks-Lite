using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDImageSharpeningEnabledProperty : WidgetToggleProperty
    {
        public AMDImageSharpeningEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDImageSharpeningEnabled, inUI, inOwner)
        {
        }

        // See AMDRadeonSuperResolutionEnabledProperty for the full rationale - same double-send
        // bug (generic bound-control auto-send racing the SetProfileField intent from
        // AMDImageSharpeningToggle_Toggled -> SettingChanged).
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
