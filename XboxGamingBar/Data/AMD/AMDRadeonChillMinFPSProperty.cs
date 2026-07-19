using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillMinFPSProperty : WidgetSliderProperty
    {
        public AMDRadeonChillMinFPSProperty(Slider inControl, Page inOwner) : base(30, Function.AMDRadeonChillMinFPS, inControl, inOwner)
        {
        }

        // Same double-send bug as AMDRadeonSuperResolutionEnabledProperty, slider variant.
        protected override void Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
        }
    }
}
