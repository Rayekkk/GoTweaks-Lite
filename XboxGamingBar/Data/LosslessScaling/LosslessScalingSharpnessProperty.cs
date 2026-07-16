using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingSharpnessProperty : WidgetSliderProperty
    {
        public LosslessScalingSharpnessProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingSharpness, inUI, inOwner)
        {
            if (UI != null)
            {
                // WidgetSliderProperty already guards its own ValueChanged handler with
                // IsUpdatingUI (public accessor) during helper-driven UI syncs - reuse the same
                // flag here via a second subscriber, rather than modifying the shared base class
                // just for Scale-tab dirty-tracking.
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
