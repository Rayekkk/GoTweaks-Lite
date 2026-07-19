using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDFluidMotionFrameEnabledProperty : WidgetToggleProperty
    {
        public AMDFluidMotionFrameEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDFluidMotionFrameEnabled, inUI, inOwner)
        {
        }

        // See AMDRadeonSuperResolutionEnabledProperty for the full rationale - same double-send
        // bug. For AFMF specifically this also caused the "enabling AFMF actually enables
        // Anti-Lag" confusion: the SetProfileField path (AMDFluidMotionFrameToggle_ProfileToggled)
        // correctly forces RadeonAntiLag on together with FluidMotionFrames (a real AMD driver
        // requirement), while this class's own auto-send raced it with a plain single-field
        // FluidMotionFrames Set that carried no such coupling - whichever write landed last on the
        // driver determined which feature visibly ended up enabled.
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
