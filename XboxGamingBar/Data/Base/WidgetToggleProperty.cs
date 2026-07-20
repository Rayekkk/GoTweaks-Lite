using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetToggleProperty : WidgetControlProperty<bool, ToggleSwitch>
    {
        /// <summary>
        /// Flag to indicate when the UI is being updated programmatically (from helper sync).
        /// When true, Toggled events should not send values back to the helper.
        /// </summary>
        private bool isUpdatingUI;

        /// <summary>
        /// Public accessor for isUpdatingUI. External handlers (e.g., PerGameProfileToggle_Changed)
        /// can check this to skip profile-creation logic when the toggle was set by helper pipe sync.
        /// </summary>
        public bool IsUpdatingUI => isUpdatingUI;

        public WidgetToggleProperty(bool inValue, Function inFunction, ToggleSwitch inUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.Toggled += ToggleSwitch_ValueChanged;
                UI.IsOn = inValue;
            }
        }

        /// <summary>
        /// Sets the bound ToggleSwitch's IsOn programmatically without echoing back to the
        /// helper. For a control ALSO independently driven by another property (e.g.
        /// RunningGameProperty force-resetting the shared PerGameProfileToggle when a game
        /// closes) - that other owner has no way to guard THIS property's own Toggled handler,
        /// so it must go through this method instead of touching UI.IsOn directly.
        /// </summary>
        internal void SetControlValueSilently(bool value)
        {
            if (UI == null) return;
            isUpdatingUI = true;
            try
            {
                UI.IsOn = value;
            }
            finally
            {
                isUpdatingUI = false;
            }
        }

        protected virtual void ToggleSwitch_ValueChanged(object sender, RoutedEventArgs e)
        {
            // Skip if UI is being updated programmatically (from helper sync)
            // This prevents echoing values back to the helper and potential profile corruption
            if (isUpdatingUI)
            {
                return;
            }

            SetValue(UI.IsOn, DateTime.Now.Ticks);
        }

        // [full-audit fix, 2026-07-20 — B4/B5/B6] Reverted to the original SYNCHRONOUS bracket -
        // see WidgetSliderProperty.NotifyPropertyChanged for the full rationale (the 200ms grace
        // window caused more problems than it solved; the real echo is closed by the
        // timing-independent value-equality no-op IsFieldIntentUnchanged instead).
        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} value {Value}.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Set flags to prevent Toggled handler from echoing value back
                    // and to prevent SettingChanged from auto-saving during helper sync
                    WidgetSliderProperty.HelperSyncCount++;
                    isUpdatingUI = true;
                    try
                    {
                        UI.IsOn = Value;
                    }
                    finally
                    {
                        isUpdatingUI = false;
                        WidgetSliderProperty.HelperSyncCount--;
                    }
                });
            }
        }
    }
}
