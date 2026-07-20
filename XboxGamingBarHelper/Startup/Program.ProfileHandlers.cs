using NLog;
using Shared.Constants;
using Shared.Data;
using Shared.IPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.LosslessScaling;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Systems;
using XboxGamingBarHelper.Labs;
using Shared.Enums;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {
        private const string ProfileSaveFlagsSettingsKey = "ProfileSaveFlags";

        // Snapshot of the widget's Profiles-tab save checkboxes. Defaults match the widget's
        // initial-field defaults so any handler that runs before the widget has pushed flags
        // falls back to the same behavior the UI shows to the user.
        // - true  => setting is captured per-game; mid-session writes land in CurrentProfile.
        // - false => setting is global; mid-session writes land in GlobalProfile regardless of
        //            whether a game is active, so reboots don't pull stale per-game values back.
        private static class ProfileSaveFlagsState
        {
            public static bool TDP = true;
            public static bool CPUBoost = true;
            public static bool CPUEPP = true;
            public static bool CPUState = true;
            public static bool AMDFeatures = false;
            public static bool FPSLimit = true;
            public static bool OSPowerMode = true;
            public static bool HDR = false;
            public static bool Resolution = false;
            public static bool RefreshRate = false;
            public static bool OverlayLevel = false;
            public static bool NintendoLayout = false;
            public static bool Vibration = false;
            public static bool Lighting = false;
            public static bool ButtonMappings = false;
            public static bool GyroSettings = false;

            static ProfileSaveFlagsState()
            {
                if (LocalSettingsHelper.TryGetValue<string>(ProfileSaveFlagsSettingsKey, out var saved)
                    && !string.IsNullOrWhiteSpace(saved))
                    ApplyProfileSaveFlags(saved, persist: false);
            }
        }

        internal static void ApplyProfileSaveFlags(string configJson)
        {
            ApplyProfileSaveFlags(configJson, persist: true);
        }

        private static void ApplyProfileSaveFlags(string configJson, bool persist)
        {
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(configJson);
                if (cfg == null) return;
                if (cfg.TryGetValue("TDP", out var v1)) ProfileSaveFlagsState.TDP = v1;
                if (cfg.TryGetValue("CPUBoost", out var v2)) ProfileSaveFlagsState.CPUBoost = v2;
                if (cfg.TryGetValue("CPUEPP", out var v3)) ProfileSaveFlagsState.CPUEPP = v3;
                if (cfg.TryGetValue("CPUState", out var v4)) ProfileSaveFlagsState.CPUState = v4;
                if (cfg.TryGetValue("AMDFeatures", out var v5)) ProfileSaveFlagsState.AMDFeatures = v5;
                if (cfg.TryGetValue("FPSLimit", out var v6)) ProfileSaveFlagsState.FPSLimit = v6;
                if (cfg.TryGetValue("OSPowerMode", out var v8)) ProfileSaveFlagsState.OSPowerMode = v8;
                if (cfg.TryGetValue("HDR", out var v9)) ProfileSaveFlagsState.HDR = v9;
                if (cfg.TryGetValue("Resolution", out var v10)) ProfileSaveFlagsState.Resolution = v10;
                if (cfg.TryGetValue("RefreshRate", out var v11)) ProfileSaveFlagsState.RefreshRate = v11;
                if (cfg.TryGetValue("OverlayLevel", out var v13)) ProfileSaveFlagsState.OverlayLevel = v13;
                if (cfg.TryGetValue("NintendoLayout", out var v15)) ProfileSaveFlagsState.NintendoLayout = v15;
                if (cfg.TryGetValue("Vibration", out var v16)) ProfileSaveFlagsState.Vibration = v16;
                if (cfg.TryGetValue("Lighting", out var v17)) ProfileSaveFlagsState.Lighting = v17;
                if (cfg.TryGetValue("ButtonMappings", out var v18)) ProfileSaveFlagsState.ButtonMappings = v18;
                if (cfg.TryGetValue("GyroSettings", out var v19)) ProfileSaveFlagsState.GyroSettings = v19;
                if (persist)
                    LocalSettingsHelper.SetValue(ProfileSaveFlagsSettingsKey, GetProfileSaveFlagsSnapshot());
                Logger.Info("Applied ProfileSaveFlags from widget "
                    + $"(TDP={ProfileSaveFlagsState.TDP}, CPUBoost={ProfileSaveFlagsState.CPUBoost}, "
                    + $"CPUEPP={ProfileSaveFlagsState.CPUEPP}, CPUState={ProfileSaveFlagsState.CPUState}, "
                    + $"NintendoLayout={ProfileSaveFlagsState.NintendoLayout}, "
                    + $"Vibration={ProfileSaveFlagsState.Vibration}, Lighting={ProfileSaveFlagsState.Lighting}, "
                    + $"ButtonMappings={ProfileSaveFlagsState.ButtonMappings}, GyroSettings={ProfileSaveFlagsState.GyroSettings})");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyProfileSaveFlags: {ex.Message}");
            }
        }

        internal static string GetProfileSaveFlagsSnapshot()
        {
            // Reading any field triggers ProfileSaveFlagsState's static initializer, which loads
            // the helper-owned persisted snapshot before it is exposed to a widget.
            return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, bool>
            {
                ["TDP"] = ProfileSaveFlagsState.TDP,
                ["CPUBoost"] = ProfileSaveFlagsState.CPUBoost,
                ["CPUEPP"] = ProfileSaveFlagsState.CPUEPP,
                ["CPUState"] = ProfileSaveFlagsState.CPUState,
                ["AMDFeatures"] = ProfileSaveFlagsState.AMDFeatures,
                ["FPSLimit"] = ProfileSaveFlagsState.FPSLimit,
                ["OSPowerMode"] = ProfileSaveFlagsState.OSPowerMode,
                ["HDR"] = ProfileSaveFlagsState.HDR,
                ["Resolution"] = ProfileSaveFlagsState.Resolution,
                ["RefreshRate"] = ProfileSaveFlagsState.RefreshRate,
                ["OverlayLevel"] = ProfileSaveFlagsState.OverlayLevel,
                ["NintendoLayout"] = ProfileSaveFlagsState.NintendoLayout,
                ["Vibration"] = ProfileSaveFlagsState.Vibration,
                ["Lighting"] = ProfileSaveFlagsState.Lighting,
                ["ButtonMappings"] = ProfileSaveFlagsState.ButtonMappings,
                ["GyroSettings"] = ProfileSaveFlagsState.GyroSettings,
            });
        }

        // Routes a setting save to CurrentProfile (per-game capture) when saveToProfile is true,
        // else to GlobalProfile (treat as device-wide). Caller supplies a setter action for each
        // target; the target's own setter handles the equality check and debounced Save().
        //
        // Takes profileApplicationLock so a save landing here can't race ApplyProfileFieldIntent's
        // read-mutate-reapply sequence (which now holds the same lock, see the fresh re-read there)
        // or the 4 full-reapply sites - a save interleaving a snapshot's read and its reapply could
        // otherwise have the reapply write an OLD field value back over the more recent change.
        // (Originally motivated by the removed external-change-detection listener firing these
        // handlers on a native ADLX thread; the concurrent-writer surface is now the power-event
        // thread + any off-pipe-thread save, but the lock is still needed for those.) The lock is
        // cheap (no I/O; Save() only queues a debounced write) so it adds no meaningful contention.
        private static void RouteProfileSave(bool saveToProfile, string settingName,
            Action<Profile.GameProfileProperty> onCurrent, Action<Shared.Data.GameProfile> onGlobal)
        {
            lock (profileApplicationLock)
            {
                // [root-cause fix, 2026-07-20] See isRoutingProfileSave's doc comment (Program.cs) -
                // this is a pure persistence write; mutating CurrentProfile below must not be
                // mistaken by CurrentProfile_PropertyChanged for a request to reapply everything.
                isRoutingProfileSave = true;
                try
                {
                    if (saveToProfile)
                    {
                        Logger.Info($"Saving {settingName} to profile {profileManager.CurrentProfile.GameId.Name}");
                        onCurrent(profileManager.CurrentProfile);
                    }
                    else
                    {
                        Logger.Info($"Saving {settingName} to global (per-game capture disabled)");
                        // [2.0 fix] The old code passed GlobalProfile (a mutable STRUCT) by value to the
                        // onGlobal lambda, so `glo => glo.X = value` only mutated a throwaway copy - the
                        // save was silently lost (proven via global.xml: LegionVibration/Light/gyro stayed
                        // xsi:nil while deadzones, which route through CurrentProfile, persisted). This is a
                        // pre-existing §29 bug, masked until the widget stopped owning these settings.
                        // When no game is active, CurrentProfile IS the global profile (a reference-type
                        // GameProfileProperty that persists correctly) - route through it. When a game IS
                        // active, mutate the GlobalProfile struct field and write it back.
                        if (profileManager.CurrentProfile.IsGlobalProfile)
                        {
                            onCurrent(profileManager.CurrentProfile);
                        }
                        else
                        {
                            // [code review fix] The above fix was still incomplete: onGlobal is
                            // Action<GameProfile>, a VALUE type, so `onGlobal(glob)` mutates only the
                            // delegate's own by-value parameter copy - `glob` in this scope was never
                            // touched, making `profileManager.GlobalProfile = glob;` a no-op
                            // self-reassignment. The mutation's Save() call still correctly updates the
                            // shared cache dictionary AND queues the disk write (both keyed by GameId/Path,
                            // reference-type fields untouched by the struct copy) - only the separate
                            // in-memory GlobalProfile snapshot field was stale. RefreshGlobalProfile()
                            // flushes that pending write then re-reads it from disk, correctly picking up
                            // the change instead of the useless reassignment.
                            var glob = profileManager.GlobalProfile;
                            onGlobal(glob);
                            profileManager.RefreshGlobalProfile();
                        }
                    }
                }
                finally
                {
                    isRoutingProfileSave = false;
                }
            }
        }

        // [2.0 rebuild - profile-system consolidation] TDP triplet + Custom-mode gating +
        // CPU cluster (Boost/EPP/Max/MinCPUState), shared by all 4 full-reapply trigger sites
        // (RestoreGlobalProfileSettings, CurrentProfile_PropertyChanged, PerGameProfile_
        // PropertyChanged, RunningGame_PropertyChanged), which used to hand-duplicate this exact
        // isOnAC-resolution block 4 times - each with its own independently-discovered gap
        // history (see the many "found in an independent audit" comments this replaces).
        // LegionPerformanceMode's own switch-decision is NOT part of this - it stays inline at
        // each call site because the decision genuinely differs per site (RestoreGlobalProfile
        // Settings always switches if set; PerGameProfile_PropertyChanged defaults new profiles
        // to Custom; CurrentProfile_PropertyChanged gates on cp.Use) - the caller must resolve
        // LegionPerformanceMode BEFORE calling this, so legionManager.LegionPerformanceMode.Value
        // already reflects it here.
        private static void ApplyTDPAndCPUFromProfile(Shared.Data.GameProfile profile, bool isOnAC)
        {
            int targetTdp = isOnAC ? profile.TDP : (profile.TDP_DC ?? profile.TDP);
            int targetTdpFast = isOnAC ? profile.TDPFast : (profile.TDPFast_DC ?? profile.TDPFast);
            int targetTdpPeak = isOnAC ? profile.TDPPeak : (profile.TDPPeak_DC ?? profile.TDPPeak);
            performanceManager.TDP.SetProfileValue(targetTdp);
            // [TDP Custom-mode fix] The flat TDP.SetProfileValue call above is a no-op for the
            // actual hardware SPPT/FPPT in Custom mode (ReassertCustomTDP re-pushes whatever's
            // cached, ignoring the passed value). Push the real triplet through SetCustomTDP, the
            // same mode-switch-safe path the widget's sliders use.
            if (legionManager != null && legionManager.LegionPerformanceMode.Value == 255)
            {
                legionManager.SetCustomTDP(targetTdp, targetTdpFast, targetTdpPeak);
            }

            int targetMaxCpuState = isOnAC ? profile.MaxCPUState : (profile.MaxCPUState_DC ?? profile.MaxCPUState);
            bool targetCpuBoost = isOnAC ? profile.CPUBoost : (profile.CPUBoost_DC ?? profile.CPUBoost);
            // [self-heal, matches the AMD mutual-exclusion pattern] Windows cannot boost above a
            // max CPU state below 100% - this was previously enforced only at SetProfileField
            // intent-time (ApplyProfileFieldIntent's CPUState branch), so legacy/hand-edited data
            // that violates it never self-corrected on a normal reapply. Persist the correction
            // through the profile's own property setter (calls Save() on the local copy - safe,
            // see GameProfile.Save()'s Path-keyed PendingWrites) so it doesn't need re-fixing here
            // forever, same rationale as the AMD Chill/AntiLag/Boost persistence fix.
            if (targetMaxCpuState < 100 && targetCpuBoost)
            {
                Logger.Warn($"Profile has CPUBoost enabled with MaxCPUState={targetMaxCpuState}% (<100%) - Windows cannot boost above a reduced max state; disabling CPUBoost");
                targetCpuBoost = false;
                if (isOnAC) profile.CPUBoost = false; else profile.CPUBoost_DC = false;
            }
            powerManager.CPUBoost.SetValue(targetCpuBoost);
            powerManager.CPUEPP.SetValue(isOnAC ? profile.CPUEPP : (profile.CPUEPP_DC ?? profile.CPUEPP));
            powerManager.MaxCPUState.SetValue(targetMaxCpuState);
            powerManager.MinCPUState.SetValue(isOnAC ? profile.MinCPUState : (profile.MinCPUState_DC ?? profile.MinCPUState));
        }

        // [2.0 rebuild - profile-system consolidation] FPSLimit/HDR/Resolution/RefreshRate, shared
        // by all 4 full-reapply trigger sites plus ApplyPowerSourceChangeInternal (Program.
        // PowerSourceHandler.cs). The null-conditional manager guards (?.) are the more defensive
        // variant 2 of the 5 original call sites already used - kept for all callers now.
        private static void ApplyDisplaySettingsFromProfile(Shared.Data.GameProfile profile, bool isOnAC)
        {
            int? targetFpsLimit = isOnAC ? profile.FPSLimit : (profile.FPSLimit_DC ?? profile.FPSLimit);
            if (targetFpsLimit.HasValue && rtssManager?.FPSLimit != null)
            {
                rtssManager.FPSLimit.SetValue(targetFpsLimit.Value);
            }
            bool? targetHdr = isOnAC ? profile.HDREnabled : (profile.HDREnabled_DC ?? profile.HDREnabled);
            if (targetHdr.HasValue && systemManager?.HDREnabled != null)
            {
                systemManager.HDREnabled.SetValue(targetHdr.Value);
            }
            string targetResolution = isOnAC ? profile.Resolution : (profile.Resolution_DC ?? profile.Resolution);
            if (!string.IsNullOrEmpty(targetResolution) && systemManager?.Resolution != null)
            {
                systemManager.Resolution.SetValue(targetResolution);
            }
            int? targetRefreshRate = isOnAC ? profile.RefreshRate : (profile.RefreshRate_DC ?? profile.RefreshRate);
            if (targetRefreshRate.HasValue && systemManager?.RefreshRate != null)
            {
                systemManager.RefreshRate.SetValue(targetRefreshRate.Value);
            }
        }

        // [2.0 rebuild - profile-system consolidation] Now takes the already-resolved profile
        // struct instead of reading profileManager.CurrentProfile directly - closes a staleness
        // gap found while fixing the GameId-only-equality bug (this function used to implicitly
        // depend on CurrentProfile being fresh, which SetValue's dedupe check could silently
        // violate; ForceSetValue fixes that at the source, but passing the struct explicitly here
        // removes the dependency entirely).
        private static void ApplyLegionControllerSettingsFromProfile(Shared.Data.GameProfile profile)
        {
            var profileName = profile.GameId.Name;

            Logger.Info($"Applying Legion controller settings from profile: {profileName}");

            // Button mappings - skip empty/null fields (not configured in profile).
            // An explicit disabled mapping like {"Type":0,"GamepadAction":0,...} MUST be
            // applied so the hardware clear command is sent; otherwise buttons like Desktop
            // keep their hardware default (Xbox) even though the UI shows "Disabled".
            if (!string.IsNullOrEmpty(profile.LegionButtonY1))
            {
                Logger.Debug($"Applying LegionButtonY1: {profile.LegionButtonY1}");
                legionManager.LegionButtonY1.SetValue(profile.LegionButtonY1);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonY2))
            {
                Logger.Debug($"Applying LegionButtonY2: {profile.LegionButtonY2}");
                legionManager.LegionButtonY2.SetValue(profile.LegionButtonY2);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonY3))
            {
                Logger.Debug($"Applying LegionButtonY3: {profile.LegionButtonY3}");
                legionManager.LegionButtonY3.SetValue(profile.LegionButtonY3);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonM1))
            {
                Logger.Debug($"Applying LegionButtonM1: {profile.LegionButtonM1}");
                legionManager.LegionButtonM1.SetValue(profile.LegionButtonM1);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonM2))
            {
                Logger.Debug($"Applying LegionButtonM2: {profile.LegionButtonM2}");
                legionManager.LegionButtonM2.SetValue(profile.LegionButtonM2);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonM3))
            {
                Logger.Debug($"Applying LegionButtonM3: {profile.LegionButtonM3}");
                legionManager.LegionButtonM3.SetValue(profile.LegionButtonM3);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonDesktop))
            {
                Logger.Debug($"Applying LegionButtonDesktop: {profile.LegionButtonDesktop}");
                legionManager.LegionButtonDesktop.SetValue(profile.LegionButtonDesktop);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonPage))
            {
                Logger.Debug($"Applying LegionButtonPage: {profile.LegionButtonPage}");
                legionManager.LegionButtonPage.SetValue(profile.LegionButtonPage);
            }

            // Gyro settings
            // Apply explicit safe defaults when profile entries are missing so gyro never
            // inherits prior game/global values by accident.
            int legionGyroButton = profile.LegionGyroButton ?? 0;
            int legionGyroTarget = profile.LegionGyroTarget ?? 0;
            int legionGyroSensitivityX = profile.LegionGyroSensitivityX ?? 50;
            int legionGyroSensitivityY = profile.LegionGyroSensitivityY ?? 50;
            bool legionGyroInvertX = profile.LegionGyroInvertX ?? false;
            bool legionGyroInvertY = profile.LegionGyroInvertY ?? false;
            int legionGyroMappingType = profile.LegionGyroMappingType ?? 0;
            int legionGyroActivationMode = profile.LegionGyroActivationMode ?? 0;
            int legionGyroDeadzone = profile.LegionGyroDeadzone ?? 10;

            Logger.Debug($"Applying LegionGyroButton: {legionGyroButton}{(profile.LegionGyroButton.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroActivationButton.SetValue(legionGyroButton);

            Logger.Debug($"Applying LegionGyroTarget: {legionGyroTarget}{(profile.LegionGyroTarget.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroTarget.SetValue(legionGyroTarget);

            Logger.Debug($"Applying LegionGyroSensitivityX: {legionGyroSensitivityX}{(profile.LegionGyroSensitivityX.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroSensitivityX.SetValue(legionGyroSensitivityX);

            Logger.Debug($"Applying LegionGyroSensitivityY: {legionGyroSensitivityY}{(profile.LegionGyroSensitivityY.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroSensitivityY.SetValue(legionGyroSensitivityY);

            Logger.Debug($"Applying LegionGyroInvertX: {legionGyroInvertX}{(profile.LegionGyroInvertX.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroInvertX.SetValue(legionGyroInvertX);

            Logger.Debug($"Applying LegionGyroInvertY: {legionGyroInvertY}{(profile.LegionGyroInvertY.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroInvertY.SetValue(legionGyroInvertY);

            Logger.Debug($"Applying LegionGyroMappingType: {legionGyroMappingType}{(profile.LegionGyroMappingType.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroMappingType.SetValue(legionGyroMappingType);

            Logger.Debug($"Applying LegionGyroActivationMode: {legionGyroActivationMode}{(profile.LegionGyroActivationMode.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroActivationMode.SetValue(legionGyroActivationMode);

            Logger.Debug($"Applying LegionGyroDeadzone: {legionGyroDeadzone}{(profile.LegionGyroDeadzone.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroDeadzone.SetValue(legionGyroDeadzone);

            // Stick deadzones
            if (profile.LegionLeftStickDeadzone.HasValue)
            {
                Logger.Debug($"Applying LegionLeftStickDeadzone: {profile.LegionLeftStickDeadzone.Value}");
                legionManager.LegionLeftStickDeadzone.SetValue(profile.LegionLeftStickDeadzone.Value);
            }
            if (profile.LegionRightStickDeadzone.HasValue)
            {
                Logger.Debug($"Applying LegionRightStickDeadzone: {profile.LegionRightStickDeadzone.Value}");
                legionManager.LegionRightStickDeadzone.SetValue(profile.LegionRightStickDeadzone.Value);
            }

            // Trigger travel
            if (profile.LegionLeftTriggerStart.HasValue)
            {
                Logger.Debug($"Applying LegionLeftTriggerStart: {profile.LegionLeftTriggerStart.Value}");
                legionManager.LegionLeftTriggerStart.SetValue(profile.LegionLeftTriggerStart.Value);
            }
            if (profile.LegionLeftTriggerEnd.HasValue)
            {
                Logger.Debug($"Applying LegionLeftTriggerEnd: {profile.LegionLeftTriggerEnd.Value}");
                legionManager.LegionLeftTriggerEnd.SetValue(profile.LegionLeftTriggerEnd.Value);
            }
            if (profile.LegionRightTriggerStart.HasValue)
            {
                Logger.Debug($"Applying LegionRightTriggerStart: {profile.LegionRightTriggerStart.Value}");
                legionManager.LegionRightTriggerStart.SetValue(profile.LegionRightTriggerStart.Value);
            }
            if (profile.LegionRightTriggerEnd.HasValue)
            {
                Logger.Debug($"Applying LegionRightTriggerEnd: {profile.LegionRightTriggerEnd.Value}");
                legionManager.LegionRightTriggerEnd.SetValue(profile.LegionRightTriggerEnd.Value);
            }
            if (profile.LegionHairTriggers.HasValue)
            {
                Logger.Debug($"Applying LegionHairTriggers: {profile.LegionHairTriggers.Value}");
                legionManager.LegionHairTriggers.SetValue(profile.LegionHairTriggers.Value);
            }

            // Joystick as mouse
            if (profile.LegionJoystickAsMouseMode.HasValue)
            {
                Logger.Debug($"Applying LegionJoystickAsMouseMode: {profile.LegionJoystickAsMouseMode.Value}");
                legionManager.LegionJoystickAsMouseMode.SetValue(profile.LegionJoystickAsMouseMode.Value);
            }
            if (profile.LegionJoystickMouseSens.HasValue)
            {
                Logger.Debug($"Applying LegionJoystickMouseSens: {profile.LegionJoystickMouseSens.Value}");
                legionManager.LegionJoystickMouseSens.SetValue(profile.LegionJoystickMouseSens.Value);
            }

            // Gamepad mapping
            if (!string.IsNullOrEmpty(profile.LegionGamepadMapping))
            {
                Logger.Debug($"Applying LegionGamepadMapping from profile");
                legionManager.LegionGamepadMapping.SetValue(profile.LegionGamepadMapping);
            }

            // Other controller settings
            if (profile.LegionNintendoLayout.HasValue)
            {
                Logger.Debug($"Applying LegionNintendoLayout: {profile.LegionNintendoLayout.Value}");
                legionManager.LegionNintendoLayout.SetValue(profile.LegionNintendoLayout.Value);
            }
            if (profile.LegionVibration.HasValue)
            {
                Logger.Debug($"Applying LegionVibration: {profile.LegionVibration.Value}");
                legionManager.LegionVibration.SetValue(profile.LegionVibration.Value);
            }
            if (profile.LegionVibrationMode.HasValue)
            {
                Logger.Debug($"Applying LegionVibrationMode: {profile.LegionVibrationMode.Value}");
                legionManager.LegionVibrationMode.SetValue(profile.LegionVibrationMode.Value);
            }

            // Lighting settings - apply color, brightness, and speed BEFORE mode
            // to prevent flash to white when mode is applied with old/default color
            if (!string.IsNullOrEmpty(profile.LegionLightColor))
            {
                Logger.Debug($"Applying LegionLightColor: {profile.LegionLightColor}");
                legionManager.LegionLightColor.SetValue(profile.LegionLightColor);
            }
            if (profile.LegionLightBrightness.HasValue)
            {
                Logger.Debug($"Applying LegionLightBrightness: {profile.LegionLightBrightness.Value}");
                legionManager.LegionLightBrightness.SetValue(profile.LegionLightBrightness.Value);
            }
            if (profile.LegionLightSpeed.HasValue)
            {
                Logger.Debug($"Applying LegionLightSpeed: {profile.LegionLightSpeed.Value}");
                legionManager.LegionLightSpeed.SetValue(profile.LegionLightSpeed.Value);
            }
            // Apply mode last so it uses the updated color/brightness/speed
            if (profile.LegionLightMode.HasValue)
            {
                Logger.Debug($"Applying LegionLightMode: {profile.LegionLightMode.Value}");
                legionManager.LegionLightMode.SetValue(profile.LegionLightMode.Value);
            }
            if (profile.LegionPowerLight.HasValue)
            {
                Logger.Debug($"Applying LegionPowerLight: {profile.LegionPowerLight.Value}");
                legionManager.LegionPowerLight.SetValue(profile.LegionPowerLight.Value);
            }
        }


        /// <summary>
        /// Restores global profile settings (TDP, AutoTDP, Legion mode, etc.)
        /// Called when transitioning away from a per-game profile:
        /// - Game stops (RunningGame becomes invalid)
        /// - Game changes from per-game profile game to non-per-game-profile game
        /// - Per-game profile is explicitly disabled by widget
        /// Must be called within isApplyingProfile = true context.
        /// </summary>
        private static void RestoreGlobalProfileSettings()
        {
            // Refresh GlobalProfile from cache — property change handlers (TDP_PropertyChanged, etc.)
            // update CurrentProfile (a struct copy), which saves to cache/disk, but the
            // GlobalProfile field stays stale since GameProfile is a struct.
            profileManager.RefreshGlobalProfile();

            // [race fix] GameProfile.Equals/== compares only GameId (by design, for dictionary
            // keying) - but GenericProperty<T>.SetValue reuses that as its "did anything change"
            // dedupe check, so SetValue is a complete no-op (no field replacement, no
            // PropertyChanged) whenever CurrentProfile already holds this same GameId - exactly
            // the "already on global, restoring global again" case this function exists for.
            // ForceSetValue unconditionally replaces + notifies.
            profileManager.CurrentProfile.ForceSetValue(profileManager.GlobalProfile);

            Logger.Info($"Applying global profile settings: TDP={profileManager.GlobalProfile.TDP}, CPUBoost={profileManager.GlobalProfile.CPUBoost}, EPP={profileManager.GlobalProfile.CPUEPP}");

            // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
            // 2026-07-19: this whole method used to read ONLY the base (AC) fields, regardless of
            // the actual live power state - so restoring the global profile (e.g. on game close)
            // while genuinely on battery applied the AC-side values instead of the configured DC
            // overrides. No AC/DC transition event fires to correct it afterward (the power state
            // didn't change). Same resolve pattern as Program.PowerSourceHandler.cs's
            // ApplyPowerSourceChangeInternal.
            var globalProfile = profileManager.GlobalProfile;
            // [full-audit fix, 2026-07-20 — A#3] Resolve via ResolvesOnAc so a split-DISABLED
            // profile always resolves the base (AC) value even on battery, matching the collapse
            // ApplyPowerSourceChangeInternal already does. "isOnAC" here means "resolve the base
            // value", which is exactly what the downstream (isOnAC ? base : (_DC ?? base)) expects.
            bool isOnAC = ResolvesOnAc(globalProfile);

            // Restore LegionPerformanceMode from global profile if set
            if (legionManager != null)
            {
                int? savedMode = isOnAC ? globalProfile.LegionPerformanceMode : (globalProfile.LegionPerformanceMode_DC ?? globalProfile.LegionPerformanceMode);
                if (savedMode.HasValue)
                {
                    int currentMode = legionManager.LegionPerformanceMode.Value;
                    if (currentMode != savedMode.Value)
                    {
                        Logger.Info($"Restoring global profile performance mode ({savedMode.Value}) (was {currentMode})");
                        legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                    }
                }
            }

            ApplyTDPAndCPUFromProfile(globalProfile, isOnAC);
            ApplyOSPowerModeFromProfile(globalProfile, isOnAC, "Restoring global profile");
            ApplyDisplaySettingsFromProfile(globalProfile, isOnAC);
            // [2.0 rebuild - Faza C2]
            ApplyAMDFeaturesFromProfile(globalProfile, isOnAC);
            profileManager.PerGameProfile.SetValue(false);

            // Apply Legion controller settings from global profile
            if (legionManager != null)
            {
                ApplyLegionControllerSettingsFromProfile(globalProfile);
            }
        }

        /// <summary>
        /// [2.0 rebuild - Faza C2] Applies the 6 AMD Radeon per-game feature toggles (11 fields
        /// incl. their value sliders) from a GameProfile struct. Shared by both apply sites
        /// (RunningGame_PropertyChanged's per-game block passes the TryGetProfile struct;
        /// RestoreGlobalProfileSettings passes GlobalProfile - both are the same
        /// Shared.Data.GameProfile type). All fields are nullable - only apply what this profile
        /// ever explicitly touched, same convention as the gyro fields in
        /// ApplyLegionControllerSettingsFromProfile never forcing an unconfigured default.
        ///
        /// [code review fix] RSR/RIS and Chill/AntiLag/Boost are mutually exclusive on the driver
        /// side; the widget's old LoadProfileSettings used to correct a profile that had both sides
        /// of a pair enabled (e.g. from an external AMD Adrenalin change hitting both PropertyChanged
        /// handlers independently) before applying. That correction was dropped when this method
        /// replaced the widget's apply block in Faza C3 - restored here, HasValue-aware so it only
        /// corrects pairs this profile actually configured. Send order matches the original: disable
        /// the losing side of a conflict (RIS, then AntiLag/Boost) before the winning side (RSR, then
        /// Chill), so the driver never briefly sees both enabled.
        /// </summary>
        // [2.0 rebuild - AC/DC persistence] isOnAC defaults to true only as a safety fallback for
        // any future caller that doesn't have a real power-state reading handy. All FOUR profile-
        // apply call sites (RestoreGlobalProfileSettings, CurrentProfile_PropertyChanged,
        // PerGameProfile_PropertyChanged, RunningGame_PropertyChanged) now pass IsCurrentlyOnAC
        // explicitly - found in an independent audit 2026-07-19 that they used to always read the
        // base (AC) fields regardless of actual power state, so e.g. launching a game while
        // already on battery applied the AC-side AMD settings instead of the configured DC
        // overrides (no AC/DC transition event fires to correct it, since the power state didn't
        // change). Program.PowerSourceHandler.cs's real AC/DC reapply also passes the real state.
        // [2.0 rebuild - profile-system consolidation] onlyFields narrows which of the 11 AMD
        // fields actually get pushed to ADLX - null (the default, used by every full-reapply
        // trigger site) means "apply all 11", matching the previous unconditional behavior. A
        // single-field edit (ApplyProfileFieldIntent) passes the edited field + its conflict
        // partner(s) here instead, so an unrelated widget echo/edit can no longer cascade into
        // reapplying (and potentially re-flipping) all 11 fields - this was the mechanism that let
        // a widget-side ComboBox echo bug cascade into "GoTweaks turns Boost/RSR on by itself." The
        // conflict computation and persistence logic below is UNCHANGED - only which resolved
        // values reach amdManager.X.SetValue(...) narrows.
        private static readonly Dictionary<string, string[]> AmdConflictPartners = new Dictionary<string, string[]>
        {
            ["RadeonSuperResolution"] = new[] { "ImageSharpening" },
            ["ImageSharpening"] = new[] { "RadeonSuperResolution" },
            ["RadeonChill"] = new[] { "RadeonAntiLag", "RadeonBoost" },
            ["RadeonAntiLag"] = new[] { "RadeonChill" },
            ["RadeonBoost"] = new[] { "RadeonChill" },
            ["FluidMotionFrames"] = new[] { "RadeonAntiLag" },
        };

        private static void ApplyAMDFeaturesFromProfile(Shared.Data.GameProfile profile, bool isOnAC = true, HashSet<string> onlyFields = null)
        {
            bool? fluidMotionFrames = isOnAC ? profile.FluidMotionFrames : (profile.FluidMotionFrames_DC ?? profile.FluidMotionFrames);
            if (fluidMotionFrames.HasValue && (onlyFields == null || onlyFields.Contains("FluidMotionFrames")))
                amdManager.AMDFluidMotionFrameEnabled.SetValue(fluidMotionFrames.Value);

            // RSR and RIS are mutually exclusive - if both are configured true, prefer RSR.
            bool? rsr = isOnAC ? profile.RadeonSuperResolution : (profile.RadeonSuperResolution_DC ?? profile.RadeonSuperResolution);
            bool? ris = isOnAC ? profile.ImageSharpening : (profile.ImageSharpening_DC ?? profile.ImageSharpening);
            int? risSharpness = isOnAC ? profile.ImageSharpeningSharpness : (profile.ImageSharpeningSharpness_DC ?? profile.ImageSharpeningSharpness);
            int? rsrSharpness = isOnAC ? profile.RadeonSuperResolutionSharpness : (profile.RadeonSuperResolutionSharpness_DC ?? profile.RadeonSuperResolutionSharpness);
            if (rsr == true && ris == true)
            {
                Logger.Warn("Profile has both RSR and RIS enabled - disabling RIS (mutually exclusive)");
                ris = false;
                // [persistence fix, found on-device 2026-07-20] The correction below ONLY updated
                // the local `ris` variable used for the hardware apply a few lines down - the
                // PERSISTED profile (GameProfile.ImageSharpening's own property setter, which
                // auto-queues a debounced disk Save()) never got the correction, so the conflict
                // was never actually resolved on disk. Every later reapply (there are many: this
                // method runs on helper startup, on every AC/DC transition, on every per-game
                // profile switch, and after every single unrelated AMD field edit via
                // ApplyProfileFieldIntent) re-read the SAME stale conflicting pair, re-logged this
                // same warning, and re-forced RIS off again - permanently overriding whatever the
                // user had actually just set for RIS, forever, until this line existed. Writing
                // through the profile's own setter (not a disconnected local bool) queues a real
                // write via GameProfile.Save() (PendingWrites is keyed by file Path, not by struct
                // instance, so this persists correctly even though `profile` here is a
                // pass-by-value copy).
                if (isOnAC) profile.ImageSharpening = false; else profile.ImageSharpening_DC = false;
            }
            if (ris.HasValue && (onlyFields == null || onlyFields.Contains("ImageSharpening")))
                amdManager.AMDImageSharpeningEnabled.SetValue(ris.Value);
            if (risSharpness.HasValue && (onlyFields == null || onlyFields.Contains("ImageSharpeningSharpness")))
                amdManager.AMDImageSharpeningSharpness.SetValue(risSharpness.Value);
            if (rsr.HasValue && (onlyFields == null || onlyFields.Contains("RadeonSuperResolution")))
                amdManager.AMDRadeonSuperResolutionEnabled.SetValue(rsr.Value);
            if (rsrSharpness.HasValue && (onlyFields == null || onlyFields.Contains("RadeonSuperResolutionSharpness")))
                amdManager.AMDRadeonSuperResolutionSharpness.SetValue(rsrSharpness.Value);

            // Chill is mutually exclusive with Anti-Lag and Boost - if Chill is configured true
            // alongside either, disable Anti-Lag/Boost.
            bool? antiLag = isOnAC ? profile.RadeonAntiLag : (profile.RadeonAntiLag_DC ?? profile.RadeonAntiLag);
            bool? boost = isOnAC ? profile.RadeonBoost : (profile.RadeonBoost_DC ?? profile.RadeonBoost);
            bool? chill = isOnAC ? profile.RadeonChill : (profile.RadeonChill_DC ?? profile.RadeonChill);
            int? boostResolution = isOnAC ? profile.RadeonBoostResolution : (profile.RadeonBoostResolution_DC ?? profile.RadeonBoostResolution);
            int? chillMinFPS = isOnAC ? profile.RadeonChillMinFPS : (profile.RadeonChillMinFPS_DC ?? profile.RadeonChillMinFPS);
            int? chillMaxFPS = isOnAC ? profile.RadeonChillMaxFPS : (profile.RadeonChillMaxFPS_DC ?? profile.RadeonChillMaxFPS);
            if (chill == true && (antiLag == true || boost == true))
            {
                Logger.Warn("Profile has Chill with Anti-Lag/Boost enabled - disabling Anti-Lag and Boost (mutually exclusive)");
                antiLag = false;
                boost = false;
                // [persistence fix, found on-device 2026-07-20] Same gap as the RSR/RIS correction
                // above - `antiLag`/`boost` are local variables used only for the hardware apply
                // below; the PERSISTED profile never got the correction. Confirmed on-device:
                // global.xml held RadeonBoost=true and RadeonChill=true simultaneously - every
                // reapply (helper startup, every AC/DC transition, every profile switch, every
                // single AMD field edit) re-read this same stale pair, re-warned, and re-forced
                // Boost off again, silently undoing the user's actual Boost-enable every time -
                // this was the root cause of "enabling Boost does nothing". Must run BEFORE the
                // AFMF-forces-Anti-Lag override below, which stays apply-time-only/unpersisted by
                // design (see its own comment) - persisting here first means that override still
                // only ever touches the in-memory `antiLag` local, never the stored preference.
                if (isOnAC) { profile.RadeonAntiLag = false; profile.RadeonBoost = false; }
                else { profile.RadeonAntiLag_DC = false; profile.RadeonBoost_DC = false; }
            }
            // AFMF requires Anti-Lag - apply-time only, never persisted (see
            // TryApplyAmdProfileField's FluidMotionFrames branch). Takes precedence over the
            // Chill exclusion above: a real AMD driver requirement isn't optional the way the
            // Chill/Anti-Lag/Boost preference conflict is. When FluidMotionFrames is false again,
            // this resolves back to the profile's own untouched Anti-Lag preference.
            if (fluidMotionFrames == true)
                antiLag = true;
            if (antiLag.HasValue && (onlyFields == null || onlyFields.Contains("RadeonAntiLag")))
                amdManager.AMDRadeonAntiLagEnabled.SetValue(antiLag.Value);
            if (boost.HasValue && (onlyFields == null || onlyFields.Contains("RadeonBoost")))
                amdManager.AMDRadeonBoostEnabled.SetValue(boost.Value);
            if (boostResolution.HasValue && (onlyFields == null || onlyFields.Contains("RadeonBoostResolution")))
                amdManager.AMDRadeonBoostResolution.SetValue(boostResolution.Value);
            if (chill.HasValue && (onlyFields == null || onlyFields.Contains("RadeonChill")))
                amdManager.AMDRadeonChillEnabled.SetValue(chill.Value);
            if (chillMinFPS.HasValue && (onlyFields == null || onlyFields.Contains("RadeonChillMinFPS")))
                amdManager.AMDRadeonChillMinFPS.SetValue(chillMinFPS.Value);
            if (chillMaxFPS.HasValue && (onlyFields == null || onlyFields.Contains("RadeonChillMaxFPS")))
                amdManager.AMDRadeonChillMaxFPS.SetValue(chillMaxFPS.Value);
        }

        private static void CurrentProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Use lock to ensure atomic profile application and prevent interleaved settings
            // from rapid game switches (Game A → Game B → Game A)
            lock (profileApplicationLock)
            {
                // Prevent reentrant profile handling that can cause race conditions
                if (isApplyingProfile)
                {
                    Logger.Debug("Skipping CurrentProfile_PropertyChanged - already applying profile");
                    return;
                }

                // [root-cause fix, 2026-07-20] See isRoutingProfileSave's doc comment (Program.cs).
                // A RouteProfileSave-driven mutation of CurrentProfile is a pure persistence write
                // for a field that was already applied to hardware - reapplying all 11 AMD fields
                // here in response is redundant at best and, at worst, a race against a concurrent
                // externally-detected change to a DIFFERENT field. Confirmed on-device: this exact
                // chain fired from AMDRadeonSuperResolutionSharpness_PropertyChanged after an
                // Adrenalin-side RSR sharpness change was correctly detected and synced.
                if (isRoutingProfileSave)
                {
                    Logger.Debug("Skipping CurrentProfile_PropertyChanged - triggered by RouteProfileSave's own persistence write");
                    return;
                }

                if (profileManager.CurrentProfile.Use || profileManager.CurrentProfile.IsGlobalProfile)
                {
                    try
                    {
                        isApplyingProfile = true;
                        Logger.Info($"Profile changed to {profileManager.CurrentProfile.GameId.Name}, apply it.");

                        // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
                        // 2026-07-19: this whole block used to read ONLY the base (AC) fields off
                        // profileManager.CurrentProfile, regardless of the actual live power state -
                        // same gap as RestoreGlobalProfileSettings/RunningGame_PropertyChanged, fixed
                        // the same way.
                        // [comment corrected, round 11 re-audit] This method's BODY is reachable in
                        // practice only via the direct, non-event call from SystemManager's
                        // sleep/resume handler (Program.PropertyHandlers.cs) - all 3 sites that call
                        // profileManager.CurrentProfile.SetValue(...), which raises this as a real
                        // PropertyChanged event too, already set isApplyingProfile=true first, so the
                        // top-of-method guard always blocks re-entry via the event path. The fix here
                        // is still real and exercised (sleep/resume needs it), just narrower in scope
                        // than "any CurrentProfile change" - don't assume this runs on every profile
                        // switch when reasoning about coverage elsewhere.
                        var cp = profileManager.CurrentProfile;
                        // [full-audit fix, 2026-07-20 — A#3] Resolve via ResolvesOnAc so a
                        // split-disabled profile resolves the base value even on battery.
                        bool isOnAC = ResolvesOnAc(cp);

                        // For per-game profiles, apply the saved LegionPerformanceMode if set
                        // This ensures the correct TDP mode is applied when the game is detected
                        if (cp.Use && legionManager != null)
                        {
                            int? savedMode = isOnAC ? cp.LegionPerformanceMode : (cp.LegionPerformanceMode_DC ?? cp.LegionPerformanceMode);
                            if (savedMode.HasValue)
                            {
                                int currentMode = legionManager.LegionPerformanceMode.Value;
                                if (currentMode != savedMode.Value)
                                {
                                    Logger.Info($"Switching to saved performance mode ({savedMode.Value}) for per-game profile (was {currentMode})");
                                    legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                                }
                            }
                            else
                            {
                                // Profile has no saved LegionPerformanceMode - auto-switch to Custom mode (255)
                                // if not already in Custom mode, so that custom TDP values can be applied
                                int currentMode = legionManager.LegionPerformanceMode.Value;
                                if (currentMode != 255)
                                {
                                    Logger.Info($"Per-game profile has no saved LegionPerformanceMode, auto-switching to Custom mode (was {currentMode}) to enable TDP control");
                                    legionManager.LegionPerformanceMode.SetValue(255);
                                }
                                else
                                {
                                    Logger.Debug($"Per-game profile has no saved LegionPerformanceMode, already in Custom mode");
                                }
                            }
                        }

                        // Use ApplyTDPAndCPUFromProfile so profile TDP takes precedence over
                        // in-flight widget messages - all settings applied atomically under lock
                        // to prevent cross-contamination.
                        ApplyTDPAndCPUFromProfile(cp, isOnAC);
                        ApplyOSPowerModeFromProfile(cp, isOnAC, "Applying current profile");
                        profileManager.PerGameProfile.SetValue(cp.Use);
                        ApplyDisplaySettingsFromProfile(cp, isOnAC);

                        if (amdManager != null)
                        {
                            ApplyAMDFeaturesFromProfile(cp, isOnAC);
                        }

                        // Apply Legion controller settings from profile (both global and per-game)
                        if (legionManager != null)
                        {
                            ApplyLegionControllerSettingsFromProfile(cp);
                        }
                    }
                    finally
                    {
                        profileSwitchTime = DateTime.UtcNow;
                        isApplyingProfile = false;
                    }
                }
                else
                {
                    Logger.Info($"Profile changed to {profileManager.CurrentProfile.GameId.Name} is not used.");
                }
            }
        }

        private static void PerGameProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // [race fix, 2.0 rebuild - profile-system consolidation] This is one of 2 full-reapply
            // trigger functions (with RunningGame_PropertyChanged) that used to only check the
            // unsynchronized isApplyingProfile bool with no lock at all - a genuine TOCTOU race
            // against every OTHER full-reapply site, which already takes profileApplicationLock
            // (CurrentProfile_PropertyChanged, ApplyProfileFieldIntent, SystemManager_
            // PowerSourceChanged): both could pass the "isApplyingProfile==false" check before
            // either set it true, then both proceed to write to the same hardware/profile fields
            // concurrently. lock is reentrant per-thread, so this is safe even if something in
            // this body re-enters via the same thread.
            lock (profileApplicationLock)
            {
            // Prevent reentrant profile handling
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping PerGameProfile_PropertyChanged - already applying profile");
                return;
            }

            try
            {
                isApplyingProfile = true;
                GameProfile gameProfile;
                if (profileManager.PerGameProfile)
                {
                    // Don't enable per-game profile if there's no valid game running
                    // This prevents race conditions when game closes and stale PerGameProfile=true arrives
                    if (!systemManager.RunningGame.Value.IsValid())
                    {
                        Logger.Info("Ignoring PerGameProfile=true - no valid game running (stale message)");
                        return;
                    }

                    if (!profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out gameProfile))
                    {
                        gameProfile = profileManager.AddNewProfile(systemManager.RunningGame.Value.GameId);
                    }
                    Logger.Info($"Enable per-game profile for {systemManager.RunningGame.Value.GameId}");
                    gameProfile.Use = true;

                    // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
                    // 2026-07-19: same gap as RestoreGlobalProfileSettings/CurrentProfile_PropertyChanged.
                    // [full-audit fix, 2026-07-20 — A#3] Resolve via ResolvesOnAc (split collapse).
                    bool isOnAC = ResolvesOnAc(gameProfile);

                    // Apply saved LegionPerformanceMode from game profile, or default to Custom (255) for new profiles.
                    // Previously this always switched to Custom, which overrode user-saved preset modes.
                    if (legionManager != null)
                    {
                        int? savedMode = isOnAC ? gameProfile.LegionPerformanceMode : (gameProfile.LegionPerformanceMode_DC ?? gameProfile.LegionPerformanceMode);
                        if (savedMode.HasValue && savedMode.Value > 0)
                        {
                            if (legionManager.LegionPerformanceMode.Value != savedMode.Value)
                            {
                                Logger.Info($"Applying saved performance mode ({savedMode.Value}) for per-game profile '{systemManager.RunningGame.Value.GameId.Name}'");
                                legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                            }
                            else
                            {
                                Logger.Debug($"Per-game profile already in saved mode ({savedMode.Value})");
                            }
                        }
                        else if (legionManager.LegionPerformanceMode.Value != 255)
                        {
                            Logger.Info("Switching to Custom TDP mode for new per-game profile (no saved mode)");
                            legionManager.LegionPerformanceMode.SetValue(255);
                        }
                    }

                    // Set current profile and apply settings from per-game profile.
                    // CurrentProfile_PropertyChanged is blocked by isApplyingProfile, so we
                    // must apply settings explicitly here (same pattern as RestoreGlobalProfileSettings).
                    // ForceSetValue, not SetValue - see RestoreGlobalProfileSettings' comment for why
                    // (GameId-only equality would silently no-op a same-GameId re-application).
                    profileManager.CurrentProfile.ForceSetValue(gameProfile);

                    // [2.0 rebuild - profile-system consolidation] This site used to only push
                    // the flat SPL via SetProfileValue, missing the Custom-mode SPPT/FPPT triplet
                    // push every other apply site already has (ApplyTDPAndCPUFromProfile includes
                    // it) - so enabling Per-Game Profile while in Custom TDP mode could leave the
                    // PREVIOUS profile's boost values on the hardware. Consolidating onto the
                    // shared function fixes this as a side effect.
                    ApplyTDPAndCPUFromProfile(gameProfile, isOnAC);
                    ApplyOSPowerModeFromProfile(gameProfile, isOnAC, "Enabling per-game profile");
                    ApplyDisplaySettingsFromProfile(gameProfile, isOnAC);

                    // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
                    // 2026-07-19: this branch (manually toggling "Per-Game Profile" on) never
                    // applied AMD Radeon features NOR Legion controller settings at all - a
                    // pre-existing gap, not introduced by the AC/DC sweep. The comment above
                    // ("same pattern as RestoreGlobalProfileSettings") states the intent but this
                    // never actually matched it. Without this, manually enabling per-game profile
                    // left AMD toggles and Legion button/gyro/vibration/lighting settings at
                    // whatever the PREVIOUS profile had, instead of restoring this game's saved
                    // values.
                    if (amdManager != null)
                    {
                        ApplyAMDFeaturesFromProfile(gameProfile, isOnAC);
                    }
                    if (legionManager != null)
                    {
                        ApplyLegionControllerSettingsFromProfile(gameProfile);
                    }
                }
                else
                {
                    // Don't disable per-game profile if a game with an active profile is still running
                    // This prevents race conditions when widget sends stale PerGameProfile=false
                    if (systemManager.RunningGame.Value.IsValid())
                    {
                        // Check if the current profile matches the running game (or a similar name variant)
                        var currentProfile = profileManager.CurrentProfile;
                        if (currentProfile != null && currentProfile != profileManager.GlobalProfile && currentProfile.Use)
                        {
                            var runningGameName = systemManager.RunningGame.Value.GameId.Name ?? "";
                            var profileName = currentProfile.GameId.Name ?? "";

                            // Check for exact match or name variants (e.g., "Game: Title" vs "Game Title")
                            bool isSameGame = string.Equals(runningGameName, profileName, StringComparison.OrdinalIgnoreCase) ||
                                              runningGameName.Replace(":", "").Replace("  ", " ").Trim().Equals(
                                                  profileName.Replace(":", "").Replace("  ", " ").Trim(),
                                                  StringComparison.OrdinalIgnoreCase);

                            if (isSameGame)
                            {
                                // Only ignore if this is likely a stale message from a recent game
                                // transition (within 2 seconds). Otherwise, honor the user's explicit
                                // toggle and restore global settings.
                                double secondsSinceSwitch = (DateTime.UtcNow - profileSwitchTime).TotalSeconds;
                                if (secondsSinceSwitch < 2)
                                {
                                    Logger.Info($"Ignoring stale PerGameProfile=false - game '{runningGameName}' still running, recent switch {secondsSinceSwitch:F1}s ago");
                                    return;
                                }
                                Logger.Info($"Honoring PerGameProfile=false while game '{runningGameName}' is running (user toggle, {secondsSinceSwitch:F1}s since last switch)");
                                // Fall through to disable per-game profile and restore global settings
                            }
                        }
                    }

                    if (profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out gameProfile))
                    {
                        gameProfile.Use = false;
                    }
                    // Restore global profile and apply all its settings (TDP, AutoTDP, etc.)
                    // CurrentProfile_PropertyChanged is blocked by isApplyingProfile, so we
                    // must apply settings explicitly here
                    RestoreGlobalProfileSettings();
                }
            }
            finally
            {
                profileSwitchTime = DateTime.UtcNow;
                isApplyingProfile = false;
            }
            }
        }

        private static void TDP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            // (e.g., writing game profile TDP to global profile during switch)
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping TDP_PropertyChanged - already applying profile (TDP={performanceManager.TDP})");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping TDP_PropertyChanged - in profile switch cooldown (TDP={performanceManager.TDP})");
                return;
            }

            // TEST [ProfileSaveFlags-TDP]: With ProfileSaveTDP unchecked in the widget, change
            // TDP while a per-game profile is active. Expect the change to land in GlobalProfile
            // (and the per-game TDP to remain whatever was saved before). Pre-flag baseline:
            // always wrote to CurrentProfile regardless of flag.
            // [2.0 rebuild - AC/DC live-edit fix] Writes to the base (AC) or _DC field depending
            // on the CURRENT power state (Program.PowerSourceHandler.cs's IsCurrentlyOnAC) - a
            // live edit while on battery must land in the DC override, not silently overwrite the
            // AC value. Found on-device 2026-07-18: every one of these handlers always wrote to
            // base regardless of actual power state, so a live edit (or an AMD-driver-forced
            // dependent change, e.g. AFMF forcing Anti-Lag on) silently corrupted the AC/DC split.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.TDP, "TDP",
                cur => { if (isOnAC) cur.TDP = performanceManager.TDP; else cur.TDP_DC = performanceManager.TDP; },
                glo => { if (isOnAC) glo.TDP = performanceManager.TDP; else glo.TDP_DC = performanceManager.TDP; });
        }

        // [2.0 rebuild - Faza C1] The remaining Performance-tab settings whose schema already
        // exists on both sides (GameProfile.cs field + a live helper Function/property of the
        // MATCHING type) and whose save-flag was already scaffolded in ProfileSaveFlagsState but
        // never actually wired to a PropertyChanged handler. Same RouteProfileSave pattern as TDP
        // above. (OSPowerMode/OverlayLevel were investigated and excluded - see
        // win32-2.0-migration.md: OSPowerMode's GameProfile field is a string left over from the
        // removed AC/DC Power Plan feature, type-mismatched with the live int power-slider
        // property; OverlayLevel is superseded by slice 1's helper-authoritative OSD for the RTSS
        // case and has no helper property at all for the AMD-overlay-cycling case. CPUAffinity was
        // fully unimplemented on both sides - deleted rather than excluded, see the dead-code sweep.)

        private static void FPSLimit_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping FPSLimit_PropertyChanged - already applying profile (FPSLimit={rtssManager.FPSLimit.Value})");
                return;
            }
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping FPSLimit_PropertyChanged - in profile switch cooldown (FPSLimit={rtssManager.FPSLimit.Value})");
                return;
            }

            // [2.0 rebuild - AC/DC live-edit fix] See TDP_PropertyChanged's comment above.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.FPSLimit, "FPSLimit",
                cur => { if (isOnAC) cur.FPSLimit = rtssManager.FPSLimit.Value; else cur.FPSLimit_DC = rtssManager.FPSLimit.Value; },
                glo => { if (isOnAC) glo.FPSLimit = rtssManager.FPSLimit.Value; else glo.FPSLimit_DC = rtssManager.FPSLimit.Value; });
        }

        private static void HDREnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping HDREnabled_PropertyChanged - already applying profile (HDREnabled={systemManager.HDREnabled.Value})");
                return;
            }
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping HDREnabled_PropertyChanged - in profile switch cooldown (HDREnabled={systemManager.HDREnabled.Value})");
                return;
            }

            // [2.0 rebuild - AC/DC live-edit fix] See TDP_PropertyChanged's comment above.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.HDR, "HDREnabled",
                cur => { if (isOnAC) cur.HDREnabled = systemManager.HDREnabled.Value; else cur.HDREnabled_DC = systemManager.HDREnabled.Value; },
                glo => { if (isOnAC) glo.HDREnabled = systemManager.HDREnabled.Value; else glo.HDREnabled_DC = systemManager.HDREnabled.Value; });
        }

        private static void Resolution_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping Resolution_PropertyChanged - already applying profile (Resolution={systemManager.Resolution.Value})");
                return;
            }
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping Resolution_PropertyChanged - in profile switch cooldown (Resolution={systemManager.Resolution.Value})");
                return;
            }

            // [2.0 rebuild - AC/DC live-edit fix] See TDP_PropertyChanged's comment above.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.Resolution, "Resolution",
                cur => { if (isOnAC) cur.Resolution = systemManager.Resolution.Value; else cur.Resolution_DC = systemManager.Resolution.Value; },
                glo => { if (isOnAC) glo.Resolution = systemManager.Resolution.Value; else glo.Resolution_DC = systemManager.Resolution.Value; });
        }

        private static void RefreshRate_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping RefreshRate_PropertyChanged - already applying profile (RefreshRate={systemManager.RefreshRate.Value})");
                return;
            }
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping RefreshRate_PropertyChanged - in profile switch cooldown (RefreshRate={systemManager.RefreshRate.Value})");
                return;
            }

            // [2.0 rebuild - AC/DC live-edit fix] See TDP_PropertyChanged's comment above.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.RefreshRate, "RefreshRate",
                cur => { if (isOnAC) cur.RefreshRate = systemManager.RefreshRate.Value; else cur.RefreshRate_DC = systemManager.RefreshRate.Value; },
                glo => { if (isOnAC) glo.RefreshRate = systemManager.RefreshRate.Value; else glo.RefreshRate_DC = systemManager.RefreshRate.Value; });
        }

        // [2.0 rebuild - Faza C2] The 6 AMD Radeon per-game feature toggles, all gated by the
        // single ProfileSaveFlagsState.AMDFeatures flag (mirrors GyroSettings gating 9 controller
        // properties with one flag). Same isApplyingProfile + cooldown guard shape as the others.

        // [2.0 rebuild - AC/DC live-edit fix] All 11 AMD handlers below now write to the base
        // (AC) or _DC field depending on IsCurrentlyOnAC, same reasoning as TDP_PropertyChanged's
        // comment further up.

        private static void AMDFluidMotionFrameEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // AFMF and LS frame generation are mutually exclusive. The helper owns this
            // decision so it is enforced even when the widget is suspended or a hotkey,
            // profile, or another client changes AFMF.
            if (amdManager.AMDFluidMotionFrameEnabled.Value
                && !string.Equals(losslessScalingManager?.LosslessScalingFrameGenType?.Value, "Off", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("AFMF enabled - helper disabling Lossless Scaling frame generation.");
                losslessScalingManager.LosslessScalingFrameGenType.SetValue("Off", DateTime.Now.Ticks);
            }

            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;

            // [full-audit fix, 2026-07-20 — A#8] Do NOT persist an AFMF-off that exists only because
            // LS frame generation is currently on (they are mutually exclusive and LS just forced
            // AFMF off). Persisting it would permanently clobber the user's real AFMF preference, so
            // turning LS FG back off could never restore AFMF. Mirrors the AFMF-forces-Anti-Lag
            // skip-save convention (that dependency is likewise apply-time-only, never persisted).
            // The user's preference stays in the profile and is restored in
            // LosslessScalingFrameGenType_PropertyChanged's "Off" branch below.
            bool afmfValue = amdManager.AMDFluidMotionFrameEnabled.Value;
            bool lsFrameGenOn = !string.Equals(losslessScalingManager?.LosslessScalingFrameGenType?.Value, "Off", StringComparison.OrdinalIgnoreCase);
            if (!afmfValue && lsFrameGenOn)
            {
                Logger.Info("Skipping FluidMotionFrames save - AFMF is forced off by active LS frame generation (not a user preference).");
                return;
            }

            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "FluidMotionFrames",
                cur => { if (isOnAC) cur.FluidMotionFrames = amdManager.AMDFluidMotionFrameEnabled.Value; else cur.FluidMotionFrames_DC = amdManager.AMDFluidMotionFrameEnabled.Value; },
                glo => { if (isOnAC) glo.FluidMotionFrames = amdManager.AMDFluidMotionFrameEnabled.Value; else glo.FluidMotionFrames_DC = amdManager.AMDFluidMotionFrameEnabled.Value; });
        }

        private static void LosslessScalingFrameGenType_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            bool lsFrameGenOn = !string.Equals(losslessScalingManager.LosslessScalingFrameGenType.Value, "Off", StringComparison.OrdinalIgnoreCase);
            if (lsFrameGenOn && amdManager.AMDFluidMotionFrameEnabled.Value)
            {
                Logger.Info("Lossless Scaling frame generation enabled - helper disabling AFMF.");
                amdManager.AMDFluidMotionFrameEnabled.SetValue(false, DateTime.Now.Ticks);
            }
            else if (!lsFrameGenOn)
            {
                // [full-audit fix, 2026-07-20 — A#8] LS frame generation turned off - restore the
                // user's persisted AFMF preference (which the AFMF save handler deliberately did NOT
                // overwrite while AFMF was forced off). Without this, AFMF stayed off forever after
                // ever enabling LS FG once.
                var cp = profileManager.CurrentProfile.Value;
                bool resolveOnAc = ResolvesOnAc(cp);
                bool? afmfPref = resolveOnAc ? cp.FluidMotionFrames : (cp.FluidMotionFrames_DC ?? cp.FluidMotionFrames);
                if (afmfPref == true && !amdManager.AMDFluidMotionFrameEnabled.Value)
                {
                    Logger.Info("LS frame generation disabled - restoring persisted AFMF preference (on).");
                    amdManager.AMDFluidMotionFrameEnabled.SetValue(true, DateTime.Now.Ticks);
                }
            }
        }

        private static void AMDRadeonSuperResolutionEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonSuperResolution",
                cur => { if (isOnAC) cur.RadeonSuperResolution = amdManager.AMDRadeonSuperResolutionEnabled.Value; else cur.RadeonSuperResolution_DC = amdManager.AMDRadeonSuperResolutionEnabled.Value; },
                glo => { if (isOnAC) glo.RadeonSuperResolution = amdManager.AMDRadeonSuperResolutionEnabled.Value; else glo.RadeonSuperResolution_DC = amdManager.AMDRadeonSuperResolutionEnabled.Value; });
        }

        private static void AMDRadeonSuperResolutionSharpness_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonSuperResolutionSharpness",
                cur => { if (isOnAC) cur.RadeonSuperResolutionSharpness = amdManager.AMDRadeonSuperResolutionSharpness.Value; else cur.RadeonSuperResolutionSharpness_DC = amdManager.AMDRadeonSuperResolutionSharpness.Value; },
                glo => { if (isOnAC) glo.RadeonSuperResolutionSharpness = amdManager.AMDRadeonSuperResolutionSharpness.Value; else glo.RadeonSuperResolutionSharpness_DC = amdManager.AMDRadeonSuperResolutionSharpness.Value; });
        }

        private static void AMDImageSharpeningEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "ImageSharpening",
                cur => { if (isOnAC) cur.ImageSharpening = amdManager.AMDImageSharpeningEnabled.Value; else cur.ImageSharpening_DC = amdManager.AMDImageSharpeningEnabled.Value; },
                glo => { if (isOnAC) glo.ImageSharpening = amdManager.AMDImageSharpeningEnabled.Value; else glo.ImageSharpening_DC = amdManager.AMDImageSharpeningEnabled.Value; });
        }

        private static void AMDImageSharpeningSharpness_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "ImageSharpeningSharpness",
                cur => { if (isOnAC) cur.ImageSharpeningSharpness = amdManager.AMDImageSharpeningSharpness.Value; else cur.ImageSharpeningSharpness_DC = amdManager.AMDImageSharpeningSharpness.Value; },
                glo => { if (isOnAC) glo.ImageSharpeningSharpness = amdManager.AMDImageSharpeningSharpness.Value; else glo.ImageSharpeningSharpness_DC = amdManager.AMDImageSharpeningSharpness.Value; });
        }

        private static void AMDRadeonAntiLagEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            // [AFMF-forces-AntiLag fix] AMDManager.cs's own AmdFluidMotionFrameEnabled handler
            // force-enables Anti-Lag whenever AFMF turns on (a real AMD driver requirement, not a
            // free user choice in that state) - that forced SetValue fires this same handler as a
            // side effect. Saving it as if it were an independent edit pollutes the persisted
            // Anti-Lag preference with a value that only reflects AFMF's requirement, not the
            // user's actual preference for when AFMF is off. Skip the save entirely while AFMF is
            // on; the widget greys out the Anti-Lag toggle in this state for the same reason
            // (GamingWidget.QuickSettings.Actions.cs's AMDFluidMotionFrameToggle_ProfileToggled).
            if (amdManager.AMDFluidMotionFrameEnabled.Value)
            {
                Logger.Debug("Skipping AMDRadeonAntiLagEnabled_PropertyChanged save - forced on by AFMF, not an independent choice");
                return;
            }
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonAntiLag",
                cur => { if (isOnAC) cur.RadeonAntiLag = amdManager.AMDRadeonAntiLagEnabled.Value; else cur.RadeonAntiLag_DC = amdManager.AMDRadeonAntiLagEnabled.Value; },
                glo => { if (isOnAC) glo.RadeonAntiLag = amdManager.AMDRadeonAntiLagEnabled.Value; else glo.RadeonAntiLag_DC = amdManager.AMDRadeonAntiLagEnabled.Value; });
        }

        private static void AMDRadeonBoostEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonBoost",
                cur => { if (isOnAC) cur.RadeonBoost = amdManager.AMDRadeonBoostEnabled.Value; else cur.RadeonBoost_DC = amdManager.AMDRadeonBoostEnabled.Value; },
                glo => { if (isOnAC) glo.RadeonBoost = amdManager.AMDRadeonBoostEnabled.Value; else glo.RadeonBoost_DC = amdManager.AMDRadeonBoostEnabled.Value; });
        }

        private static void AMDRadeonBoostResolution_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonBoostResolution",
                cur => { if (isOnAC) cur.RadeonBoostResolution = amdManager.AMDRadeonBoostResolution.Value; else cur.RadeonBoostResolution_DC = amdManager.AMDRadeonBoostResolution.Value; },
                glo => { if (isOnAC) glo.RadeonBoostResolution = amdManager.AMDRadeonBoostResolution.Value; else glo.RadeonBoostResolution_DC = amdManager.AMDRadeonBoostResolution.Value; });
        }

        private static void AMDRadeonChillEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonChill",
                cur => { if (isOnAC) cur.RadeonChill = amdManager.AMDRadeonChillEnabled.Value; else cur.RadeonChill_DC = amdManager.AMDRadeonChillEnabled.Value; },
                glo => { if (isOnAC) glo.RadeonChill = amdManager.AMDRadeonChillEnabled.Value; else glo.RadeonChill_DC = amdManager.AMDRadeonChillEnabled.Value; });
        }

        private static void AMDRadeonChillMinFPS_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonChillMinFPS",
                cur => { if (isOnAC) cur.RadeonChillMinFPS = amdManager.AMDRadeonChillMinFPS.Value; else cur.RadeonChillMinFPS_DC = amdManager.AMDRadeonChillMinFPS.Value; },
                glo => { if (isOnAC) glo.RadeonChillMinFPS = amdManager.AMDRadeonChillMinFPS.Value; else glo.RadeonChillMinFPS_DC = amdManager.AMDRadeonChillMinFPS.Value; });
        }

        private static void AMDRadeonChillMaxFPS_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile || IsInProfileSwitchCooldown()) return;
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "RadeonChillMaxFPS",
                cur => { if (isOnAC) cur.RadeonChillMaxFPS = amdManager.AMDRadeonChillMaxFPS.Value; else cur.RadeonChillMaxFPS_DC = amdManager.AMDRadeonChillMaxFPS.Value; },
                glo => { if (isOnAC) glo.RadeonChillMaxFPS = amdManager.AMDRadeonChillMaxFPS.Value; else glo.RadeonChillMaxFPS_DC = amdManager.AMDRadeonChillMaxFPS.Value; });
        }

        private static void RunningGame_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // #66: drive PresentMon subprocess lifecycle from the same RunningGame signal.
            // Wrap in try/catch in the caller so PresentMon faults never break profile flow.
            OnRunningGameChangedForPresentMon();

            // [race fix, 2.0 rebuild - profile-system consolidation] Same fix as
            // PerGameProfile_PropertyChanged - this full-reapply trigger function used to only
            // check the unsynchronized isApplyingProfile bool, with no lock, racing every OTHER
            // apply site that already takes profileApplicationLock.
            lock (profileApplicationLock)
            {
            // Prevent reentrant profile handling
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping RunningGame_PropertyChanged - already applying profile");
                return;
            }

            try
            {
                isApplyingProfile = true;
                if (systemManager.RunningGame.Value.IsValid())
                {
                    bool gameHasActiveProfile = false;
                    if (profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out var runningGameProfile))
                    {
                        if (runningGameProfile.Use)
                        {
                            Logger.Info($"Game {systemManager.RunningGame.GameId} has per-game profile in use.");
                            // ForceSetValue, not SetValue - see RestoreGlobalProfileSettings' comment for
                            // why (GameId-only equality would silently no-op a same-game re-fire, which
                            // this code's own surrounding comments document as a real, reachable case).
                            profileManager.CurrentProfile.ForceSetValue(runningGameProfile);
                            gameHasActiveProfile = true;

                            // Notify widget that per-game profile is active.
                            // The widget may not auto-enable (e.g., disabled preference stored locally),
                            // so the helper must assert this to keep both sides in sync.
                            profileManager.PerGameProfile.ForceSetValue(true);

                            // Apply all settings explicitly. CurrentProfile_PropertyChanged is blocked
                            // by isApplyingProfile, so we must apply here (same as PerGameProfile_PropertyChanged).
                            // [2.0 rebuild - AC/DC persistence follow-up] Found in an independent audit
                            // 2026-07-19: this whole block used to read ONLY the base (AC) fields off
                            // runningGameProfile, regardless of the actual live power state - so a game
                            // launching while genuinely on battery got the AC-side settings instead of
                            // the configured DC overrides. Same gap/fix shape as
                            // RestoreGlobalProfileSettings/CurrentProfile_PropertyChanged.
                            // [full-audit fix, 2026-07-20 — A#3] Resolve via ResolvesOnAc (split collapse).
                            bool isOnAC = ResolvesOnAc(runningGameProfile);
                            if (legionManager != null)
                            {
                                int? savedMode = isOnAC ? runningGameProfile.LegionPerformanceMode : (runningGameProfile.LegionPerformanceMode_DC ?? runningGameProfile.LegionPerformanceMode);
                                if (savedMode.HasValue && savedMode.Value > 0)
                                {
                                    if (legionManager.LegionPerformanceMode.Value != savedMode.Value)
                                    {
                                        Logger.Info($"Applying saved performance mode ({savedMode.Value}) for game '{systemManager.RunningGame.Value.GameId.Name}'");
                                        legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                                    }
                                }
                                else if (legionManager.LegionPerformanceMode.Value != 255)
                                {
                                    Logger.Info("Switching to Custom TDP mode for game profile (no saved mode)");
                                    legionManager.LegionPerformanceMode.SetValue(255);
                                }
                            }

                            ApplyTDPAndCPUFromProfile(runningGameProfile, isOnAC);
                            ApplyOSPowerModeFromProfile(runningGameProfile, isOnAC, "Applying running-game profile");
                            ApplyDisplaySettingsFromProfile(runningGameProfile, isOnAC);
                            // [2.0 rebuild - Faza C2] AMD toggles are nullable - only apply if this
                            // profile ever explicitly touched them (never force a default).
                            ApplyAMDFeaturesFromProfile(runningGameProfile, isOnAC);

                            if (legionManager != null)
                            {
                                ApplyLegionControllerSettingsFromProfile(runningGameProfile);
                            }

                            int loggedTdp = isOnAC ? runningGameProfile.TDP : (runningGameProfile.TDP_DC ?? runningGameProfile.TDP);
                            Logger.Info($"Applied per-game profile settings for {systemManager.RunningGame.Value.GameId.Name}: TDP={loggedTdp} ({(isOnAC ? "AC" : "DC")})");
                        }
                        else
                        {
                            Logger.Info($"Game {systemManager.RunningGame.GameId} has per-game profile but not in use.");
                        }
                    }
                    else
                    {
                        Logger.Info($"Game {systemManager.RunningGame.GameId} doesn't have per-game profile.");
                    }

                    // Only restore global if the current game doesn't have an active profile
                    // AND we're currently on a per-game profile from a previous game.
                    // Without the gameHasActiveProfile check, re-firing for the SAME game
                    // (e.g., foreground change) would incorrectly restore global and cause
                    // per-game AutoTDP settings to bleed into the global profile.
                    if (!gameHasActiveProfile && !profileManager.CurrentProfile.IsGlobalProfile)
                    {
                        Logger.Info($"Previous game had per-game profile active, restoring global profile for {systemManager.RunningGame.GameId}");
                        RestoreGlobalProfileSettings();
                    }

                    // Switch Lossless Scaling profile for the detected game
                    if (losslessScalingManager.LosslessScalingInstalled.Value)
                    {
                        var gameName = systemManager.RunningGame.Value.GameId.Name;
                        var gamePath = systemManager.RunningGame.Value.GameId.Path;
                        losslessScalingManager.SetCurrentGame(gameName, gamePath);
                    }
                }
                else
                {
                    Logger.Info($"Stopped playing game, use global profile instead.");
                    RestoreGlobalProfileSettings();

                    // Reset Lossless Scaling to Default profile when game stops
                    if (losslessScalingManager.LosslessScalingInstalled.Value)
                    {
                        losslessScalingManager.SetCurrentGame("Default", "");
                    }
                }
            }
            finally
            {
                profileSwitchTime = DateTime.UtcNow;
                isApplyingProfile = false;
            }
            }
        }

    }
}
