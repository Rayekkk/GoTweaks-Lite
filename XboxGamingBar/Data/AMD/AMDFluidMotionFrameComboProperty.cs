using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Generic ComboBox property for AFMF 2.x extended controls (Algorithm, SearchMode,
    /// PerformanceMode, FastMotionResponse). The ADLX enums are 0-indexed and contiguous,
    /// so the ComboBox's SelectedIndex is the value verbatim — no index→value mapping
    /// table is needed. These are global (non-profile) settings; widget→helper is a
    /// straight write, helper→widget reflects the live driver value at startup.
    /// </summary>
    internal class AMDFluidMotionFrameComboProperty : WidgetControlProperty<int, ComboBox>
    {
        private bool isUpdatingUI;

        public AMDFluidMotionFrameComboProperty(Function function, ComboBox inUI, Page inOwner)
            : base(0, function, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                if (UI.SelectedIndex != Value) UI.SelectedIndex = Value;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI) return;
            int idx = UI.SelectedIndex;
            if (idx < 0 || idx == Value) return;
            Logger.Info($"{Function} ComboBox -> {idx}");
            // Bug fix: omitting the timestamp defaults SetValue's updatedTime to 0.
            // PropertyUpdateArbiter (issue #79) coerces a 0 timestamp to "now" only when
            // this property has never had a real prior timestamp - once ANY edit has
            // succeeded (lastUpdatedTime > 0), a subsequent 0-timestamped edit is rejected
            // as stale, silently dropping the change. WidgetToggleProperty already passes
            // DateTime.Now.Ticks explicitly for exactly this reason; this class didn't.
            SetValue(idx, DateTime.Now.Ticks);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (UI.SelectedIndex == Value) return;
                    isUpdatingUI = true;
                    try
                    {
                        UI.SelectedIndex = Value;
                    }
                    finally
                    {
                        isUpdatingUI = false;
                    }
                });
            }
        }
    }
}
