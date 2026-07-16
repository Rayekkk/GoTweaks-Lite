using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingFlowScaleProperty : WidgetSliderProperty
    {
        public LosslessScalingFlowScaleProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingFlowScale, inUI, inOwner)
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
