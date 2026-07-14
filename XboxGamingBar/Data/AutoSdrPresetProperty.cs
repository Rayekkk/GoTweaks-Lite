using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Auto SDR curve preset (0=Legion Go 2 default, 1=Custom), bound to
    // AutoSdrPresetComboBox. Selecting Custom reveals the curve editor panel; selecting
    // Legion Go 2 hides it (the fixed default curve has nothing to show/edit).
    internal class AutoSdrPresetProperty : WidgetControlProperty<int, ComboBox>
    {
        private readonly GamingWidget widgetOwner;

        public AutoSdrPresetProperty(ComboBox inUI, GamingWidget inOwner) : base(0, Function.AutoSdrPreset, inUI, inOwner)
        {
            widgetOwner = inOwner;
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UI.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int tagValue) && tagValue != Value)
            {
                SetValue(tagValue);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    foreach (var obj in UI.Items)
                    {
                        if (obj is ComboBoxItem item && item.Tag?.ToString() == Value.ToString())
                        {
                            UI.SelectedItem = item;
                            break;
                        }
                    }
                    widgetOwner?.ApplyAutoSdrPreset(Value);
                });
            }
        }
    }
}
