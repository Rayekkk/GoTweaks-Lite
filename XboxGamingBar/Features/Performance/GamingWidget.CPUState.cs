using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        // [2.0 rebuild - Faza C-CPU] Set while a helper push is being reflected into the (headless,
        // unbound) Max/MinCPUStateComboBox. Guards the SelectionChanged handlers below so the
        // programmatic ComboBox update doesn't echo straight back to the helper as a "user edit"
        // (MaxCPUStateProperty/MinCPUStateProperty's OnValueSyncedFromHelper dispatch queues this
        // update via Dispatcher.RunAsync, which can run after isApplyingHelperUpdate has already
        // been reset - unlike the synchronous pipe-push path, so that flag alone isn't sufficient).
        private bool isApplyingCPUStateFromHelper = false;

        /// <summary>
        /// Reflects a helper-pushed Max/Min CPU State value into the ComboBox. Called from
        /// MaxCPUStateProperty/MinCPUStateProperty's OnValueSyncedFromHelper.
        /// </summary>
        internal void ApplyCPUStateFromHelper(bool isMax, int value)
        {
            if (MinCPUStateComboBox == null || MaxCPUStateComboBox == null) return;

            isApplyingCPUStateFromHelper = true;
            try
            {
                SetCPUStateComboBoxValue(isMax ? MaxCPUStateComboBox : MinCPUStateComboBox, value);
                if (isMax)
                {
                    // allowAutoDisable:false - never stomp the just-pushed CPUBoost value or push
                    // to the helper from here, same reasoning as the profile-load call site (#88 bug #4).
                    UpdateCPUBoostEnabledState(allowAutoDisable: false);
                }
            }
            finally
            {
                isApplyingCPUStateFromHelper = false;
            }
        }

        /// <summary>
        /// Initializes CPU State comboboxes with percentage values (5%, 10%, 15%... 100%)
        /// </summary>
        private void InitializeCPUStateComboBoxes()
        {
            MinCPUStateComboBox.Items.Clear();
            MaxCPUStateComboBox.Items.Clear();

            // Add values from 5 to 100 in 5% increments
            for (int i = 5; i <= 100; i += 5)
            {
                MinCPUStateComboBox.Items.Add(new ComboBoxItem { Content = $"{i}%", Tag = i });
                MaxCPUStateComboBox.Items.Add(new ComboBoxItem { Content = $"{i}%", Tag = i });
            }

            // Set defaults: Min=5%, Max=100%
            MinCPUStateComboBox.SelectedIndex = 0; // 5%
            MaxCPUStateComboBox.SelectedIndex = 19; // 100%

            // Enable the comboboxes
            MinCPUStateComboBox.IsEnabled = true;
            MaxCPUStateComboBox.IsEnabled = true;
        }

        /// <summary>
        /// Gets the CPU state percentage value from a ComboBox selection
        /// </summary>
        private int GetSelectedCPUStateValue(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is int value)
            {
                return value;
            }
            // Default values: 100 for max, 5 for min
            return comboBox == MaxCPUStateComboBox ? 100 : 5;
        }

        /// <summary>
        /// Sets the CPU State ComboBox to the specified percentage value
        /// </summary>
        private void SetCPUStateComboBoxValue(ComboBox comboBox, int value)
        {
            // Clamp to valid range and round to nearest 5
            value = Math.Max(5, Math.Min(100, value));
            value = (int)(Math.Round(value / 5.0) * 5);
            if (value < 5) value = 5;

            // Find and select the matching item
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is int itemValue && itemValue == value)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Handler for Min CPU State ComboBox selection change
        /// </summary>
        private void MinCPUStateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (MinCPUStateComboBox == null || MaxCPUStateComboBox == null)
                return;

            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync || isApplyingCPUStateFromHelper)
                return;

            int minValue = GetSelectedCPUStateValue(MinCPUStateComboBox);
            int maxValue = GetSelectedCPUStateValue(MaxCPUStateComboBox);

            // Ensure min doesn't exceed max
            if (minValue > maxValue)
            {
                isApplyingCPUStateFromHelper = true;
                try { SetCPUStateComboBoxValue(MaxCPUStateComboBox, minValue); }
                finally { isApplyingCPUStateFromHelper = false; }
            }

            Logger.Info($"Min CPU State changed to {minValue}%");
        }

        /// <summary>
        /// Handler for Max CPU State ComboBox selection change
        /// </summary>
        private void MaxCPUStateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (MinCPUStateComboBox == null || MaxCPUStateComboBox == null)
                return;

            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync || isApplyingCPUStateFromHelper)
                return;

            int minValue = GetSelectedCPUStateValue(MinCPUStateComboBox);
            int maxValue = GetSelectedCPUStateValue(MaxCPUStateComboBox);

            // Ensure max doesn't go below min
            if (maxValue < minValue)
            {
                isApplyingCPUStateFromHelper = true;
                try { SetCPUStateComboBoxValue(MinCPUStateComboBox, maxValue); }
                finally { isApplyingCPUStateFromHelper = false; }
            }

            // Update CPU Boost toggle enabled state
            UpdateCPUBoostEnabledState();

            Logger.Info($"Max CPU State changed to {maxValue}%");
        }

        /// <summary>
        /// Updates the CPU Boost toggle enabled state based on Max CPU State.
        /// When Max CPU State is below 100%, CPU Boost cannot work (Windows prevents boosting beyond the limit).
        /// </summary>
        /// <param name="allowAutoDisable">
        /// When true (genuine user change of Max CPU State), force CPU Boost off and notify the
        /// helper if boost is no longer possible. When false (profile load/switch), only update the
        /// greyed-out state — never stomp the just-loaded CPUBoost value nor push to the helper.
        /// During a profile load the combo can momentarily read a stale value (e.g. 95% before the
        /// profile's 100% settles), which previously force-disabled boost and corrupted the
        /// profile by pushing false to the helper. (Issue #88 bug #4)
        /// </param>
        private void UpdateCPUBoostEnabledState(bool allowAutoDisable = true)
        {
            if (CPUBoostToggle == null || MaxCPUStateComboBox == null) return;

            int maxCPUStateValue = GetSelectedCPUStateValue(MaxCPUStateComboBox);
            bool canBoost = maxCPUStateValue >= 100;

            CPUBoostToggle.IsEnabled = canBoost;

            // CPU Boost is helper-owned. A Max CPU State intent below 100% is validated as an
            // atomic profile change by the helper, which clears Boost there and pushes the
            // confirmed value back. The widget must not independently rewrite it.
        }

        /// <summary>
        /// Gets a short string representation of enabled AMD features for display in profile cards
        /// </summary>
        private static string GetAMDFeaturesShortString(PerformanceProfile profile)
        {
            var features = new List<string>();

            if (profile.FluidMotionFrames) features.Add("AFMF");
            if (profile.RadeonSuperResolution) features.Add("RSR");
            if (profile.ImageSharpening) features.Add("RIS");
            if (profile.RadeonAntiLag) features.Add("AL");
            if (profile.RadeonBoost) features.Add("Boost");
            if (profile.RadeonChill) features.Add("Chill");

            return string.Join(",", features);
        }

    }
}
