using System;
using System.Threading.Tasks;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Enables/disables the built-in brightness slider based on whether the panel is controllable.
    // When false (internal panel off / external monitor): slider is disabled (blocks interaction)
    // AND the whole brightness bar is dimmed, so it reads as grayed-out. Mirrors the AMDDisplay*
    // Supported pattern but also dims the surrounding row Border for a clearer "unavailable" look.
    internal class PanelBrightnessSupportedProperty : WidgetControlEnabledProperty<Slider>
    {
        private readonly Border row;

        public PanelBrightnessSupportedProperty(Slider inSlider, Border inRow, Page inOwner)
            : base(Function.PanelBrightnessSupported, inSlider, inOwner)
        {
            row = inRow;
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            base.SetControlEnabled(isEnabled); // slider.IsEnabled
            if (row != null)
            {
                row.Opacity = isEnabled ? 1.0 : 0.4; // dim the whole bar when unavailable
            }
        }

        // Batch sync (BatchGet) bypasses Sync(); the base enables the control unconditionally, which
        // would wrongly light up the bar when the panel is off. Re-assert the real state instead.
        public override async Task OnBatchSyncCompleted()
        {
            if (Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetControlEnabled(Value));
            }
        }
    }
}
