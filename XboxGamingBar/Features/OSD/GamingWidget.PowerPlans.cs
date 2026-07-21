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
        // Monotonic revisions per functional field. A late response must never overwrite a
        // newer value of the SAME field, but a CPU-slider response must not be discarded merely
        // because the user independently changed HDR while it was in flight.
        private readonly Dictionary<string, long> latestProfileIntentRevisions = new Dictionary<string, long>();
        // LocalSettings remains only a UI cache. On every connection it is refreshed from the
        // helper before any direct user edit can use it as an edit buffer.
        private async Task SyncPowerSourceProfilesFromHelperAsync(string snapshotJson = null)
        {
            try
            {
                string json = snapshotJson;
                if (string.IsNullOrWhiteSpace(json))
                {
                    var request = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Get },
                        { "Function", (int)Shared.Enums.Function.PowerSourceProfileValues }
                    };
                    var response = await App.PipeClient.SendRequestAsync(request);
                    if (response == null || !response.TryGetValue("Content", out object content) || !(content is string received) || string.IsNullOrWhiteSpace(received))
                    {
                        Logger.Warn("Power-source profile snapshot was empty; leaving UI cache unchanged");
                        return;
                    }
                    json = received;
                }

                var values = Windows.Data.Json.JsonObject.Parse(json);
                double Number(string key, double fallback) => values.ContainsKey(key) ? values.GetNamedNumber(key, fallback) : fallback;
                bool Bool(string key, bool fallback) => values.ContainsKey(key) ? values.GetNamedBoolean(key, fallback) : fallback;
                string Text(string key, string fallback) => values.ContainsKey(key) ? values.GetNamedString(key, fallback) : fallback;
                PerformanceProfile Read(string prefix)
                {
                    int fps = (int)Number(prefix + "FpsLimit", 0);
                    int refresh = (int)Number(prefix + "RefreshRate", 0);
                    return new PerformanceProfile
                    {
                        LegionPerformanceMode = (int)Number(prefix + "LegionPerformanceMode", 2),
                        TDP = Number(prefix + "Tdp", 15), TDPFast = Number(prefix + "TdpFast", 15), TDPPeak = Number(prefix + "TdpPeak", 15),
                        CPUBoost = Bool(prefix + "CpuBoost", false), CPUEPP = Number(prefix + "CpuEpp", 80),
                        MaxCPUState = (int)Number(prefix + "MaxCpuState", 100), MinCPUState = (int)Number(prefix + "MinCpuState", 5),
                        OSPowerMode = (int)Number(prefix + "OsPowerMode", 1),
                        FPSLimitEnabled = fps > 0, FPSLimitValue = fps > 0 ? fps : 60,
                        HDREnabled = Bool(prefix + "HdrEnabled", false), Resolution = Text(prefix + "Resolution", ""), RefreshRate = refresh > 0 ? (int?)refresh : null,
                        FluidMotionFrames = Bool(prefix + "FluidMotionFrames", false), RadeonSuperResolution = Bool(prefix + "RadeonSuperResolution", false),
                        RadeonSuperResolutionSharpness = Number(prefix + "RadeonSuperResolutionSharpness", 80),
                        ImageSharpening = Bool(prefix + "ImageSharpening", false), ImageSharpeningSharpness = Number(prefix + "ImageSharpeningSharpness", 80),
                        RadeonAntiLag = Bool(prefix + "RadeonAntiLag", false), RadeonBoost = Bool(prefix + "RadeonBoost", false),
                        RadeonBoostResolution = Number(prefix + "RadeonBoostResolution", 0), RadeonChill = Bool(prefix + "RadeonChill", false),
                        RadeonChillMinFPS = Number(prefix + "RadeonChillMinFPS", 30), RadeonChillMaxFPS = Number(prefix + "RadeonChillMaxFPS", 60)
                    };
                }

                var ac = Read("Ac");
                var dc = Read("Dc");
                bool isGlobal = Bool("IsGlobal", true);
                bool splitEnabled = Bool("PowerSourceSplitEnabled", false);
                hasHelperActiveProfileScope = true;
                helperActiveProfileIsPerGame = Text("HelperActiveScope", "Global") == "PerGame";
                helperActiveProfileGameName = Text("HelperActiveGameName", "");
                helperActiveProfileGamePath = Text("HelperActiveGamePath", "");
                helperActiveProfileIsOnAC = Bool("HelperIsOnAC", true);
                helperActiveProfilePowerSourceSplit = Bool("HelperActivePowerSourceSplitEnabled", false);
                if (isGlobal)
                {
                    hasHelperGlobalPowerSourceSplit = true;
                    helperGlobalPowerSourceSplit = splitEnabled;
                    globalProfile = ac.Clone();
                    acProfile = ac;
                    dcProfile = dc;
                }
                else
                {
                    hasHelperGamePowerSourceSplit = true;
                    helperGamePowerSourceSplitGameName = currentGameName ?? "";
                    helperGamePowerSourceSplit = splitEnabled;
                    gameProfile = ac.Clone();
                    gameACProfile = ac;
                    gameDCProfile = dc;
                }
                UpdateDisplayProfileNameFromHelperScope();
                Logger.Info("Hydrated AC/DC profile cache from helper");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to hydrate power-source profiles from helper: {ex.Message}");
            }
        }

        private async Task SendProfileFieldIntentAsync(string field, object value)
        {
            if (!App.IsConnected)
            {
                await ShowSettingApplyFailureAsync(Function.PowerSourceProfileValues,
                    $"{field}: helper disconnected; the requested value was not applied.");
                return;
            }
            long intentRevision;
            lock (latestProfileIntentRevisions)
            {
                latestProfileIntentRevisions.TryGetValue(field, out long latest);
                intentRevision = latest + 1;
                latestProfileIntentRevisions[field] = intentRevision;
            }
            Windows.UI.Xaml.Controls.Control pendingControl = null;
            if (field == "CPUBoost") pendingControl = CPUBoostToggle;
            else if (field == "CPUEPP") pendingControl = CPUEPPSlider;
            else if (field == "CPUState")
            {
                if (MinCPUStateComboBox != null) MinCPUStateComboBox.IsEnabled = false;
                if (MaxCPUStateComboBox != null) MaxCPUStateComboBox.IsEnabled = false;
            }
            else if (field == "FPSLimit")
            {
                if (FPSLimitToggle != null) FPSLimitToggle.IsEnabled = false;
                if (FPSLimitSlider != null) FPSLimitSlider.IsEnabled = false;
            }
            else if (field == "OSPowerMode") pendingControl = OSPowerModeComboBox;
            else if (field == "RefreshRate") pendingControl = RefreshRatesComboBox;
            else if (field == "HDR") pendingControl = HDRToggle;
            else if (field == "Resolution") pendingControl = ResolutionComboBox;
            else if (field == "LegionPerformanceMode") pendingControl = TDPModeComboBox;
            else if (field == "CustomTDP")
            {
                if (CustomTDPSlowSlider != null) CustomTDPSlowSlider.IsEnabled = false;
                if (CustomTDPFastSlider != null) CustomTDPFastSlider.IsEnabled = false;
                if (CustomTDPPeakSlider != null) CustomTDPPeakSlider.IsEnabled = false;
            }
            if (pendingControl != null) pendingControl.IsEnabled = false;
            try
            {
                // Scope and AC/DC are helper-confirmed runtime state, not the widget's
                // local profile name. The fallback is used only before the first snapshot.
                bool perGame = hasHelperActiveProfileScope
                    ? helperActiveProfileIsPerGame
                    : perGameProfile?.Value == true && HasValidGame(currentGameName);
                bool dc = hasHelperActiveProfileScope
                    ? !helperActiveProfileIsOnAC
                    : currentProfileName != null && currentProfileName.EndsWith("_DC");
                var json = new Windows.Data.Json.JsonObject
                {
                    ["Intent"] = Windows.Data.Json.JsonValue.CreateStringValue("SetProfileField"),
                    ["Field"] = Windows.Data.Json.JsonValue.CreateStringValue(field),
                    ["Scope"] = Windows.Data.Json.JsonValue.CreateStringValue(perGame ? "PerGame" : "Global"),
                    ["Power"] = Windows.Data.Json.JsonValue.CreateStringValue(dc ? "DC" : "AC")
                };
                if (perGame)
                {
                    json["TargetGameName"] = Windows.Data.Json.JsonValue.CreateStringValue(hasHelperActiveProfileScope ? helperActiveProfileGameName : currentGameName ?? "");
                    json["TargetGamePath"] = Windows.Data.Json.JsonValue.CreateStringValue(hasHelperActiveProfileScope ? helperActiveProfileGamePath : currentGameExePath ?? "");
                }
                if (field == "CPUState" && value is int[] cpuState && cpuState.Length == 2)
                {
                    json["MinValue"] = Windows.Data.Json.JsonValue.CreateNumberValue(cpuState[0]);
                    json["MaxValue"] = Windows.Data.Json.JsonValue.CreateNumberValue(cpuState[1]);
                }
                else if (field == "CustomTDP" && value is int[] customTdp && customTdp.Length == 3)
                {
                    json["SlowValue"] = Windows.Data.Json.JsonValue.CreateNumberValue(customTdp[0]);
                    json["FastValue"] = Windows.Data.Json.JsonValue.CreateNumberValue(customTdp[1]);
                    json["PeakValue"] = Windows.Data.Json.JsonValue.CreateNumberValue(customTdp[2]);
                }
                else if (value is bool boolean) json["Value"] = Windows.Data.Json.JsonValue.CreateBooleanValue(boolean);
                else if (value is string text) json["Value"] = Windows.Data.Json.JsonValue.CreateStringValue(text);
                else json["Value"] = Windows.Data.Json.JsonValue.CreateNumberValue(Convert.ToDouble(value));

                var response = await App.PipeClient.SendRequestAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.PowerSourceProfileValues },
                    { "Content", json.Stringify() }
                });
                if (response == null || !response.TryGetValue("Content", out object raw) || !(raw is string content))
                {
                    Logger.Warn($"Profile intent {field} was not confirmed by helper");
                    await SyncPowerSourceProfilesFromHelperAsync();
                    await ShowSettingApplyFailureAsync(Function.PowerSourceProfileValues,
                        $"{field}: helper did not confirm the requested value.");
                    return;
                }
                var result = Windows.Data.Json.JsonObject.Parse(content);
                long latestForField;
                lock (latestProfileIntentRevisions)
                {
                    latestProfileIntentRevisions.TryGetValue(field, out latestForField);
                }
                if (intentRevision != latestForField)
                {
                    Logger.Debug($"Ignoring stale profile intent response for {field} (revision {intentRevision})");
                    return;
                }
                string outcome = result.GetNamedString("Outcome", "Rejected");
                if (outcome != "Applied")
                {
                    string reason = result.GetNamedString("Reason", "unknown reason");
                    Logger.Warn($"Profile intent {field} rejected: {reason}");
                    await ShowSettingApplyFailureAsync(Function.PowerSourceProfileValues, $"{field}: {reason}");
                }
                string snapshot = result.GetNamedString("Snapshot", "");
                await SyncPowerSourceProfilesFromHelperAsync(snapshot);
                ApplyConfirmedProfileFieldFromSnapshot(field, dc, snapshot);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Profile intent {field} failed: {ex.Message}");
                await SyncPowerSourceProfilesFromHelperAsync();
                await ShowSettingApplyFailureAsync(Function.PowerSourceProfileValues, $"{field}: {ex.Message}");
            }
            finally
            {
                if (pendingControl != null) pendingControl.IsEnabled = true;
                if (field == "CPUState")
                {
                    if (MinCPUStateComboBox != null) MinCPUStateComboBox.IsEnabled = true;
                    if (MaxCPUStateComboBox != null) MaxCPUStateComboBox.IsEnabled = true;
                }
                else if (field == "FPSLimit")
                {
                    if (FPSLimitToggle != null) FPSLimitToggle.IsEnabled = rtssInstalled?.Value == true;
                    if (FPSLimitSlider != null) FPSLimitSlider.IsEnabled = rtssInstalled?.Value == true;
                }
                else if (field == "CustomTDP")
                {
                    bool enabled = legionGoDetected?.Value == true && IsCustomTdpModeSelected();
                    if (CustomTDPSlowSlider != null) CustomTDPSlowSlider.IsEnabled = enabled;
                    if (CustomTDPFastSlider != null) CustomTDPFastSlider.IsEnabled = enabled;
                    if (CustomTDPPeakSlider != null) CustomTDPPeakSlider.IsEnabled = enabled;
                }
            }
        }

        private async Task SendPowerSourceSplitIntentAsync(bool enabled, bool perGame)
        {
            if (!App.IsConnected) return;
            try
            {
                if (hasHelperActiveProfileScope)
                    perGame = helperActiveProfileIsPerGame;
                var json = new Windows.Data.Json.JsonObject
                {
                    ["Intent"] = Windows.Data.Json.JsonValue.CreateStringValue("SetPowerSourceSplit"),
                    ["Scope"] = Windows.Data.Json.JsonValue.CreateStringValue(perGame ? "PerGame" : "Global"),
                    ["Enabled"] = Windows.Data.Json.JsonValue.CreateBooleanValue(enabled)
                };
                if (perGame)
                {
                    json["TargetGameName"] = Windows.Data.Json.JsonValue.CreateStringValue(hasHelperActiveProfileScope ? helperActiveProfileGameName : currentGameName ?? "");
                    json["TargetGamePath"] = Windows.Data.Json.JsonValue.CreateStringValue(hasHelperActiveProfileScope ? helperActiveProfileGamePath : currentGameExePath ?? "");
                }
                var response = await App.PipeClient.SendRequestAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.PowerSourceProfileValues },
                    { "Content", json.Stringify() }
                });
                if (response == null || !response.TryGetValue("Content", out object raw) || !(raw is string content))
                {
                    Logger.Warn("Power-source split was not confirmed by helper");
                    await SyncPowerSourceProfilesFromHelperAsync();
                    return;
                }
                var result = Windows.Data.Json.JsonObject.Parse(content);
                if (result.GetNamedString("Outcome", "Rejected") != "Applied")
                    Logger.Warn($"Power-source split rejected: {result.GetNamedString("Reason", "unknown reason")}");
                await SyncPowerSourceProfilesFromHelperAsync(result.GetNamedString("Snapshot", ""));
                SyncPowerSourceProfileToggleForCurrentContext();
                UpdateActiveProfileIndicator();
                UpdateProfileDisplay();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Power-source split intent failed: {ex.Message}");
                await SyncPowerSourceProfilesFromHelperAsync();
            }
        }

        private void UpdateDisplayProfileNameFromHelperScope()
        {
            if (!hasHelperActiveProfileScope) return;

            if (helperActiveProfileIsPerGame)
            {
                string gameName = helperActiveProfileGameName ?? "";
                currentProfileName = helperActiveProfilePowerSourceSplit
                    ? $"Game_{gameName}_{(helperActiveProfileIsOnAC ? "AC" : "DC")}" : $"Game_{gameName}";
            }
            else
            {
                currentProfileName = helperActiveProfilePowerSourceSplit
                    ? (helperActiveProfileIsOnAC ? "AC" : "DC") : "Global";
            }
        }

        // ToggleSwitch and Slider visually change before their event reaches us. Replace that
        // requested value with the helper-confirmed value on every response, including a
        // rejection. SuppressRemoteSync makes this a display update, never a retry.
        private void ApplyConfirmedProfileFieldFromSnapshot(string field, bool dc, string snapshotJson)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson)) return;
            try
            {
                var values = Windows.Data.Json.JsonObject.Parse(snapshotJson);
                string prefix = dc ? "Dc" : "Ac";
                if (field == "CPUBoost" && values.ContainsKey(prefix + "CpuBoost"))
                {
                    cpuBoost.SuppressRemoteSync = true;
                    try { cpuBoost.ForceSetValue(values.GetNamedBoolean(prefix + "CpuBoost")); }
                    finally { cpuBoost.SuppressRemoteSync = false; }
                }
                else if (field == "CPUEPP" && values.ContainsKey(prefix + "CpuEpp"))
                {
                    cpuEPP.SuppressRemoteSync = true;
                    try { cpuEPP.ForceSetValue((int)values.GetNamedNumber(prefix + "CpuEpp")); }
                    finally { cpuEPP.SuppressRemoteSync = false; }
                }
                else if (field == "CPUState" && values.ContainsKey(prefix + "MinCpuState") && values.ContainsKey(prefix + "MaxCpuState"))
                {
                    minCPUState.SuppressRemoteSync = true;
                    maxCPUState.SuppressRemoteSync = true;
                    try
                    {
                        minCPUState.ForceSetValue((int)values.GetNamedNumber(prefix + "MinCpuState"));
                        maxCPUState.ForceSetValue((int)values.GetNamedNumber(prefix + "MaxCpuState"));
                    }
                    finally
                    {
                        minCPUState.SuppressRemoteSync = false;
                        maxCPUState.SuppressRemoteSync = false;
                    }
                }
                else if (field == "FPSLimit" && values.ContainsKey(prefix + "FpsLimit"))
                {
                    int confirmed = (int)values.GetNamedNumber(prefix + "FpsLimit");
                    fpsLimit.SuppressRemoteSync = true;
                    try { fpsLimit.ForceSetValue(confirmed); }
                    finally { fpsLimit.SuppressRemoteSync = false; }

                    isApplyingHelperUpdate = true;
                    try
                    {
                        if (FPSLimitToggle != null) FPSLimitToggle.IsOn = confirmed > 0;
                        if (FPSLimitSlider != null && confirmed > 0) FPSLimitSlider.Value = confirmed;
                        if (FPSLimitValue != null) FPSLimitValue.Text = confirmed > 0 ? $"{confirmed} FPS" : "Off";
                    }
                    finally { isApplyingHelperUpdate = false; }
                }
                else if (field == "CustomTDP" && values.ContainsKey(prefix + "Tdp"))
                {
                    SetCustomTDPSlidersSilent(
                        (int)values.GetNamedNumber(prefix + "Tdp"),
                        (int)values.GetNamedNumber(prefix + "TdpFast"),
                        (int)values.GetNamedNumber(prefix + "TdpPeak"));
                }
                else if (field == "OSPowerMode" && values.ContainsKey(prefix + "OsPowerMode"))
                {
                    osPowerMode.SuppressRemoteSync = true;
                    try { osPowerMode.ForceSetValue((int)values.GetNamedNumber(prefix + "OsPowerMode")); }
                    finally { osPowerMode.SuppressRemoteSync = false; }
                }
                else if (field == "RefreshRate" && values.ContainsKey(prefix + "RefreshRate"))
                {
                    int confirmed = (int)values.GetNamedNumber(prefix + "RefreshRate");
                    if (confirmed > 0)
                    {
                        refreshRate.SuppressRemoteSync = true;
                        try { refreshRate.ForceSetValue(confirmed); }
                        finally { refreshRate.SuppressRemoteSync = false; }
                    }
                }
                else if (field == "HDR" && values.ContainsKey(prefix + "HdrEnabled"))
                {
                    hdrEnabled.SuppressRemoteSync = true;
                    try { hdrEnabled.ForceSetValue(values.GetNamedBoolean(prefix + "HdrEnabled")); }
                    finally { hdrEnabled.SuppressRemoteSync = false; }
                }
                else if (field == "Resolution" && values.ContainsKey(prefix + "Resolution"))
                {
                    resolution.SuppressRemoteSync = true;
                    try { resolution.ForceSetValue(values.GetNamedString(prefix + "Resolution")); }
                    finally { resolution.SuppressRemoteSync = false; }
                }
                else if (field == "LegionPerformanceMode" && values.ContainsKey(prefix + "LegionPerformanceMode"))
                {
                    int confirmed = (int)values.GetNamedNumber(prefix + "LegionPerformanceMode");
                    legionPerformanceMode.SuppressRemoteSync = true;
                    try { legionPerformanceMode.ForceSetValue(confirmed); }
                    finally { legionPerformanceMode.SuppressRemoteSync = false; }

                    int confirmedIndex = confirmed == 1 ? 0 : confirmed == 2 ? 1 : confirmed == 3 ? 2 : 3;
                    lastTDPModeIndex = confirmedIndex;
                    UpdateTDPSliderEnabledState();
                }
                else if (IsAmdProfileField(field))
                {
                    ApplyConfirmedAmdProfileFields(values, prefix);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to render confirmed {field} value: {ex.Message}");
            }
        }

        private static bool IsAmdProfileField(string field)
        {
            return field == "FluidMotionFrames" || field == "RadeonSuperResolution" || field == "RadeonSuperResolutionSharpness"
                || field == "ImageSharpening" || field == "ImageSharpeningSharpness" || field == "RadeonAntiLag"
                || field == "RadeonBoost" || field == "RadeonBoostResolution" || field == "RadeonChill"
                || field == "RadeonChillMinFPS" || field == "RadeonChillMaxFPS";
        }

        private void ApplyConfirmedAmdProfileFields(Windows.Data.Json.JsonObject values, string prefix)
        {
            void Bool(string key, XboxGamingBar.Data.WidgetProperty<bool> property)
            {
                if (!values.ContainsKey(prefix + key)) return;
                property.SuppressRemoteSync = true;
                try { property.ForceSetValue(values.GetNamedBoolean(prefix + key)); }
                finally { property.SuppressRemoteSync = false; }
            }
            void Int(string key, XboxGamingBar.Data.WidgetProperty<int> property)
            {
                if (!values.ContainsKey(prefix + key)) return;
                property.SuppressRemoteSync = true;
                try { property.ForceSetValue((int)values.GetNamedNumber(prefix + key)); }
                finally { property.SuppressRemoteSync = false; }
            }

            Bool("FluidMotionFrames", amdFluidMotionFrameEnabled);
            Bool("RadeonSuperResolution", amdRadeonSuperResolutionEnabled);
            Int("RadeonSuperResolutionSharpness", amdRadeonSuperResolutionSharpness);
            Bool("ImageSharpening", amdImageSharpeningEnabled);
            Int("ImageSharpeningSharpness", amdImageSharpeningSharpness);
            Bool("RadeonAntiLag", amdRadeonAntiLagEnabled);
            Bool("RadeonBoost", amdRadeonBoostEnabled);
            Int("RadeonBoostResolution", amdRadeonBoostResolution);
            Bool("RadeonChill", amdRadeonChillEnabled);
            Int("RadeonChillMinFPS", amdRadeonChillMinFPSProperty);
            Int("RadeonChillMaxFPS", amdRadeonChillMaxFPSProperty);
            UpdateAntiLagLockState();
        }

    }
}
