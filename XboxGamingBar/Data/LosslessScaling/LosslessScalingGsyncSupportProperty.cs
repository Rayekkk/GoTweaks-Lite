using Shared.Enums;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingGsyncSupportProperty : WidgetToggleProperty
    {
        public LosslessScalingGsyncSupportProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingGsyncSupport, inUI, inOwner)
        {
        }

        protected override void ToggleSwitch_ValueChanged(object sender, RoutedEventArgs e)
        {
            bool before = Value;
            base.ToggleSwitch_ValueChanged(sender, e);
            if (Value != before)
            {
                (Owner as GamingWidget)?.MarkLosslessScalingSettingsDirty();
            }
        }
    }
}
