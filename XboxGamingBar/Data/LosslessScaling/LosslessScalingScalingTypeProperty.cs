using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingScalingTypeProperty : WidgetControlProperty<string, ComboBox>
    {
        public LosslessScalingScalingTypeProperty(ComboBox inUI, Page inOwner) : base("Off", Function.LosslessScalingScalingType, inUI, inOwner)
        {
            if (UI != null)
            {
                // Sync the UI to the constructor's initial value BEFORE wiring SelectionChanged.
                // GenericProperty.SetValue no-ops (skips NotifyPropertyChanged) when the incoming
                // BatchGet value equals this default - which it does whenever the real LS profile
                // is left at "Off" (the common case) - so without this, the ComboBox would sit at
                // SelectedIndex=-1 (showing PlaceholderText) forever even though Value is correct.
                SyncSelectedIndexFromValue();
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void SyncSelectedIndexFromValue()
        {
            if (UI == null) return;
            for (var i = 0; i < UI.Items.Count; i++)
            {
                if (UI.Items[i] is string stringValue && stringValue == Value)
                {
                    UI.SelectedIndex = i;
                    return;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string stringValue && stringValue != Value)
            {
                Logger.Info($"{Function} combo box updated to {stringValue}.");
                SetValue(stringValue);
                (Owner as GamingWidget)?.MarkLosslessScalingSettingsDirty();
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
