using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // LS1's own sharpness field (Settings.xml "LS1Sharpness"), separate from the general
    // Sharpness field used by FSR/NIS/SGSR/BCAS (LosslessScalingSharpnessProperty).
    internal class LosslessScalingLS1SharpnessProperty : WidgetSliderProperty
    {
        public LosslessScalingLS1SharpnessProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingLS1Sharpness, inUI, inOwner)
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
