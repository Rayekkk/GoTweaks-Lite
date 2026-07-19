using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void LegionDesktopControls_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isApplyingHelperUpdate)
                return;

            // The helper expands this named preset into joystick-as-mouse plus its mapping
            // dictionary and publishes those confirmed component values back to the widget.
            Logger.Info($"Desktop Controls requested: {LegionDesktopControlsToggle?.IsOn ?? false}");
        }
    }
}
