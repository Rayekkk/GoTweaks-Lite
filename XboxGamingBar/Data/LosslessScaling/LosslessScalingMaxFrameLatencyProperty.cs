using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingMaxFrameLatencyProperty : WidgetSliderProperty
    {
        public LosslessScalingMaxFrameLatencyProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingMaxFrameLatency, inUI, inOwner)
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
