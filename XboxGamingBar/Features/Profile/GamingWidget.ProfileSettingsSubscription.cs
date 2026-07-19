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
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        private void SubscribeToSettingsChanges()
        {
            // Performance settings
            // (Master TDP slider removed — the Custom power-limit sliders self-save via their own
            //  ValueChanged handlers → OnCustomTDPSliderChanged → SaveCurrentSettingsToProfile.)
            CPUBoostToggle.Toggled += SettingChanged;
            CPUEPPSlider.ValueChanged += SettingChangedDebounced;
            MinCPUStateComboBox.SelectionChanged += SettingChanged;
            MaxCPUStateComboBox.SelectionChanged += SettingChanged;
            FPSLimitToggle.Toggled += FPSLimitToggle_Toggled;
            FPSLimitSlider.ValueChanged += FPSLimitSlider_ValueChanged;

            // Graphics settings (HDR and Resolution for profile feature)
            HDRToggle.Toggled += SettingChanged;
            ResolutionComboBox.SelectionChanged += SettingChanged;
            // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
            // 2026-07-19 (round 13): unlike Resolution/HDR (both wired to SettingChanged here AND
            // to ProfileTrackedProperty_ChangedResyncProfile for helper-pushed changes),
            // RefreshRate only ever had the latter - which calls SaveCurrentSettingsToProfile but
            // never SendPowerSourceProfileValuesToHelper(). Same failure shape as the already-fixed
            // Custom TDP sliders / FPS Limit bugs: a live refresh-rate change (via this combobox OR
            // the Quick Settings tile's CycleRefreshRate) saved locally but never resynced to the
            // helper's persisted AC/DC GameProfile, so it could be silently reverted by a stale
            // value on the next AC/DC transition or helper restart.
            RefreshRatesComboBox.SelectionChanged += SettingChanged;

            // AMD settings
            AMDFluidMotionFrameToggle.Toggled += AMDFluidMotionFrameToggle_ProfileToggled;
            AMDRadeonSuperResolutionToggle.Toggled += AMDRadeonSuperResolutionToggle_Toggled;
            AMDRadeonSuperResolutionSharpnessSlider.ValueChanged += SettingChangedDebounced;
            AMDImageSharpeningToggle.Toggled += AMDImageSharpeningToggle_Toggled;
            AMDImageSharpeningSlider.ValueChanged += SettingChangedDebounced;
            AMDRadeonAntiLagToggle.Toggled += AMDRadeonAntiLagToggle_Toggled;
            AMDRadeonBoostToggle.Toggled += AMDRadeonBoostToggle_Toggled;
            AMDRadeonBoostResolutionComboBox.SelectionChanged += SettingChanged;
            AMDRadeonChillToggle.Toggled += AMDRadeonChillToggle_Toggled;
            AMDRadeonChillMinFPSSlider.ValueChanged += SettingChangedDebounced;
            AMDRadeonChillMaxFPSSlider.ValueChanged += SettingChangedDebounced;

            // Legion controller button mapping settings
            InitializeButtonMappingEvents("Y1");
            InitializeButtonMappingEvents("Y2");
            InitializeButtonMappingEvents("Y3");
            InitializeButtonMappingEvents("M1");
            InitializeButtonMappingEvents("M2");
            InitializeButtonMappingEvents("M3");
            InitializeButtonMappingEvents("Desktop");
            InitializeButtonMappingEvents("Page");

            if (LegionNintendoLayoutToggle != null)
                LegionNintendoLayoutToggle.Toggled += LegionNintendoLayout_Toggled;
            if (LegionDesktopControlsToggle != null)
                LegionDesktopControlsToggle.Toggled += LegionDesktopControls_Toggled;
            if (LegionVibrationComboBox != null)
                LegionVibrationComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionVibrationModeComboBox != null)
                LegionVibrationModeComboBox.SelectionChanged += ControllerSettingChanged;

            // Gyro settings (per-game profile)
            if (LegionGyroTargetComboBox != null)
                LegionGyroTargetComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroSensitivityXSlider != null)
                LegionGyroSensitivityXSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionGyroSensitivityYSlider != null)
                LegionGyroSensitivityYSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionGyroInvertXToggle != null)
                LegionGyroInvertXToggle.Toggled += ControllerSettingChanged;
            if (LegionGyroInvertYToggle != null)
                LegionGyroInvertYToggle.Toggled += ControllerSettingChanged;
            if (LegionGyroMappingTypeComboBox != null)
                LegionGyroMappingTypeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroActivationModeComboBox != null)
                LegionGyroActivationModeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroActivationButtonComboBox != null)
                LegionGyroActivationButtonComboBox.SelectionChanged += ControllerSettingChanged;

            // Advanced gyro settings (per-game profile)
            if (LegionGyroDeadzoneSlider != null)
                LegionGyroDeadzoneSlider.ValueChanged += ControllerSliderSettingChanged;

            // Stick deadzones (per-game profile)
            if (LegionLeftStickDeadzoneSlider != null)
                LegionLeftStickDeadzoneSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionRightStickDeadzoneSlider != null)
                LegionRightStickDeadzoneSlider.ValueChanged += ControllerSliderSettingChanged;

            // Trigger travel (per-game profile)
            if (LegionLeftTriggerStartSlider != null)
                LegionLeftTriggerStartSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionLeftTriggerEndSlider != null)
                LegionLeftTriggerEndSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionRightTriggerStartSlider != null)
                LegionRightTriggerStartSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionRightTriggerEndSlider != null)
                LegionRightTriggerEndSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionHairTriggersToggle != null)
                LegionHairTriggersToggle.Toggled += LegionHairTriggers_Toggled;

            // Joystick as mouse (per-game profile)
            if (LegionJoystickAsMouseComboBox != null)
                LegionJoystickAsMouseComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionJoystickMouseSensSlider != null)
                LegionJoystickMouseSensSlider.ValueChanged += ControllerSliderSettingChanged;

            // Lighting settings (per-game profile)
            if (LegionPowerLightToggle != null)
                LegionPowerLightToggle.Toggled += ControllerSettingChanged;
            if (LegionLightModeComboBox != null)
                LegionLightModeComboBox.SelectionChanged += ControllerSettingChanged;
            // LegionColorPicker's save/send is triggered from LegionColorPicker_ColorChanged
            // (GamingWidget.LegionGo.cs), which also updates the preview swatch - no separate
            // subscription here (a second one used to double-fire ControllerSettingChanged on
            // every ColorChanged tick, which itself fires continuously while dragging).
            if (LegionBrightnessSlider != null)
                LegionBrightnessSlider.ValueChanged += ControllerSliderSettingChanged;
            if (LegionSpeedSlider != null)
                LegionSpeedSlider.ValueChanged += ControllerSliderSettingChanged;

            foreach (string buttonName in LegionRemapButtonNames)
            {
                UpdateButtonGamepadComboControls(buttonName);
            }
        }

        private void SettingChanged(object sender, object e)
        {
            // Don't save during profile loading, switching, initial sync, when helper is updating values,
            // or when any property is syncing from helper pipe
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync
                || WidgetSliderProperty.HelperSyncCount > 0 || refreshRate?.IsUpdatingUI == true
                || hdrEnabled?.IsUpdatingUI == true || resolution?.IsUpdatingUI == true)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate}, initialSync={isInitialSync})");
                return;
            }

            string group = GetPowerSourceProfileChangedGroup(sender);
            // First vertical 2.0 slice: these fields no longer write the widget-local
            // PerformanceProfile. The helper owns persistence and confirms the resulting state.
            if (group == "CPUBoost")
            {
                _ = SendProfileFieldIntentAsync("CPUBoost", CPUBoostToggle?.IsOn ?? false);
                return;
            }
            if (group == "CPUEPP")
            {
                _ = SendProfileFieldIntentAsync("CPUEPP", (int)(CPUEPPSlider?.Value ?? 80));
                return;
            }
            if (group == "CPUState")
            {
                _ = SendProfileFieldIntentAsync("CPUState", new[]
                {
                    GetSelectedCPUStateValue(MinCPUStateComboBox),
                    GetSelectedCPUStateValue(MaxCPUStateComboBox)
                });
                return;
            }
            if (group == "RefreshRate")
            {
                if (RefreshRatesComboBox?.SelectedItem is int selectedRefreshRate)
                    _ = SendProfileFieldIntentAsync("RefreshRate", selectedRefreshRate);
                return;
            }
            if (group == "HDR")
            {
                _ = SendProfileFieldIntentAsync("HDR", HDRToggle?.IsOn ?? false);
                return;
            }
            if (group == "Resolution")
            {
                if (ResolutionComboBox?.SelectedItem is string selectedResolution)
                    _ = SendProfileFieldIntentAsync("Resolution", selectedResolution);
                return;
            }
            if (TryGetAmdProfileFieldIntent(sender, out string amdField, out object amdValue))
            {
                _ = SendProfileFieldIntentAsync(amdField, amdValue);
                return;
            }

            // Functional profile changes must be explicit helper intents. A sender with no
            // mapped intent is intentionally ignored rather than captured into a local
            // whole-profile snapshot.
            Logger.Debug($"Ignoring unmigrated local profile save group: {group ?? "none"}");
        }

        private string GetPowerSourceProfileChangedGroup(object sender)
        {
            if (sender == CPUBoostToggle) return "CPUBoost";
            if (sender == CPUEPPSlider) return "CPUEPP";
            if (sender == MinCPUStateComboBox || sender == MaxCPUStateComboBox) return "CPUState";
            if (sender == HDRToggle) return "HDR";
            if (sender == ResolutionComboBox) return "Resolution";
            if (sender == RefreshRatesComboBox) return "RefreshRate";
            if (sender == AMDFluidMotionFrameToggle || sender == AMDRadeonSuperResolutionToggle ||
                sender == AMDRadeonSuperResolutionSharpnessSlider || sender == AMDImageSharpeningToggle ||
                sender == AMDImageSharpeningSlider || sender == AMDRadeonAntiLagToggle ||
                sender == AMDRadeonBoostToggle || sender == AMDRadeonBoostResolutionComboBox ||
                sender == AMDRadeonChillToggle || sender == AMDRadeonChillMinFPSSlider || sender == AMDRadeonChillMaxFPSSlider)
                return "AMD";
            return null;
        }

        private bool TryGetAmdProfileFieldIntent(object sender, out string field, out object value)
        {
            field = null;
            value = null;
            if (sender == AMDFluidMotionFrameToggle) { field = "FluidMotionFrames"; value = AMDFluidMotionFrameToggle.IsOn; }
            else if (sender == AMDRadeonSuperResolutionToggle) { field = "RadeonSuperResolution"; value = AMDRadeonSuperResolutionToggle.IsOn; }
            else if (sender == AMDRadeonSuperResolutionSharpnessSlider) { field = "RadeonSuperResolutionSharpness"; value = (int)AMDRadeonSuperResolutionSharpnessSlider.Value; }
            else if (sender == AMDImageSharpeningToggle) { field = "ImageSharpening"; value = AMDImageSharpeningToggle.IsOn; }
            else if (sender == AMDImageSharpeningSlider) { field = "ImageSharpeningSharpness"; value = (int)AMDImageSharpeningSlider.Value; }
            else if (sender == AMDRadeonAntiLagToggle) { field = "RadeonAntiLag"; value = AMDRadeonAntiLagToggle.IsOn; }
            else if (sender == AMDRadeonBoostToggle) { field = "RadeonBoost"; value = AMDRadeonBoostToggle.IsOn; }
            else if (sender == AMDRadeonBoostResolutionComboBox && amdRadeonBoostResolution != null) { field = "RadeonBoostResolution"; value = amdRadeonBoostResolution.Value; }
            else if (sender == AMDRadeonChillToggle) { field = "RadeonChill"; value = AMDRadeonChillToggle.IsOn; }
            else if (sender == AMDRadeonChillMinFPSSlider) { field = "RadeonChillMinFPS"; value = (int)AMDRadeonChillMinFPSSlider.Value; }
            else if (sender == AMDRadeonChillMaxFPSSlider) { field = "RadeonChillMaxFPS"; value = (int)AMDRadeonChillMaxFPSSlider.Value; }
            return field != null;
        }

        /// <summary>
        /// Debounced entry point for slider ValueChanged events (CPU EPP, AMD sharpness/
        /// resolution/FPS sliders). Raw ValueChanged fires continuously while the thumb moves,
        /// and SettingChanged does a synchronous full-profile write on the UI thread - without
        /// this, dragging one of these sliders end-to-end fires dozens of writes in under a
        /// second. Coalesces to a single SettingChanged call ~300ms after the drag settles,
        /// matching the FPSLimitSlider debounce pattern (GamingWidget.QuickSettings.Actions.cs).
        /// </summary>
        private void SettingChangedDebounced(object sender, object e)
        {
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync
                || WidgetSliderProperty.HelperSyncCount > 0)
            {
                return;
            }

            if (settingsSaveDebounceTimer == null)
            {
                settingsSaveDebounceTimer = new DispatcherTimer();
                settingsSaveDebounceTimer.Interval = TimeSpan.FromMilliseconds(SETTINGS_SAVE_DEBOUNCE_MS);
                settingsSaveDebounceTimer.Tick += SettingsSaveDebounceTimer_Tick;
            }

            settingsSaveDebounceTimer.Stop();
            settingsSaveDebouncePendingSender = sender;
            settingsSaveDebounceTimer.Start();
        }

        private void SettingsSaveDebounceTimer_Tick(object sender, object e)
        {
            settingsSaveDebounceTimer?.Stop();
            // Re-checks the same guards - state may have changed during the debounce wait.
            SettingChanged(settingsSaveDebouncePendingSender, e);
            settingsSaveDebouncePendingSender = null;
        }

    }
}
