using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonSuperResolutionSharpnessProperty : WidgetSliderProperty
    {
        public AMDRadeonSuperResolutionSharpnessProperty(Slider inControl, Page inOwner) : base(75, Function.AMDRadeonSuperResolutionSharpness, inControl, inOwner)
        {
        }

        // Same double-send bug as AMDRadeonSuperResolutionEnabledProperty, slider variant
        // (matches CPUEPPProperty's already-fixed pattern) - the debounced SetProfileField intent
        // from SettingChangedDebounced already handles the send.
        protected override void Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
        }
    }
}
