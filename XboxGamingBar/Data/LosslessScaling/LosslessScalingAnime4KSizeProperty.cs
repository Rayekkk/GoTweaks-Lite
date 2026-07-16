using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingAnime4KSizeProperty : WidgetControlProperty<string, ComboBox>
    {
        public LosslessScalingAnime4KSizeProperty(ComboBox inUI, Page inOwner) : base("Medium", Function.LosslessScalingAnime4KSize, inUI, inOwner)
        {
            if (UI != null)
            {
                // See LosslessScalingScalingTypeProperty for why this proactive sync is needed:
                // GenericProperty.SetValue skips NotifyPropertyChanged when the incoming value
                // equals this constructor default, which it does for the common "Medium" case.
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
