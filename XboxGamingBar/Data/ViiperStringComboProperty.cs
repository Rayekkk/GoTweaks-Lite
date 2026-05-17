using System;
using System.Threading;
using System.Threading.Tasks;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Shared ComboBox-backed property for VIIPER string selectors (device type,
    /// input source, gyro source, Steam sub-device). The ComboBoxItem's Tag string
    /// is the canonical value sent to the helper.
    /// </summary>
    internal class ViiperStringComboProperty : WidgetControlProperty<string, ComboBox>
    {
        // SelectionChanged fires on every intermediate item when the user navigates
        // a closed combo with keyboard/D-pad (UWP's default behavior). For the device-
        // type combo each fire triggers a 1-2 s VIIPER hot-swap, so scrolling from
        // "Xbox 360" to "Steam Controller" used to cascade through DS4, DSE, and
        // xboxelite2 before settling — visible as a stutter of virtual pads in joy.cpl
        // and "VIIPER hot-swap" spam in the helper log. Debounce the commit so only
        // the value the user settles on is sent to the helper.
        private const int DebounceMs = 500;
        private CancellationTokenSource debounceCts;

        public ViiperStringComboProperty(string initialValue, Function inFunction, ComboBox inUI, Page inOwner)
            : base(initialValue, inFunction, inUI, inOwner)
        {
            if (UI != null)
            {
                // Set selected index to match initial value BEFORE wiring SelectionChanged, so the
                // ComboBox renders the right item from the start. NotifyPropertyChanged only fires
                // when Value actually changes — if the helper syncs the same value as our initial,
                // the UI would otherwise never get updated and the combo appears empty.
                SyncSelectedIndexFromValue();
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void SyncSelectedIndexFromValue()
        {
            if (UI == null) return;
            string targetTag = Value ?? string.Empty;
            for (int i = 0; i < UI.Items.Count; i++)
            {
                var item = UI.Items[i] as ComboBoxItem;
                if (item != null && (item.Tag as string) == targetTag)
                {
                    if (UI.SelectedIndex != i)
                    {
                        UI.SelectedIndex = i;
                    }
                    return;
                }
            }
            // Value doesn't match any item — fall back to first item so the combo isn't empty.
            if (UI.Items.Count > 0 && UI.SelectedIndex < 0)
            {
                UI.SelectedIndex = 0;
            }
        }

        private async void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = UI.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;
            var tagString = selectedItem.Tag as string;
            if (tagString == null || tagString == Value) return;

            // Cancel any pending commit from a prior intermediate selection.
            var previous = debounceCts;
            debounceCts = new CancellationTokenSource();
            previous?.Cancel();
            previous?.Dispose();
            var token = debounceCts.Token;

            try
            {
                await Task.Delay(DebounceMs, token);
            }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;

            // Re-read the current ComboBox state at commit time — the user may have
            // arrowed past `tagString` since we started this timer, and the latest
            // tag is the one we want to ship.
            string commit = null;
            if (UI != null && UI.SelectedItem is ComboBoxItem latest)
            {
                commit = latest.Tag as string;
            }
            if (commit != null && commit != Value)
            {
                Logger.Info($"{Function} combo settled at {commit} (debounced).");
                SetValue(commit);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Find the item with matching tag.
                    string targetTag = Value ?? string.Empty;
                    for (int i = 0; i < UI.Items.Count; i++)
                    {
                        var item = UI.Items[i] as ComboBoxItem;
                        if (item != null && (item.Tag as string) == targetTag)
                        {
                            if (UI.SelectedIndex != i)
                            {
                                UI.SelectedIndex = i;
                            }
                            break;
                        }
                    }
                });
            }
        }
    }
}
