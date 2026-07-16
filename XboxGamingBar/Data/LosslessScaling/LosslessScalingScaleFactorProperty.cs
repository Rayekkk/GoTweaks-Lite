using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingScaleFactorProperty : WidgetSliderProperty
    {
        public LosslessScalingScaleFactorProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingScaleFactor, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.ValueChanged += (s, e) =>
                {
                    if (!IsUpdatingUI && (int)e.NewValue != Value)
                    {
                        (Owner as GamingWidget)?.MarkLosslessScalingSettingsDirty();
                    }
                };
            }
        }
    }
}
