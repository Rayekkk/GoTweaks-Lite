using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonSuperResolutionEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonSuperResolutionEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonSuperResolutionEnabled, inUI, inOwner)
        {
        }

        // Profile edits use the explicit SetProfileField intent (AMDRadeonSuperResolutionToggle_Toggled
        // -> SettingChanged). Without this override, the generic bound-control path ALSO sent a raw
        // Set for this Function on every click - two independent, racing round-trips per toggle,
        // matching CPUBoostProperty's already-fixed pattern. The second of the two to reach the
        // driver saw ADLX_ALREADY_ENABLED (not a real failure) and surfaced as "the helper rejected
        // an invalid or stale value" despite the toggle having actually applied.
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
