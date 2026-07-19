using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillMaxFPSProperty : WidgetSliderProperty
    {
        public AMDRadeonChillMaxFPSProperty(Slider inControl, Page inOwner) : base(60, Function.AMDRadeonChillMaxFPS, inControl, inOwner)
        {
        }

        // Same double-send bug as AMDRadeonSuperResolutionEnabledProperty, slider variant.
        protected override void Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
        }
    }
}
