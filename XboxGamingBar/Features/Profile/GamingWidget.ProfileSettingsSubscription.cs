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
                || hdrEnabled?.IsUpdatingUI == true || resolution?.IsUpdatingUI == true
                // [full-audit fix, 2026-07-20 — B1] The Min/Max CPU State combos have TWO
                // SelectionChanged subscribers; a helper push reflects via ApplyCPUStateFromHelper
                // (which sets isApplyingCPUStateFromHelper synchronously around the SelectedIndex
                // write), but this generic handler never consulted that flag - so the push echoed
                // back as a user CPUState intent and could write a DC value into the AC field.
                || isApplyingCPUStateFromHelper)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate}, initialSync={isInitialSync}, cpuStateFromHelper={isApplyingCPUStateFromHelper})");
                return;
            }

            string group = GetPowerSourceProfileChangedGroup(sender);
            // First vertical 2.0 slice: these fields no longer write the widget-local
            // PerformanceProfile. The helper owns persistence and confirms the resulting state.
            //
            // [full-audit fix, 2026-07-20 — B4/B5/B6] Every send below is now gated by the
            // timing-independent value-equality no-op IsFieldIntentUnchanged. A helper-driven UI
            // update, by construction, sets the control to a value that already equals the bound
            // property's confirmed .Value, so any echo that reaches this handler after the
            // synchronous HelperSyncCount bracket has closed (a delayed/secondary event) is
            // suppressed here without needing a time-based grace window on the shared counter
            // (which caused B4/B5/B6). A genuine user edit always differs (the property's .Value
            // only updates on helper confirmation), so real edits still go through.
            if (group == "CPUBoost")
            {
                bool v = CPUBoostToggle?.IsOn ?? false;
                if (!IsFieldIntentUnchanged("CPUBoost", v)) _ = SendProfileFieldIntentAsync("CPUBoost", v);
                return;
            }
            if (group == "CPUEPP")
            {
                int cpuEppValue = (int)(CPUEPPSlider?.Value ?? 80);
                if (!IsFieldIntentUnchanged("CPUEPP", cpuEppValue)) _ = SendProfileFieldIntentAsync("CPUEPP", cpuEppValue);
                return;
            }
            if (group == "CPUState")
            {
                int minV = GetSelectedCPUStateValue(MinCPUStateComboBox);
                int maxV = GetSelectedCPUStateValue(MaxCPUStateComboBox);
                if (!IsFieldIntentUnchanged("CPUState", new[] { minV, maxV }))
                    _ = SendProfileFieldIntentAsync("CPUState", new[] { minV, maxV });
                return;
            }
            if (group == "RefreshRate")
            {
                if (RefreshRatesComboBox?.SelectedItem is int selectedRefreshRate
                    && !IsFieldIntentUnchanged("RefreshRate", selectedRefreshRate))
                    _ = SendProfileFieldIntentAsync("RefreshRate", selectedRefreshRate);
                return;
            }
            if (group == "HDR")
            {
                bool v = HDRToggle?.IsOn ?? false;
                if (!IsFieldIntentUnchanged("HDR", v)) _ = SendProfileFieldIntentAsync("HDR", v);
                return;
            }
            if (group == "Resolution")
            {
                if (ResolutionComboBox?.SelectedItem is string selectedResolution
                    && !IsFieldIntentUnchanged("Resolution", selectedResolution))
                    _ = SendProfileFieldIntentAsync("Resolution", selectedResolution);
                return;
            }
            if (TryGetAmdProfileFieldIntent(sender, out string amdField, out object amdValue))
            {
                if (!IsFieldIntentUnchanged(amdField, amdValue))
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
        /// [full-audit fix, 2026-07-20 — B4/B5/B6, generalized from the AMD-only version]
        /// Timing-independent value-equality no-op check for EVERY field sent through
        /// SettingChanged. A control's change event can fire (and reach SettingChanged /
        /// SettingChangedDebounced) from something other than a genuine user action - most often a
        /// helper-driven programmatic UI update whose synchronous HelperSyncCount bracket has
        /// already closed by the time a delayed/secondary event lands. Comparing the value about to
        /// be sent against the bound property's own confirmed .Value is correct regardless of what
        /// triggered the event: a helper push, by construction, sets the UI to a value that already
        /// matches the cache, so this is a safe no-op there; a genuine user edit always differs
        /// (the property's .Value only updates on helper confirmation), so real edits still go
        /// through (including an edit that happens to land on the same value - nothing to send
        /// either way). This replaces the fragile 200ms grace window that caused B4/B5/B6.
        /// </summary>
        private bool IsFieldIntentUnchanged(string field, object value)
        {
            switch (field)
            {
                // Performance / display fields
                case "CPUBoost":
                    return cpuBoost != null && value is bool cbV && cbV == cpuBoost.Value;
                case "CPUEPP":
                    return cpuEPP != null && value is int eppV && eppV == cpuEPP.Value;
                case "CPUState":
                    return minCPUState != null && maxCPUState != null && value is int[] st && st.Length == 2
                        && st[0] == minCPUState.Value && st[1] == maxCPUState.Value;
                case "RefreshRate":
                    return refreshRate != null && value is int rrV && rrV == refreshRate.Value;
                case "HDR":
                    return hdrEnabled != null && value is bool hdrV && hdrV == hdrEnabled.Value;
                case "Resolution":
                    return resolution != null && value is string resV && resV == resolution.Value;
                // AMD fields
                case "FluidMotionFrames":
                    return amdFluidMotionFrameEnabled != null && value is bool fmfV && fmfV == amdFluidMotionFrameEnabled.Value;
                case "RadeonSuperResolution":
                    return amdRadeonSuperResolutionEnabled != null && value is bool rsrEV && rsrEV == amdRadeonSuperResolutionEnabled.Value;
                case "RadeonSuperResolutionSharpness":
                    return amdRadeonSuperResolutionSharpness != null && value is int rsrV && rsrV == amdRadeonSuperResolutionSharpness.Value;
                case "ImageSharpening":
                    return amdImageSharpeningEnabled != null && value is bool risEV && risEV == amdImageSharpeningEnabled.Value;
                case "ImageSharpeningSharpness":
                    return amdImageSharpeningSharpness != null && value is int risV && risV == amdImageSharpeningSharpness.Value;
                case "RadeonAntiLag":
                    return amdRadeonAntiLagEnabled != null && value is bool antiLagV && antiLagV == amdRadeonAntiLagEnabled.Value;
                case "RadeonBoost":
                    return amdRadeonBoostEnabled != null && value is bool boostV && boostV == amdRadeonBoostEnabled.Value;
                case "RadeonChill":
                    return amdRadeonChillEnabled != null && value is bool chillV && chillV == amdRadeonChillEnabled.Value;
                case "RadeonChillMinFPS":
                    return amdRadeonChillMinFPSProperty != null && value is int minV && minV == amdRadeonChillMinFPSProperty.Value;
                case "RadeonChillMaxFPS":
                    return amdRadeonChillMaxFPSProperty != null && value is int maxV && maxV == amdRadeonChillMaxFPSProperty.Value;
                default:
                    return false;
            }
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
