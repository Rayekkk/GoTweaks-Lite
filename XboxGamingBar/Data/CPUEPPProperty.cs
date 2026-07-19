using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CPUEPPProperty : WidgetSliderProperty
    {
        public CPUEPPProperty(int inValue, Slider inControl, Page inOwner) : base(inValue, Function.CPUEPP, inControl, inOwner)
        {
        }

        // The generic slider path is optimistic. This setting instead waits for the
        // helper's SetProfileField confirmation before its bound value is updated.
        protected override void Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
        }
    }
}
