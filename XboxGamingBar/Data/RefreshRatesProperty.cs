using Shared.Constants;
using Shared.Enums;
using System;
using System.Collections.Generic;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RefreshRatesProperty : WidgetControlProperty<List<int>, ComboBox>
    {
        // Reference to the RefreshRateProperty to restore selection after list updates
        private RefreshRateProperty refreshRateProperty;

        public RefreshRatesProperty(ComboBox inUI, Page inOwner) : base(new List<int>() { SystemConstants.DEFAULT_REFRESH_RATE }, Function.RefreshRates, inUI, inOwner)
        {
        }

        /// <summary>
        /// Sets the RefreshRateProperty reference so we can restore selection after list updates.
        /// </summary>
        public void SetRefreshRateProperty(RefreshRateProperty property)
        {
            refreshRateProperty = property;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} slider value {Value}.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UI.Items.Clear();
                    foreach (var value in Value)
                    {
                        UI.Items.Add(value);
                    }

                    // After updating items, restore the current refresh rate selection
                    // This handles the case where the value doesn't change but items do (e.g., dock/undock)
                    if (refreshRateProperty != null)
                    {
                        var currentRate = refreshRateProperty.Value;
                        for (int i = 0; i < UI.Items.Count; i++)
                        {
                            if (UI.Items[i] is int rate && rate == currentRate)
                            {
                                Logger.Info($"Restoring {Function} selection to index {i} ({currentRate}Hz) after list update.");
                                // [widget-side fix, 2.0 rebuild] Without this guard, a live
                                // (non-batch) helper push of the available-rates list (e.g.
                                // dock/undock) restoring the SAME selection still fires
                                // RefreshRatesComboBox.SelectionChanged -> SettingChanged, which
                                // would re-send a SetProfileField(RefreshRate) intent as if the
                                // user had just picked it - same bug class as the already-fixed
                                // IntTagComboProperty.
                                WidgetSliderProperty.HelperSyncCount++;
                                try
                                {
                                    UI.SelectedIndex = i;
                                }
                                finally
                                {
                                    WidgetSliderProperty.HelperSyncCount--;
                                }
                                break;
                            }
                        }
                    }
                });
            }
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            // Refresh rates combo box should be enabled/disabled by RefreshRateProperty, not this.
            // base.SetControlEnabled(isEnabled);
        }
    }
}
