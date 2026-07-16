using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Uses a ToggleSwitch: ON = PERFORMANCE, OFF = BALANCED
    internal class LosslessScalingSizeProperty : WidgetControlProperty<string, ToggleSwitch>
    {
        public LosslessScalingSizeProperty(ToggleSwitch inUI, Page inOwner)
            : base("BALANCED", Function.LosslessScalingSize, inUI, inOwner)
        {
            if (UI != null)
            {
                // See LosslessScalingScalingTypeProperty (a ComboBox sibling) for why this
                // proactive sync matters in principle; for a ToggleSwitch the visible symptom
                // is milder (no placeholder state), but this keeps the same defensive pattern
                // in case the constructor default and the real value happen to already agree.
                SyncToggleFromValue();
                UI.Toggled += Toggle_Toggled;
            }
        }

        private void SyncToggleFromValue()
        {
            if (UI == null) return;
            UI.IsOn = Value == "PERFORMANCE";
        }

        private void Toggle_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            string newValue = UI.IsOn ? "PERFORMANCE" : "BALANCED";
            if (newValue != Value)
            {
                Logger.Info($"{Function} toggle updated to {newValue}.");
                SetValue(newValue);
                (Owner as GamingWidget)?.MarkLosslessScalingSettingsDirty();
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, SyncToggleFromValue);
            }
        }
    }
}
