using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void LegionNintendoLayout_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isApplyingHelperUpdate)
                return;

            // The paired WidgetProperty relays this direct boolean intent. Firmware mapping,
            // persistence and the confirmed effective state all belong to the helper.
            Logger.Info($"Nintendo Layout requested: {LegionNintendoLayoutToggle?.IsOn ?? false}");
        }
    }
}
