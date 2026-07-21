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
using XboxGamingBar.IPC;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        // Lossless Scaling Helper Methods

        // Tracks whether the CURRENT Scale-tab setting values differ from a baseline snapshot
        // (what's actually applied - the last successful Apply-and-Restart, or the freshly
        // (re)loaded profile). Pressing "Scale" only sends LS its own toggle hotkey - it never
        // applies GoTweaks' combobox/slider values, those only reach Settings.xml via "Apply
        // and Restart" - so while diverged from baseline, "Scale" would silently act on stale
        // pre-change settings. Gate the two buttons on this instead: Scale enabled only when
        // matching baseline, Apply-and-Restart enabled (and highlighted) only when diverged.
        //
        // This is a real diff against baseline, not a one-way "something changed" latch: if the
        // user changes a setting and then changes it back to its original value, the two values
        // compare equal again and Scale re-enables - it doesn't stay stuck disabled just because
        // a control was touched at some point.
        private readonly Dictionary<Function, object> losslessScalingBaseline = new Dictionary<Function, object>();
        private bool losslessScalingBaselineCaptured = false;
        private bool losslessScalingHasPendingChanges = false;
        private Brush losslessScalingSaveButtonDefaultBackground;
        private Brush losslessScalingSaveButtonDefaultForeground;

        /// <summary>
        /// Every Scale-tab setting that "Apply and Restart" persists to Settings.xml - see
        /// LosslessScalingManager.WriteSettingsToProfile/ReadSettingsFromProfile for the
        /// authoritative list this must stay in sync with.
        /// </summary>
        private IEnumerable<FunctionalProperty> LosslessScalingTrackedProperties()
        {
            yield return losslessScalingScalingType;
            yield return losslessScalingSharpness;
            yield return losslessScalingFSROptimize;
            yield return losslessScalingAnime4KSize;
            yield return losslessScalingAnime4KVRS;
            yield return losslessScalingScaleMode;
            yield return losslessScalingScaleFactor;
            yield return losslessScalingAspectRatio;
            yield return losslessScalingFrameGenType;
            yield return losslessScalingLSFG3Mode;
            yield return losslessScalingLSFG3Multiplier;
            yield return losslessScalingLSFG3Target;
            yield return losslessScalingLSFG2Mode;
            yield return losslessScalingFlowScale;
            yield return losslessScalingSize;
            yield return losslessScalingAutoScale;
            yield return losslessScalingAutoScaleDelay;
            yield return losslessScalingSyncMode;
            yield return losslessScalingCaptureApi;
            yield return losslessScalingDrawFps;
            yield return losslessScalingHdrSupport;
            yield return losslessScalingGsyncSupport;
            yield return losslessScalingResizeBeforeScaling;
            yield return losslessScalingLS1Type;
            yield return losslessScalingMaxFrameLatency;
            yield return losslessScalingLS1Sharpness;
        }

        /// <summary>
        /// Snapshots every tracked setting's current value as the new "applied" baseline, then
        /// recomputes the dirty state against it (normally comes out clean, since this is called
        /// right after the values it's snapshotting were just applied or freshly loaded).
        /// </summary>
        private void RecaptureLosslessScalingBaseline()
        {
            losslessScalingBaseline.Clear();
            foreach (var prop in LosslessScalingTrackedProperties())
            {
                if (prop != null)
                {
                    losslessScalingBaseline[prop.Function] = prop.GetValue();
                }
            }
            Logger.Info($"Lossless Scaling: captured baseline snapshot of {losslessScalingBaseline.Count} settings");
            RecomputeLosslessScalingDirtyState();
        }

        /// <summary>
        /// Called by every Scale-tab setting property (ComboBox/Slider/ToggleSwitch) from its
        /// own "the user really changed this" branch - see e.g.
        /// LosslessScalingScalingTypeProperty.ComboBox_SelectionChanged. Never called for
        /// helper-driven syncs (BatchGet, profile load), since those update the control to a
        /// value that already equals Value, so the "did it actually change" checks in each
        /// property's own handler skip calling this.
        /// </summary>
        internal void MarkLosslessScalingSettingsDirty()
        {
            RecomputeLosslessScalingDirtyState();
        }

        /// <summary>
        /// Compares every tracked property's current value against the captured baseline and
        /// updates losslessScalingHasPendingChanges accordingly (true the moment ANY value
        /// differs, false again once every value matches baseline - including "changed and
        /// changed back").
        /// </summary>
        private void RecomputeLosslessScalingDirtyState()
        {
            bool diverged = false;
            if (losslessScalingBaseline.Count > 0)
            {
                foreach (var prop in LosslessScalingTrackedProperties())
                {
                    if (prop == null) continue;
                    if (!losslessScalingBaseline.TryGetValue(prop.Function, out var baseValue) || !Equals(baseValue, prop.GetValue()))
                    {
                        diverged = true;
                        break;
                    }
                }
            }
            // No baseline captured yet (shouldn't normally happen once LS is installed and the
            // first status update has run) - treat as clean rather than permanently disabling
            // Scale until a baseline shows up.

            if (losslessScalingHasPendingChanges != diverged)
            {
                losslessScalingHasPendingChanges = diverged;
                Logger.Info($"Lossless Scaling: pending-changes state now {diverged}");
            }
            UpdateLosslessScalingDirtyState();
        }

        /// <summary>
        /// Applies the dirty/clean state to the Scale and Apply-and-Restart buttons. Safe to
        /// call before the controls are loaded (no-ops via null checks) and off the UI thread
        /// is NOT required here - callers are already on the UI thread (button click handlers,
        /// property change handlers dispatched via Owner.Dispatcher).
        /// </summary>
        private void UpdateLosslessScalingDirtyState()
        {
            if (LosslessScalingSaveSettingsButton == null || LosslessScalingEnabledToggle == null)
            {
                return;
            }

            bool isInstalled = losslessScalingInstalled?.Value ?? false;
            bool isRunning = losslessScalingRunning?.Value ?? false;

            LosslessScalingEnabledToggle.IsEnabled = isInstalled && !losslessScalingHasPendingChanges;

            bool enableSaveButton = isInstalled && isRunning && losslessScalingHasPendingChanges;
            LosslessScalingSaveSettingsButton.IsEnabled = enableSaveButton;

            // Capture the style's own defaults once, before ever overriding them, so "not
            // dirty" can restore exactly what LosslessScalingApplyButtonStyle would have
            // applied (its Disabled VisualState also independently resets both when the
            // button transitions to IsEnabled=False, but this keeps .Background/.Foreground
            // correct even before that visual-state transition runs).
            if (losslessScalingSaveButtonDefaultBackground == null)
            {
                losslessScalingSaveButtonDefaultBackground = LosslessScalingSaveSettingsButton.Background;
            }
            if (losslessScalingSaveButtonDefaultForeground == null)
            {
                losslessScalingSaveButtonDefaultForeground = LosslessScalingSaveSettingsButton.Foreground;
            }
            LosslessScalingSaveSettingsButton.Background = losslessScalingHasPendingChanges
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6C, 0xCB, 0x5F)) // matches the Quick Settings tile green
                : losslessScalingSaveButtonDefaultBackground;
            LosslessScalingSaveSettingsButton.Foreground = losslessScalingHasPendingChanges
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1A, 0x1A, 0x1A)) // matches Scale's dark-on-accent text
                : losslessScalingSaveButtonDefaultForeground;
        }

        private async void UpdateLosslessScalingStatus()
        {
            try
            {
                bool isInstalled = losslessScalingInstalled?.Value ?? false;
                bool isRunning = losslessScalingRunning?.Value ?? false;

                Logger.Info($"UpdateLosslessScalingStatus called. Installed: {isInstalled}, Running: {isRunning}");

                // Marshal UI updates to the dispatcher thread
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        // Check if UI elements exist (may not be loaded yet)
                        if (LosslessScalingStatusText == null || LaunchLosslessScalingButton == null || ShowLosslessScalingWindowButton == null)
                        {
                            Logger.Warn("LosslessScaling UI elements not loaded yet, skipping status update");
                            return;
                        }

                        // Enable controls only when LS is installed
                        bool enableControls = isInstalled;

                        if (!isInstalled)
                        {
                            LosslessScalingStatusText.Text = "Not Installed";
                            LosslessScalingStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x6B, 0x6B));
                            LaunchLosslessScalingButton.Visibility = Visibility.Collapsed;
                            ShowLosslessScalingWindowButton.Visibility = Visibility.Collapsed;
                        }
                        else if (!isRunning)
                        {
                            LosslessScalingStatusText.Text = "Installed (Not Running)";
                            LosslessScalingStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0xA5, 0x00));
                            LaunchLosslessScalingButton.Visibility = Visibility.Visible;
                            ShowLosslessScalingWindowButton.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            LosslessScalingStatusText.Text = "Installed and Running";
                            LosslessScalingStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4C, 0xAF, 0x50));
                            LaunchLosslessScalingButton.Visibility = Visibility.Collapsed;
                            // "Show Window" is intentionally never surfaced: forcing Lossless
                            // Scaling's window via ShowWindow/SetForegroundWindow from outside
                            // the process desyncs its own WPF-UI visibility state (tray-hide
                            // relies on the app's own show/hide routine), producing a blank,
                            // unclosable window. There's no known safe way to un-hide it
                            // externally, so we don't offer the button at all.
                            ShowLosslessScalingWindowButton.Visibility = Visibility.Collapsed;
                        }

                        // Enable/disable all Lossless Scaling controls. Scale and Apply-and-Restart
                        // are handled separately by UpdateLosslessScalingDirtyState (called below),
                        // which additionally gates them on losslessScalingHasPendingChanges.
                        if (LosslessScalingAutoScaleToggle != null) LosslessScalingAutoScaleToggle.IsEnabled = enableControls;
                        if (LosslessScalingAutoScaleDelaySlider != null) LosslessScalingAutoScaleDelaySlider.IsEnabled = enableControls;
                        if (LosslessScalingScalingTypeComboBox != null) LosslessScalingScalingTypeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingFrameGenTypeComboBox != null) LosslessScalingFrameGenTypeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingLSFG3ModeComboBox != null) LosslessScalingLSFG3ModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingLSFG3MultiplierComboBox != null) LosslessScalingLSFG3MultiplierComboBox.IsEnabled = enableControls;
                        if (LosslessScalingLSFG3TargetSlider != null) LosslessScalingLSFG3TargetSlider.IsEnabled = enableControls;
                        if (LosslessScalingLSFG2ModeComboBox != null) LosslessScalingLSFG2ModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingFlowScaleSlider != null) LosslessScalingFlowScaleSlider.IsEnabled = enableControls;
                        if (LosslessScalingSizeToggle != null) LosslessScalingSizeToggle.IsEnabled = enableControls;
                        // Additional Settings.xml-backed controls (added 2026-05-01)
                        if (LosslessScalingSyncModeComboBox != null) LosslessScalingSyncModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingCaptureApiComboBox != null) LosslessScalingCaptureApiComboBox.IsEnabled = enableControls;
                        if (LosslessScalingDrawFpsToggle != null) LosslessScalingDrawFpsToggle.IsEnabled = enableControls;
                        if (LosslessScalingHdrSupportToggle != null) LosslessScalingHdrSupportToggle.IsEnabled = enableControls;
                        if (LosslessScalingGsyncSupportToggle != null) LosslessScalingGsyncSupportToggle.IsEnabled = enableControls;
                        if (LosslessScalingResizeBeforeToggle != null) LosslessScalingResizeBeforeToggle.IsEnabled = enableControls;
                        if (LosslessScalingLS1TypeComboBox != null) LosslessScalingLS1TypeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingMaxFrameLatencySlider != null) LosslessScalingMaxFrameLatencySlider.IsEnabled = enableControls;
                        if (LosslessScalingResetProfileButton != null) LosslessScalingResetProfileButton.IsEnabled = enableControls;
                        if (LosslessScalingSaveSettingsButton != null)
                        {
                            // .IsEnabled and .Background are owned by UpdateLosslessScalingDirtyState
                            // (called below) - only compute the XY-navigation bool here.
                            bool enableSaveButton = isInstalled && isRunning && losslessScalingHasPendingChanges;
                            LosslessScalingEnabledToggle.XYFocusDown = enableSaveButton ? LosslessScalingSaveSettingsButton : (DependencyObject)LosslessScalingAutoScaleToggle;
                            LosslessScalingAutoScaleToggle.XYFocusUp = enableSaveButton ? LosslessScalingSaveSettingsButton : (DependencyObject)LosslessScalingEnabledToggle;
                        }
                        if (LosslessScalingCreateProfileButton != null)
                        {
                            bool enableCreateProfile = enableControls && HasValidGame(currentGameName);
                            LosslessScalingCreateProfileButton.IsEnabled = enableCreateProfile;

                            // Update XY navigation for Scale toggle based on Create Profile button state
                            // When Create Profile is disabled, Scale should go up to Launch, or to
                            // nav when neither Launch nor Create Profile is visible (LS running -
                            // "Show Window" was removed and is never shown, see UpdateLosslessScalingStatus).
                            if (isRunning)
                            {
                                LosslessScalingEnabledToggle.XYFocusUp = enableCreateProfile ? LosslessScalingCreateProfileButton : (DependencyObject)ScalingNavItem;
                            }
                            else if (isInstalled)
                            {
                                // Launch is visible
                                LosslessScalingEnabledToggle.XYFocusUp = enableCreateProfile ? LosslessScalingCreateProfileButton : (DependencyObject)LaunchLosslessScalingButton;
                            }
                            else
                            {
                                // Neither button visible, go to nav
                                LosslessScalingEnabledToggle.XYFocusUp = ScalingNavItem;
                            }
                        }

                        // New Scaling Algorithm controls
                        if (LosslessScalingSharpnessSlider != null) LosslessScalingSharpnessSlider.IsEnabled = enableControls;
                        if (LosslessScalingFSROptimizeToggle != null) LosslessScalingFSROptimizeToggle.IsEnabled = enableControls;
                        if (LosslessScalingAnime4KSizeComboBox != null) LosslessScalingAnime4KSizeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingAnime4KVRSToggle != null) LosslessScalingAnime4KVRSToggle.IsEnabled = enableControls;
                        if (LosslessScalingScaleModeComboBox != null) LosslessScalingScaleModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingScaleFactorSlider != null) LosslessScalingScaleFactorSlider.IsEnabled = enableControls;
                        if (LosslessScalingAspectRatioComboBox != null) LosslessScalingAspectRatioComboBox.IsEnabled = enableControls;

                        // One-time initial baseline capture. Normally LosslessScalingCurrentProfile_
                        // PropertyChanged already captures it on first connect, but that property's
                        // own sync can no-op if the real value happens to equal its widget-side
                        // constructor default ("Default") - this is a safety net so Scale never gets
                        // stuck permanently disabled from an empty baseline in that edge case.
                        if (!losslessScalingBaselineCaptured && isInstalled)
                        {
                            losslessScalingBaselineCaptured = true;
                            RecaptureLosslessScalingBaseline();
                        }
                        else
                        {
                            UpdateLosslessScalingDirtyState();
                        }

                        Logger.Info("LosslessScaling status UI updated successfully");
                    }
                    catch (Exception innerEx)
                    {
                        Logger.Error($"Error updating LosslessScaling status UI: {innerEx.Message}");
                        Logger.Error($"Stack trace: {innerEx.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateLosslessScalingStatus: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task<bool> ExecuteLosslessScalingActionAsync(string action, string payload = null)
        {
            if (!App.IsConnected)
            {
                await ShowSettingApplyFailureAsync(Function.None, "Lossless Scaling: helper disconnected.");
                return false;
            }

            try
            {
                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "ExecuteLosslessScalingAction", action }
                };
                if (!string.IsNullOrEmpty(payload)) request.Add("Payload", payload);

                var response = await App.SendMessageAsync(request);
                bool success = response != null
                    && response.TryGetValue("Success", out object successValue)
                    && successValue is bool applied
                    && applied;
                if (!success)
                {
                    string reason = response != null
                        && response.TryGetValue("Error", out object errorValue)
                        ? errorValue as string
                        : null;
                    await ShowSettingApplyFailureAsync(Function.None,
                        $"Lossless Scaling: {reason ?? "the helper rejected the action."}");
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Lossless Scaling action '{action}' failed: {ex.Message}");
                await ShowSettingApplyFailureAsync(Function.None, $"Lossless Scaling: {ex.Message}");
                return false;
            }
        }

        private async void LaunchLosslessScalingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Launch Lossless Scaling button clicked");
                LaunchLosslessScalingButton.Content = "Launching...";
                LaunchLosslessScalingButton.IsEnabled = false;

                await ExecuteLosslessScalingActionAsync("Launch");
                UpdateLosslessScalingStatus();
                LaunchLosslessScalingButton.Content = "Launch";
                LaunchLosslessScalingButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Lossless Scaling: {ex.Message}");
                LaunchLosslessScalingButton.Content = "Launch";
                LaunchLosslessScalingButton.IsEnabled = true;
            }
        }

        private async void ShowLosslessScalingWindowButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Show Lossless Scaling Window button clicked");
                await ExecuteLosslessScalingActionAsync("BringToForeground");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error showing Lossless Scaling window: {ex.Message}");
            }
        }

        private void LosslessScalingStatus_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update status when installed/running state changes
            UpdateLosslessScalingStatus();
        }

        private void RunningGame_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (runningGame?.Value != null && runningGame.Value.IsValid())
                {
                    string exePath = runningGame.Value.GameId.Path;
                    string iconPath = runningGame.Value.GameId.IconPath;

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // Check if this is the same game (preserve cached icon path if new one is empty)
                        bool isSameGame = exePath.Equals(currentGameExePath, StringComparison.OrdinalIgnoreCase);

                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            // New icon path provided - cache it
                            currentGameIconPath = iconPath;
                        }
                        else if (isSameGame && !string.IsNullOrEmpty(currentGameIconPath))
                        {
                            // Same game but no icon path in update - use cached path
                            iconPath = currentGameIconPath;
                            Logger.Info($"Using cached icon path for {exePath}");
                        }

                        currentGameExePath = exePath;
                        Logger.Info($"Updated currentGameExePath: {currentGameExePath}");

                        // Load the game icon for the Profiles tab
                        // Use helper-extracted icon if available, otherwise fall back to Steam lookup
                        LoadCurrentGameIcon(exePath, iconPath);
                    }
                    else
                    {
                        currentGameExePath = "";
                        currentGameIconPath = "";
                        Logger.Info("Cleared currentGameExePath (no path in RunningGame)");

                        // Clear the game icon
                        LoadCurrentGameIcon(null, null);
                    }
                }
                else
                {
                    currentGameExePath = "";
                    currentGameIconPath = "";
                    Logger.Info("Cleared currentGameExePath (no running game)");

                    // Clear the game icon
                    LoadCurrentGameIcon(null, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in RunningGame_PropertyChanged: {ex.Message}");
            }
        }

        private void LosslessScalingFrameGenTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedType = LosslessScalingFrameGenTypeComboBox.SelectedItem as string ?? "Off";
                bool showLSFG3 = selectedType == "LSFG3";
                bool showLSFG2 = selectedType == "LSFG2";

                // Show/hide LSFG3 settings card
                if (LSFG3SettingsCard != null)
                {
                    LSFG3SettingsCard.Visibility = showLSFG3 ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide LSFG2 settings card
                if (LSFG2SettingsCard != null)
                {
                    LSFG2SettingsCard.Visibility = showLSFG2 ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update XY navigation based on visible controls
                if (showLSFG3)
                {
                    // LSFG3: FrameGen -> LSFG3 Mode
                    LosslessScalingFrameGenTypeComboBox.XYFocusDown = LosslessScalingLSFG3ModeComboBox;
                }
                else if (showLSFG2)
                {
                    // LSFG2: FrameGen -> LSFG2 Mode
                    LosslessScalingFrameGenTypeComboBox.XYFocusDown = LosslessScalingLSFG2ModeComboBox;
                }
                else
                {
                    // No extra controls - remove XYFocusDown (end of list)
                    LosslessScalingFrameGenTypeComboBox.XYFocusDown = null;
                }

                UpdateLosslessScalingConflictWarning();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingFrameGenTypeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void LosslessScalingScalingTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedType = LosslessScalingScalingTypeComboBox.SelectedItem as string ?? "Off";

                // Show/hide Sharpness panel (for FSR, NIS, SGSR, BCAS - LS1 has its own
                // dedicated Sharpness field/panel, see showLS1Sharpness below)
                bool showSharpness = selectedType == "FSR" || selectedType == "NIS" || selectedType == "SGSR" || selectedType == "BCAS";
                bool showFSROptimize = selectedType == "FSR";
                bool showAnime4K = selectedType == "Anime4K";
                bool showLS1Type = selectedType == "LS1";
                bool showLS1Sharpness = selectedType == "LS1";
                bool showResizeBefore = selectedType != "Off";

                if (LosslessScalingSharpnessPanel != null)
                {
                    LosslessScalingSharpnessPanel.Visibility = showSharpness ? Visibility.Visible : Visibility.Collapsed;
                }

                if (LosslessScalingLS1SharpnessPanel != null)
                {
                    LosslessScalingLS1SharpnessPanel.Visibility = showLS1Sharpness ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide FSR Optimize panel (FSR only)
                if (LosslessScalingFSROptimizePanel != null)
                {
                    LosslessScalingFSROptimizePanel.Visibility = showFSROptimize ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Anime4K panel
                if (LosslessScalingAnime4KPanel != null)
                {
                    LosslessScalingAnime4KPanel.Visibility = showAnime4K ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide LS1 Type panel (LS1 only)
                if (LosslessScalingLS1TypePanel != null)
                {
                    LosslessScalingLS1TypePanel.Visibility = showLS1Type ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Resize Before Scale panel (any type other than Off)
                if (LosslessScalingResizeBeforePanel != null)
                {
                    LosslessScalingResizeBeforePanel.Visibility = showResizeBefore ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update XY navigation based on visible controls
                // ScalingTypeComboBox down: Sharpness -> FSROptimize -> Anime4K -> ScaleMode
                if (showFSROptimize)
                {
                    // FSR: Type -> Sharpness -> FSROptimize -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingSharpnessSlider;
                    LosslessScalingSharpnessSlider.XYFocusDown = LosslessScalingFSROptimizeToggle;
                    LosslessScalingFSROptimizeToggle.XYFocusDown = LosslessScalingScaleModeComboBox;
                }
                else if (showSharpness)
                {
                    // NIS, SGSR, BCAS: Type -> Sharpness -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingSharpnessSlider;
                    LosslessScalingSharpnessSlider.XYFocusDown = LosslessScalingScaleModeComboBox;
                }
                else if (showAnime4K)
                {
                    // Anime4K: Type -> Size -> VRS -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingAnime4KSizeComboBox;
                }
                else
                {
                    // No extra controls: Type -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingScaleModeComboBox;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingScalingTypeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void LosslessScalingScaleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedMode = LosslessScalingScaleModeComboBox.SelectedItem as string ?? "Auto";
                bool showAuto = selectedMode == "Auto";
                bool showCustom = selectedMode == "Custom";

                // Show/hide Auto mode panel
                if (LosslessScalingAutoModePanel != null)
                {
                    LosslessScalingAutoModePanel.Visibility = showAuto ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Custom mode panel
                if (LosslessScalingCustomModePanel != null)
                {
                    LosslessScalingCustomModePanel.Visibility = showCustom ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update XY navigation based on visible controls
                if (showAuto)
                {
                    // Auto: ScaleMode -> AspectRatio -> FrameGen
                    LosslessScalingScaleModeComboBox.XYFocusDown = LosslessScalingAspectRatioComboBox;
                    LosslessScalingAspectRatioComboBox.XYFocusDown = LosslessScalingFrameGenTypeComboBox;
                }
                else if (showCustom)
                {
                    // Custom: ScaleMode -> ScaleFactor -> FrameGen
                    LosslessScalingScaleModeComboBox.XYFocusDown = LosslessScalingScaleFactorSlider;
                    LosslessScalingScaleFactorSlider.XYFocusDown = LosslessScalingFrameGenTypeComboBox;
                }
                else
                {
                    // No extra controls: ScaleMode -> FrameGen
                    LosslessScalingScaleModeComboBox.XYFocusDown = LosslessScalingFrameGenTypeComboBox;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingScaleModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void LosslessScalingLSFG3ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedMode = LosslessScalingLSFG3ModeComboBox.SelectedItem as string ?? "FIXED";
                bool isAdaptive = selectedMode == "ADAPTIVE";

                // Hide multiplier when Adaptive mode is selected
                if (LosslessScalingLSFG3MultiplierPanel != null)
                {
                    LosslessScalingLSFG3MultiplierPanel.Visibility = isAdaptive ? Visibility.Collapsed : Visibility.Visible;
                }

                // Update XY navigation based on visible controls
                if (isAdaptive)
                {
                    // ADAPTIVE: Mode -> Target -> FlowScale -> SizeToggle (skip Multiplier)
                    LosslessScalingLSFG3ModeComboBox.XYFocusDown = LosslessScalingLSFG3TargetSlider;
                    LosslessScalingLSFG3TargetSlider.XYFocusUp = LosslessScalingLSFG3ModeComboBox;
                }
                else
                {
                    // FIXED: Mode -> Multiplier -> Target -> FlowScale -> SizeToggle
                    LosslessScalingLSFG3ModeComboBox.XYFocusDown = LosslessScalingLSFG3MultiplierComboBox;
                    LosslessScalingLSFG3TargetSlider.XYFocusUp = LosslessScalingLSFG3MultiplierComboBox;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingLSFG3ModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void AMDFluidMotionFrameToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateLosslessScalingConflictWarning();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in AMDFluidMotionFrameToggle_Toggled: {ex.Message}");
            }
        }

        private void UpdateLosslessScalingConflictWarning()
        {
            if (LSConflictWarningBorder == null || LSConflictWarningText == null) return;
            string selectedType = LosslessScalingFrameGenTypeComboBox?.SelectedItem as string ?? "Off";
            bool conflictVisible = selectedType != "Off" && (AMDFluidMotionFrameToggle?.IsOn ?? false);
            LSConflictWarningBorder.Visibility = conflictVisible ? Visibility.Visible : Visibility.Collapsed;
            if (conflictVisible)
                LSConflictWarningText.Text = "Resolving the conflict between Lossless Scaling Frame Generation and AMD Fluid Motion Frames...";
        }

        private void LosslessScalingCurrentProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (LosslessScalingCurrentProfileText != null && losslessScalingCurrentProfile != null)
                    {
                        LosslessScalingCurrentProfileText.Text = losslessScalingCurrentProfile.Value ?? "Default";
                    }

                    // A freshly (re)loaded profile's values ARE the new baseline - any pending
                    // divergence belonged to whatever profile was active a moment ago.
                    RecaptureLosslessScalingBaseline();
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingCurrentProfile_PropertyChanged: {ex.Message}");
            }
        }

        private async void LosslessScalingCreateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(currentGameName))
                {
                    // Format: "GameName<||>WindowFilter" - use game name as window title filter for Lossless Scaling profile matching
                    string profileData = $"{currentGameName}<||>{currentGameName}";
                    if (await ExecuteLosslessScalingActionAsync("CreateProfile", profileData))
                        Logger.Info($"Created Lossless Scaling profile for: {currentGameName}");
                }
                else
                {
                    Logger.Warn("Cannot create profile - no game detected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingCreateProfileButton_Click: {ex.Message}");
            }
        }

        private async void LosslessScalingSaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (await ExecuteLosslessScalingActionAsync("SaveAndRestart"))
                {
                    Logger.Info("Saved Lossless Scaling settings and restarted");
                    RecaptureLosslessScalingBaseline();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingSaveSettingsButton_Click: {ex.Message}");
            }
        }

        // Resets the active LS profile's properties to LS-default values. The
        // helper updates its in-memory state and pipes the new values back; the
        // user still needs Apply-and-Restart to persist the reset to Settings.xml.
        // That keeps Reset undoable until they explicitly commit.
        private async void LosslessScalingResetProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!await ExecuteLosslessScalingActionAsync("ResetProfile")) return;
                Logger.Info("Reset Lossless Scaling profile to defaults");

                // The helper pushes the new (default) values back via the normal per-property
                // pipe sync, which doesn't have a single clean completion signal to await here -
                // give it a moment, then re-diff against baseline with the (by then current)
                // values. If the defaults happen to exactly match what's already applied, this
                // correctly re-enables Scale instead of leaving it stuck disabled - the helper
                // only updates its in-memory state here, Settings.xml isn't touched until
                // Apply-and-Restart.
                await Task.Delay(100);
                RecomputeLosslessScalingDirtyState();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingResetProfileButton_Click: {ex.Message}");
            }
        }

    }
}
