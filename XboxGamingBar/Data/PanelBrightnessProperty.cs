using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Built-in display (panel) brightness, 0-100 %. Bound to the optional Quick-tab brightness
    // slider. WidgetSliderProperty gives the standard 500 ms debounce + echo guards, so dragging
    // the slider applies to the panel via the helper (WMI) without flooding, and helper BatchGet
    // seeds the real current brightness into the slider.
    internal class PanelBrightnessProperty : WidgetSliderProperty
    {
        public PanelBrightnessProperty(Slider inControl, Windows.UI.Xaml.Controls.Page inOwner)
            : base(50, Function.PanelBrightness, inControl, inOwner)
        {
        }
    }
}
