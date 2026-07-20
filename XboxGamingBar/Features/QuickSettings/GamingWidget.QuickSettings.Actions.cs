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

        /// <summary>
        /// Relay a direct tile intent. State-dependent cycling and action execution live in
        /// the helper, so controller combos work even while the widget is suspended.
        /// </summary>
        private void QuickSettingsTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tileTag)
                _ = SendQuickSettingActionAsync(tileTag);
        }

        private async Task SendQuickSettingActionAsync(string tileId)
        {
            if (!App.IsConnected)
            {
                await ShowSettingApplyFailureAsync(Function.None, $"Quick setting {tileId}: helper disconnected.");
                return;
            }

            try
            {
                var request = new Windows.Foundation.Collections.ValueSet { { "ExecuteQuickSetting", tileId } };
                var response = await App.SendMessageAsync(request) as Windows.Foundation.Collections.ValueSet;
                bool success = response != null && response.TryGetValue("Success", out object result)
                    && Convert.ToBoolean(result);
                if (!success)
                {
                    string reason = response != null && response.TryGetValue("Reason", out object detail)
                        ? detail?.ToString() : "helper did not confirm the action";
                    await ShowSettingApplyFailureAsync(Function.None, $"Quick setting {tileId}: {reason}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Quick setting {tileId} request failed: {ex.Message}");
                await ShowSettingApplyFailureAsync(Function.None, $"Quick setting {tileId}: {ex.Message}");
            }
        }

        /// <summary>
        /// FPS Limit toggle changed - set FPS limit to slider value or 0 (off)
        /// </summary>
        private void FPSLimitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Update display text when toggle is enabled
            if (FPSLimitToggle.IsOn && FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)FPSLimitSlider.Value} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                // Get max refresh rate and update slider
                int maxRefresh = 60;
                if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                {
                    maxRefresh = refreshRates.Value.Max();
                }

                // If slider is at minimum (15) or below, set to max refresh as default
                int limit = (int)FPSLimitSlider.Value;
                isApplyingHelperUpdate = true;
                try
                {
                    FPSLimitSlider.Maximum = maxRefresh;
                    if (limit <= 15)
                    {
                        limit = maxRefresh;
                        FPSLimitSlider.Value = limit;
                    }
                }
                finally { isApplyingHelperUpdate = false; }

                // Update display text with the final value
                if (FPSLimitValue != null)
                {
                    FPSLimitValue.Text = $"{limit} FPS";
                }

                _ = SendProfileFieldIntentAsync("FPSLimit", limit);
                Logger.Info($"FPS Limit enabled: {limit}");
            }
            else
            {
                // Disable FPS limit (0 = unlimited)
                _ = SendProfileFieldIntentAsync("FPSLimit", 0);
                Logger.Info("FPS Limit disabled");
            }

        }

        /// <summary>
        /// RSR toggle changed - disable RIS if RSR is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonSuperResolutionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (App.IsConnected)
            {
                // Mutual exclusion is resolved and confirmed by the helper profile intent.
                SettingChanged(sender, e);
                return;
            }
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDRadeonSuperResolutionToggle.IsOn && AMDImageSharpeningToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RSR enabled - disabling RIS (mutually exclusive)");
                AMDImageSharpeningToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// RIS toggle changed - disable RSR if RIS is enabled (mutually exclusive)
        /// </summary>
        private void AMDImageSharpeningToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (App.IsConnected)
            {
                SettingChanged(sender, e);
                return;
            }
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDImageSharpeningToggle.IsOn && AMDRadeonSuperResolutionToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RIS enabled - disabling RSR (mutually exclusive)");
                AMDRadeonSuperResolutionToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// AFMF toggle changed - Anti-Lag is mandatory whenever AFMF is on (AMDManager.cs's own
        /// AmdFluidMotionFrameEnabled handler force-enables it, a real AMD driver requirement, not
        /// a free user choice in that state). Grey out the Anti-Lag toggle so the user can't fight
        /// that - deliberately does NOT set Anti-Lag's IsOn itself; the bound property already
        /// reflects the helper's actual value (widget-pure-display-principle: adjust the control's
        /// enabled affordance, never independently force its value).
        /// </summary>
        private void AMDFluidMotionFrameToggle_ProfileToggled(object sender, RoutedEventArgs e)
        {
            UpdateAntiLagLockState();
            SettingChanged(sender, e);
        }

        private void UpdateAntiLagLockState()
        {
            if (AMDRadeonAntiLagToggle == null || AMDFluidMotionFrameToggle == null) return;
            AMDRadeonAntiLagToggle.IsEnabled = !AMDFluidMotionFrameToggle.IsOn;
        }

        /// <summary>
        /// Radeon Anti-Lag toggle changed - disable Chill if Anti-Lag is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonAntiLagToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (App.IsConnected)
            {
                SettingChanged(sender, e);
                return;
            }
            // Anti-Lag and Chill are mutually exclusive
            if (AMDRadeonAntiLagToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Anti-Lag enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Boost toggle changed - disable Chill if Boost is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (App.IsConnected)
            {
                SettingChanged(sender, e);
                return;
            }
            // Boost and Chill are mutually exclusive
            if (AMDRadeonBoostToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Boost enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Chill toggle changed - disable Anti-Lag and Boost if Chill is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonChillToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (App.IsConnected)
            {
                SettingChanged(sender, e);
                return;
            }
            // Chill is mutually exclusive with Anti-Lag and Boost
            if (AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                if (AMDRadeonAntiLagToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Anti-Lag (mutually exclusive)");
                    AMDRadeonAntiLagToggle.IsOn = false;
                }
                if (AMDRadeonBoostToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Boost (mutually exclusive)");
                    AMDRadeonBoostToggle.IsOn = false;
                }
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// FPS Limit slider changed - update FPS limit if toggle is on (with debouncing)
        /// </summary>
        private void FPSLimitSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Always update the display text
            if (FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)e.NewValue} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                int limit = (int)e.NewValue;
                fpsLimitPendingValue = limit;

                // Initialize debounce timer if needed
                if (fpsLimitDebounceTimer == null)
                {
                    fpsLimitDebounceTimer = new DispatcherTimer();
                    fpsLimitDebounceTimer.Interval = TimeSpan.FromMilliseconds(FPS_LIMIT_DEBOUNCE_MS);
                    fpsLimitDebounceTimer.Tick += FPSLimitDebounceTimer_Tick;
                }

                // Restart the debounce timer
                fpsLimitDebounceTimer.Stop();
                fpsLimitDebounceTimer.Start();
            }
        }

        /// <summary>
        /// Debounce timer tick - apply the pending FPS limit value
        /// </summary>
        private void FPSLimitDebounceTimer_Tick(object sender, object e)
        {
            fpsLimitDebounceTimer?.Stop();

            if (fpsLimit != null && FPSLimitToggle.IsOn)
            {
                _ = SendProfileFieldIntentAsync("FPSLimit", fpsLimitPendingValue);
                Logger.Info($"FPS Limit changed (debounced): {fpsLimitPendingValue}");
            }
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status and current fpsLimit value
        /// </summary>
        private void UpdateFPSLimitControls()
        {
            UpdateFPSLimitControls(rtssInstalled?.Value == true);
        }

        // [full-audit fix, 2026-07-20 — B8] Reflect a live helper-pushed FPSLimit value into the
        // Performance-tab toggle/slider. Called from FPSLimitProperty.OnValueSyncedFromHelper
        // (the property is headless, so a push otherwise never reaches these controls).
        // UpdateFPSLimitControls already dispatches to the UI thread and reads fpsLimit.Value.
        internal void ReflectFPSLimitFromHelper()
        {
            UpdateFPSLimitControls();
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status
        /// </summary>
        private void UpdateFPSLimitControls(bool rtssAvailable)
        {
            // Dispatch to UI thread since this may be called from property callback on non-UI thread
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    if (isUnloading) return;

                    // Guard against null controls during initialization or shutdown
                    if (FPSLimitToggle == null || FPSLimitSlider == null) return;

                    FPSLimitToggle.IsEnabled = rtssAvailable;

                    // Update slider maximum to current refresh rate
                    int maxRefresh = 60; // Default
                    if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                    {
                        maxRefresh = refreshRates.Value.Max();
                    }
                    FPSLimitSlider.Maximum = maxRefresh;

                    // Set tick frequency based on max refresh rate (show ~5-8 ticks)
                    int tickFreq;
                    if (maxRefresh >= 144)
                        tickFreq = 24;
                    else if (maxRefresh >= 120)
                        tickFreq = 20;
                    else if (maxRefresh >= 90)
                        tickFreq = 15;
                    else
                        tickFreq = 10;
                    FPSLimitSlider.TickFrequency = tickFreq;

                    // Sync toggle/slider with fpsLimit value
                    if (fpsLimit != null)
                    {
                        isApplyingHelperUpdate = true;
                        try
                        {
                            int limit = fpsLimit.Value;
                            if (limit > 0)
                            {
                                FPSLimitToggle.IsOn = true;
                                // Clamp value to slider range
                                FPSLimitSlider.Value = Math.Min(limit, maxRefresh);
                            }
                            else
                            {
                                FPSLimitToggle.IsOn = false;
                            }
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in UpdateFPSLimitControls: {ex.Message}");
                }
            });
        }
        /// <summary>
        /// Show/hide customization panel
        /// </summary>
        private void QuickSettingsCustomize_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Enter edit mode
                qsEditMode = true;
                qsSelectedTileForMove = null;

                QuickSettingsCustomizePanel.Visibility = Visibility.Visible;
                QuickSettingsCustomizeButton.Visibility = Visibility.Collapsed;

                // Register keyboard handler for B/Escape to deselect
                QuickSettingsCustomizePanel.KeyDown -= QuickSettingsCustomizePanel_KeyDown;
                QuickSettingsCustomizePanel.KeyDown += QuickSettingsCustomizePanel_KeyDown;

                // Update column button visuals
                UpdateColumnButtonVisuals();

                // Rebuild UIs with edit mode enabled
                BuildSortableGrid();
                RebuildQuickSettingsTiles();  // Shows hidden tiles with overlay in edit mode
            }
        }

        /// <summary>
        /// Handle keyboard input in customize panel (B/Escape to deselect)
        /// </summary>
        private void QuickSettingsCustomizePanel_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape ||
                e.Key == Windows.System.VirtualKey.GamepadB)
            {
                if (qsSelectedTileForMove != null)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGrid();
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Close customization panel
        /// </summary>
        private void QuickSettingsCustomizeDone_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Exit edit mode
                qsEditMode = false;
                qsSelectedTileForMove = null;
                UpdateSelectedTileIndicator(null);

                QuickSettingsCustomizePanel.Visibility = Visibility.Collapsed;
                QuickSettingsCustomizeButton.Visibility = Visibility.Visible;

                // Save config and rebuild tiles without edit overlays
                SaveQuickSettingsConfig();
                RebuildQuickSettingsTiles();
                UpdateQuickSettingsTileStates();
            }
        }

        /// <summary>
        /// Set column count to 3
        /// </summary>
        private void ColumnCount3_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 3)
            {
                qsColumnCount = 3;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 4
        /// </summary>
        private void ColumnCount4_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 4)
            {
                qsColumnCount = 4;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 5
        /// </summary>
        private void ColumnCount5_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 5)
            {
                qsColumnCount = 5;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Update column button visuals to show current selection
        /// </summary>
        private void UpdateColumnButtonVisuals()
        {
            if (Column3Button == null || Column4Button == null || Column5Button == null) return;

            var selectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180));
            var normalBrush = tileOffBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));

            Column3Button.Background = qsColumnCount == 3 ? selectedBrush : normalBrush;
            Column4Button.Background = qsColumnCount == 4 ? selectedBrush : normalBrush;
            Column5Button.Background = qsColumnCount == 5 ? selectedBrush : normalBrush;
        }

        /// <summary>
        /// Add a custom shortcut tile
        /// </summary>
        private async void AddCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            string name = CustomShortcutNameBox?.Text?.Trim();
            string shortcut = GetCustomShortcutKeysString();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(shortcut))
            {
                Logger.Warn("Custom shortcut name or shortcut is empty");
                return;
            }

            string id = Guid.NewGuid().ToString("N");
            if (!await SetCustomQuickSettingAsync(id, name, shortcut, delete: false)) return;

            // Clear inputs
            if (CustomShortcutNameBox != null) CustomShortcutNameBox.Text = "";
            _customShortcutKeys.Clear();
            UpdateCustomShortcutKeyTags();

            UpdateQuickSettingsTileStates();
        }

    }
}
