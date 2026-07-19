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
            if (!App.IsConnected) return;
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
                if (outcome != "Applied") Logger.Warn($"Profile intent {field} rejected: {result.GetNamedString("Reason", "unknown reason")}");
                string snapshot = result.GetNamedString("Snapshot", "");
                await SyncPowerSourceProfilesFromHelperAsync(snapshot);
                ApplyConfirmedProfileFieldFromSnapshot(field, dc, snapshot);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Profile intent {field} failed: {ex.Message}");
                await SyncPowerSourceProfilesFromHelperAsync();
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

        /// <summary>
        /// Pipes the active profile's AC and DC TDP / TDPBoost values to the helper. Helper
        /// caches both states and applies the appropriate set when SystemManager fires
        /// PowerSourceChanged — fixes the case where the slider visually updates on AC/DC
        /// but the hardware lags because the widget never told the helper the new value.
        /// Call this after LoadOrCreateGameProfiles, on profile-related setting changes, and
        /// on pipe connect. Only sends per-game game AC/DC profile if a per-game profile is
        /// in use; otherwise sends the global AC/DC profiles.
        /// </summary>
        internal void SendPowerSourceProfileValuesToHelper(string changedGroup = null)
        {
            try
            {
                if (!App.IsConnected) return;
                // A whole-profile payload is no longer a valid sync primitive. It is only an
                // edit patch for the group the user explicitly changed.
                if (string.IsNullOrEmpty(changedGroup))
                {
                    Logger.Debug("Skipping unscoped PowerSourceProfileValues send");
                    return;
                }

                // Pick which AC/DC pair drives the helper's per-state cache.
                //   1. Per-game profile with per-game AC/DC split enabled → game AC/DC.
                //   2. Global AC/DC split enabled → acProfile / dcProfile.
                //   3. Otherwise → globalProfile for BOTH sides (the user has no AC/DC
                //      differentiation, so AC and DC should resolve to the same values
                //      the helper would apply when no profile-driven override exists).
                //      Without this, acProfile/dcProfile sit at their constructor
                //      defaults (TDP=15W etc.) and the helper would clobber the user's
                //      real global TDP on every AC/DC transition (logged as the
                //      "global TDP=17 jumps to 15 on plug/unplug" bug).
                // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
                // 2026-07-19: this used to omit the PerGameProfileToggle.IsOn check that every
                // other "is per-game actually in effect" site in the codebase uses (e.g.
                // GetTargetProfileName, GetPowerSourceProfileEnabledForCurrentContext,
                // UpdateActiveProfileIndicator, UpdateGameProfileCardVisibility).
                // GetPerGamePowerSourceProfileEnabled is only a per-game PREFERENCE ("does this
                // game want its own AC/DC split if/when per-game profiles are used for it") - it
                // says nothing about whether per-game profiles are currently active. Without this
                // check, a game that previously had per-game profiles + its own AC/DC split
                // enabled would keep resyncing its stale gameACProfile/gameDCProfile to the helper
                // even after the user turned PerGameProfileToggle off (now editing/viewing the
                // global scope) - silently sending the wrong data while the UI shows something else.
                bool hasGameAcDc = HasValidGame(currentGameName)
                    && (PerGameProfileToggle?.IsOn ?? false)
                    && GetPerGamePowerSourceProfileEnabled(currentGameName);
                bool hasGlobalAcDc = GetGlobalPowerSourceProfileEnabled();
                PerformanceProfile ac, dc;
                string source;
                if (hasGameAcDc)
                {
                    ac = gameACProfile;
                    dc = gameDCProfile;
                    source = "game-AC/DC";
                }
                else if (hasGlobalAcDc)
                {
                    ac = acProfile;
                    dc = dcProfile;
                    source = "global-AC/DC";
                }
                else
                {
                    ac = globalProfile;
                    dc = globalProfile;
                    source = "global (no AC/DC split)";
                }

                var jsonObj = new Windows.Data.Json.JsonObject();
                // [2.0 rebuild - AC/DC persistence follow-up] LegionPerformanceMode (the TDP Mode
                // dropdown: Quiet/Balanced/Performance/Custom) was missing from this payload
                // entirely - found on-device 2026-07-18 ("TDP Mode" reported as not respecting the
                // AC/DC split). The widget's own ac/dc profile objects already track this
                // separately; it just never reached the helper.
                jsonObj["AcLegionPerformanceMode"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.LegionPerformanceMode);
                jsonObj["DcLegionPerformanceMode"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.LegionPerformanceMode);
                jsonObj["AcTdp"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.TDP);
                jsonObj["DcTdp"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.TDP);
                // SPPT/FPPT per power state - the helper needs the full Custom TDP triplet, not
                // just SPL, to correctly reapply Custom-mode limits on an AC/DC transition (a flat
                // TDP alone hits LegionManager.ReassertCustomTDP's cache-ignoring no-op).
                jsonObj["AcTdpFast"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.TDPFast);
                jsonObj["DcTdpFast"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.TDPFast);
                jsonObj["AcTdpPeak"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.TDPPeak);
                jsonObj["DcTdpPeak"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.TDPPeak);

                // Extended per-state values (build 2080+) — helper applies these on AC/DC
                // transitions independent of widget lifecycle, fixing FSE-only-helper drift.
                jsonObj["AcCpuBoost"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.CPUBoost);
                jsonObj["DcCpuBoost"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.CPUBoost);
                jsonObj["AcCpuEpp"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.CPUEPP);
                jsonObj["DcCpuEpp"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.CPUEPP);
                jsonObj["AcMaxCpuState"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.MaxCPUState);
                jsonObj["DcMaxCpuState"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.MaxCPUState);
                jsonObj["AcMinCpuState"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.MinCPUState);
                jsonObj["DcMinCpuState"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.MinCPUState);
                jsonObj["AcOsPowerMode"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.OSPowerMode);
                jsonObj["DcOsPowerMode"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.OSPowerMode);
                // FPSLimit collapses Enabled+Value into a single int on the wire: 0 = off,
                // non-zero = the cap. Matches the helper's FPSLimitProperty model where 0
                // means "no limit".
                jsonObj["AcFpsLimit"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.FPSLimitEnabled ? ac.FPSLimitValue : 0);
                jsonObj["DcFpsLimit"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.FPSLimitEnabled ? dc.FPSLimitValue : 0);

                // [2.0 rebuild - AC/DC persistence] HDR/Resolution/RefreshRate + the 11 AMD Radeon
                // fields - previously never sent to the helper at all (only the 9 fields above
                // existed on the wire), so the helper's ephemeral cache (and now its persisted
                // _DC fields) had no way to know these per-state values. RefreshRate is nullable on
                // this side (unset = "never configured") - omit the key entirely rather than
                // sending a sentinel 0, matching how the helper's ParseInt treats an absent key.
                jsonObj["AcHdrEnabled"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.HDREnabled);
                jsonObj["DcHdrEnabled"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.HDREnabled);
                jsonObj["AcResolution"] = Windows.Data.Json.JsonValue.CreateStringValue(ac.Resolution ?? "");
                jsonObj["DcResolution"] = Windows.Data.Json.JsonValue.CreateStringValue(dc.Resolution ?? "");
                if (ac.RefreshRate.HasValue) jsonObj["AcRefreshRate"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.RefreshRate.Value);
                if (dc.RefreshRate.HasValue) jsonObj["DcRefreshRate"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.RefreshRate.Value);

                jsonObj["AcFluidMotionFrames"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.FluidMotionFrames);
                jsonObj["DcFluidMotionFrames"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.FluidMotionFrames);
                jsonObj["AcRadeonSuperResolution"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.RadeonSuperResolution);
                jsonObj["DcRadeonSuperResolution"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.RadeonSuperResolution);
                jsonObj["AcRadeonSuperResolutionSharpness"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.RadeonSuperResolutionSharpness);
                jsonObj["DcRadeonSuperResolutionSharpness"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.RadeonSuperResolutionSharpness);
                jsonObj["AcImageSharpening"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.ImageSharpening);
                jsonObj["DcImageSharpening"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.ImageSharpening);
                jsonObj["AcImageSharpeningSharpness"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.ImageSharpeningSharpness);
                jsonObj["DcImageSharpeningSharpness"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.ImageSharpeningSharpness);
                jsonObj["AcRadeonAntiLag"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.RadeonAntiLag);
                jsonObj["DcRadeonAntiLag"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.RadeonAntiLag);
                jsonObj["AcRadeonBoost"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.RadeonBoost);
                jsonObj["DcRadeonBoost"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.RadeonBoost);
                jsonObj["AcRadeonBoostResolution"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.RadeonBoostResolution);
                jsonObj["DcRadeonBoostResolution"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.RadeonBoostResolution);
                jsonObj["AcRadeonChill"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ac.RadeonChill);
                jsonObj["DcRadeonChill"] = Windows.Data.Json.JsonValue.CreateBooleanValue(dc.RadeonChill);
                jsonObj["AcRadeonChillMinFPS"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.RadeonChillMinFPS);
                jsonObj["DcRadeonChillMinFPS"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.RadeonChillMinFPS);
                jsonObj["AcRadeonChillMaxFPS"] = Windows.Data.Json.JsonValue.CreateNumberValue(ac.RadeonChillMaxFPS);
                jsonObj["DcRadeonChillMaxFPS"] = Windows.Data.Json.JsonValue.CreateNumberValue(dc.RadeonChillMaxFPS);

                bool Keep(string key)
                {
                    switch (changedGroup)
                    {
                        case "TDP": return key.Contains("Tdp") || key.Contains("LegionPerformanceMode");
                        case "CPUBoost": return key.Contains("CpuBoost");
                        case "CPUEPP": return key.Contains("CpuEpp");
                        case "CPUState": return key.Contains("CpuState");
                        case "FPSLimit": return key.Contains("FpsLimit");
                        case "HDR": return key.Contains("HdrEnabled");
                        case "Resolution": return key.Contains("Resolution");
                        case "RefreshRate": return key.Contains("RefreshRate");
                        case "AMD": return key.Contains("FluidMotion") || key.Contains("Radeon") || key.Contains("ImageSharpening");
                        default: return false;
                    }
                }
                foreach (var key in jsonObj.Keys.Where(key => !Keep(key)).ToList()) jsonObj.Remove(key);

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.PowerSourceProfileValues },
                    { "Content", jsonObj.Stringify() },
                };
                App.PipeClient?.SendValueSet(request);
                Logger.Info($"Sent PowerSourceProfileValues to helper (source={source}, "
                    + $"AC: tdp={ac.TDP}W cpuBoost={ac.CPUBoost} epp={ac.CPUEPP} cpuState={ac.MinCPUState}-{ac.MaxCPUState} osMode={ac.OSPowerMode} fps={(ac.FPSLimitEnabled ? ac.FPSLimitValue : 0)}; "
                    + $"DC: tdp={dc.TDP}W cpuBoost={dc.CPUBoost} epp={dc.CPUEPP} cpuState={dc.MinCPUState}-{dc.MaxCPUState} osMode={dc.OSPowerMode} fps={(dc.FPSLimitEnabled ? dc.FPSLimitValue : 0)})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending PowerSourceProfileValues: {ex.Message}");
            }
        }

    }
}
