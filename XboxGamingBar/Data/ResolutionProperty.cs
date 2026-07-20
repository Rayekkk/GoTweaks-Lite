using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class ResolutionProperty : WidgetControlProperty<string, ComboBox>
    {
        public bool IsUpdatingUI { get; private set; }
        public ResolutionProperty(ComboBox inUI, Page inOwner) : base("1920x1080", Function.Resolution, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string strValue && strValue != Value)
            {
                Logger.Info($"{Function} combo box requested {strValue}.");
            }
        }

        // [widget-side fix, 2.0 rebuild] CoreDispatcher.RunAsync given an async lambda completes
        // its IAsyncAction at the lambda's first await, not at true completion - so two
        // overlapping NotifyPropertyChanged calls (two Value pushes close together) could have
        // their guarded regions interleave: the FIRST call's finally could reset IsUpdatingUI
        // while the SECOND call's delayed retry (mid Task.Delay) still has a pending
        // UI.SelectedIndex write, or vice versa. A generation token makes a superseded call's
        // retry chain a no-op instead of racing the newer one's guard.
        private int selectRequestToken;

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} combo box value to {Value}.");
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
                        if (UI.Items[i] is string strValue && strValue == Value)
                        {
                            Logger.Info($"{Function} combo box selected index {i} ({Value}).");
                            UI.SelectedIndex = i;
                            found = true;
                            break;
                        }
                    }
                    if (!found && retryCount < 3)
                    {
                        Logger.Info($"{Function} value {Value} not found in ComboBox items (count={UI.Items.Count}), retry {retryCount + 1}/3...");
                        await System.Threading.Tasks.Task.Delay(100);
                        if (myToken != selectRequestToken)
                        {
                            return;
                        }
                        await SelectValueInComboBox(myToken, retryCount + 1);
                    }
                    else if (!found)
                    {
                        Logger.Warn($"{Function} value {Value} not found in ComboBox items after retries.");
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
