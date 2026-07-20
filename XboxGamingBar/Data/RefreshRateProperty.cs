using System;
using Shared.Constants;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RefreshRateProperty : WidgetControlProperty<int, ComboBox>
    {
        public bool IsUpdatingUI { get; private set; }
        public RefreshRateProperty(ComboBox inUI, Page inOwner) : base(SystemConstants.DEFAULT_REFRESH_RATE, Function.RefreshRate, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is int intValue && intValue != Value)
            {
                // A selection is only a user request. GamingWidget.SettingChanged relays the
                // intent to the helper; this display property changes only after confirmation.
                Logger.Info($"{Function} combo box requested {intValue}Hz.");
            }
            else
            {
                Logger.Info($"{Function} combo box changed to {(e.AddedItems.Count > 0 ? e.AddedItems[0] : "none")}.");
            }
        }

        // [widget-side fix, 2.0 rebuild] Same guard-lifetime fix as ResolutionProperty - see its
        // comment for why a generation token is needed (CoreDispatcher.RunAsync's IAsyncAction
        // completes at an async lambda's first await, letting two overlapping
        // NotifyPropertyChanged calls interleave their guarded regions otherwise).
        private int selectRequestToken;

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} combo box value to {Value}Hz.");
                int myToken = System.Threading.Interlocked.Increment(ref selectRequestToken);
                await SelectValueInComboBox(myToken);
            }
        }

        private async System.Threading.Tasks.Task SelectValueInComboBox(int myToken, int retryCount = 0)
        {
            await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                if (myToken != selectRequestToken)
                {
                    return; // A newer NotifyPropertyChanged call superseded this one.
                }
                IsUpdatingUI = true;
                try
                {
                    bool found = false;
                    for (var i = 0; i < UI.Items.Count; i++)
                    {
                        if (UI.Items[i] is int intValue && intValue == Value)
                        {
                            Logger.Info($"{Function} combo box selected index {i} ({Value}Hz).");
                            UI.SelectedIndex = i;
                            found = true;
                            break;
                        }
                    }
                    if (!found && retryCount < 3)
                    {
                        // ComboBox items may not be populated yet (race condition with RefreshRatesProperty)
                        // Retry after a short delay
                        Logger.Info($"{Function} value {Value}Hz not found in ComboBox items (count={UI.Items.Count}), retry {retryCount + 1}/3...");
                        await System.Threading.Tasks.Task.Delay(100);
                        if (myToken != selectRequestToken)
                        {
                            return;
                        }
                        await SelectValueInComboBox(myToken, retryCount + 1);
                    }
                    else if (!found)
                    {
                        Logger.Warn($"{Function} value {Value}Hz not found in ComboBox items after retries.");
                    }
                }
                finally
                {
                    IsUpdatingUI = false;
                }
            });
        }
    }
}
