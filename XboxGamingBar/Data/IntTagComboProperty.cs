using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Shared ComboBox-backed property for int-valued selectors where each ComboBoxItem's
    /// Tag is the value's string representation (e.g. "0", "60", "300"). Mirrors
    /// ViiperStringComboProperty's pattern: the UI is synced to the constructor's initial
    /// Value BEFORE SelectionChanged is wired up, so a XAML-mount default selection (index
    /// 0) never races the real helper-synced value and gets echoed back up as a bogus
    /// user change.
    /// </summary>
    internal class IntTagComboProperty : WidgetControlProperty<int, ComboBox>
    {
        // Set true only for a Function whose control change is ALSO sent via an explicit
        // SetProfileField intent elsewhere (e.g. AMDRadeonBoostResolutionComboBox ->
        // SettingChanged) - otherwise this class's own send below would race that intent,
        // the same double-send bug found in the AMD toggle/slider properties.
        private readonly bool suppressAutoSend;

        public IntTagComboProperty(int initialValue, Function inFunction, ComboBox inUI, Page inOwner, bool suppressAutoSend = false)
            : base(initialValue, inFunction, inUI, inOwner)
        {
            this.suppressAutoSend = suppressAutoSend;
            if (UI != null)
            {
                SyncSelectedIndexFromValue();
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void SyncSelectedIndexFromValue()
        {
            if (UI == null) return;
            string targetTag = Value.ToString();
            for (int i = 0; i < UI.Items.Count; i++)
            {
                if (UI.Items[i] is ComboBoxItem item && (item.Tag as string) == targetTag)
                {
                    if (UI.SelectedIndex != i)
                    {
                        UI.SelectedIndex = i;
                    }
                    return;
                }
            }
            if (UI.Items.Count > 0 && UI.SelectedIndex < 0)
            {
                UI.SelectedIndex = 0;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = UI.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is string tagString && int.TryParse(tagString, out int newValue))
            {
                if (newValue != Value)
                {
                    if (suppressAutoSend)
                    {
                        // Don't send here - an explicit SetProfileField intent elsewhere already
                        // owns the send for this Function (see the field's doc comment). But this
                        // property's cached Value is what that intent's handler reads
                        // (TryGetAmdProfileFieldIntent reads amdRadeonBoostResolution.Value, not
                        // the ComboBox directly) - without updating it here, the intent would keep
                        // sending the value from BEFORE this selection change, so the combobox
                        // would appear to do nothing.
                        Logger.Info($"{Function} combo box updated to {newValue} (send owned by SetProfileField intent).");
                        SetValueSilent(newValue, DateTime.Now.Ticks);
                        return;
                    }

                    Logger.Info($"{Function} combo box updated to {newValue}.");
                    // Bug fix: omitting the timestamp defaults SetValue's updatedTime to 0.
                    // PropertyUpdateArbiter (issue #79) rejects a 0-timestamped edit as stale once
                    // this property has ever had a real prior timestamp (i.e. every edit after the
                    // first would silently no-op). Pass an explicit timestamp, matching
                    // WidgetToggleProperty's convention.
                    SetValue(newValue, DateTime.Now.Ticks);
                }
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, SyncSelectedIndexFromValue);
            }
        }
    }
}
