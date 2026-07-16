using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingLSFG3MultiplierProperty : WidgetControlProperty<int, ComboBox>
    {
        public LosslessScalingLSFG3MultiplierProperty(ComboBox inUI, Page inOwner) : base(2, Function.LosslessScalingLSFG3Multiplier, inUI, inOwner)
        {
            if (UI != null)
            {
                // See LosslessScalingScalingTypeProperty for why this proactive sync is needed:
                // GenericProperty.SetValue skips NotifyPropertyChanged when the incoming value
                // equals this constructor default, which it does for the common "2" case.
                SyncSelectedIndexFromValue();
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void SyncSelectedIndexFromValue()
        {
            if (UI == null) return;
            string valueStr = Value.ToString();
            for (var i = 0; i < UI.Items.Count; i++)
            {
                if (UI.Items[i] is string stringValue && stringValue == valueStr)
                {
                    UI.SelectedIndex = i;
                    return;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string stringValue && int.TryParse(stringValue, out int intValue) && intValue != Value)
            {
                Logger.Info($"{Function} combo box updated to {intValue}.");
                SetValue(intValue);
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
