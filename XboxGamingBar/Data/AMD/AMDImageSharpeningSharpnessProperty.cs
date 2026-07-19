using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDImageSharpeningSharpnessProperty : WidgetSliderProperty
    {
        public AMDImageSharpeningSharpnessProperty(Slider inControl, Page inOwner) : base(50, Function.AMDImageSharpeningSharpness, inControl, inOwner)
        {
        }

        // Same double-send bug as AMDRadeonSuperResolutionEnabledProperty, slider variant.
        protected override void Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
        }
    }
}
