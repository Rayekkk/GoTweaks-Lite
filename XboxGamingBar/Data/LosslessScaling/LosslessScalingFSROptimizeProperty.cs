using Shared.Enums;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingFSROptimizeProperty : WidgetToggleProperty
    {
        public LosslessScalingFSROptimizeProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingFSROptimize, inUI, inOwner)
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
