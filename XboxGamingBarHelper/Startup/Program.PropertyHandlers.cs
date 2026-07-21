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

        private static void CPUState_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - in profile switch cooldown");
                return;
            }

            // TEST [ProfileSaveFlags-CPUState]: With ProfileSaveCPUState unchecked, change
            // Min/Max CPU State sliders in-game. Verify the change goes to GlobalProfile, not
            // the per-game profile. Pre-flag baseline: always wrote to CurrentProfile.
            // [2.0 rebuild - AC/DC live-edit fix] Writes to the base (AC) or _DC field depending
            // on the CURRENT power state - a live edit while on battery must land in the DC
            // override, not silently overwrite the AC value (found on-device 2026-07-18: a live
            // AMD toggle edit was corrupting the AC/DC split because every one of these handlers
            // always wrote to base regardless of actual power state).
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.CPUState, "CPUState",
                cur => { if (isOnAC) { cur.MaxCPUState = powerManager.MaxCPUState.Value; cur.MinCPUState = powerManager.MinCPUState.Value; } else { cur.MaxCPUState_DC = powerManager.MaxCPUState.Value; cur.MinCPUState_DC = powerManager.MinCPUState.Value; } },
                glo => { if (isOnAC) { glo.MaxCPUState = powerManager.MaxCPUState.Value; glo.MinCPUState = powerManager.MinCPUState.Value; } else { glo.MaxCPUState_DC = powerManager.MaxCPUState.Value; glo.MinCPUState_DC = powerManager.MinCPUState.Value; } });
        }

        private static void SystemManager_ResumeFromSleep(object sender)
        {
            Logger.Info("System resumed from sleep/hibernation, refreshing hardware sensors and re-applying profile.");

            // Reset RTSS OSD connection (can become stale after hibernation, causing frozen OSD values)
            rtssManager?.ResetRTSSConnection();

            // Force refresh hardware sensors (battery values can be stale after hibernation)
            performanceManager?.ForceRefreshHardware();

            // Rebuild the EC fan override path if the PawnIO handle died during
            // sleep — otherwise the custom fan curve silently stops applying
            // until three tick-level write failures trigger the self-heal.
            legionManager?.RecoverEcFanOverrideAfterResume();

            // Re-arm the idle-to-hibernate monitor so a fresh sleep/hibernate cycle doesn't
            // immediately re-trigger on stale pre-sleep idle timestamps.
            ResetHibernateTimeoutAfterResume();

            // Re-apply current profile settings (TDP, CPU boost, EPP, CPU state)
            CurrentProfile_PropertyChanged(sender, null);
        }

        // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
        //private static void GPUClock_PropertyChanged(object sender, PropertyChangedEventArgs e)
        //{
        //    // GPU Clock is saved per-profile
        //    // Note: Profiles would need GPUClockMin/Max properties added to support per-game GPU clocks
        //    Logger.Info($"GPU Clock settings changed: Enabled={powerManager.LimitGPUClock}, Min={powerManager.GPUClockMin}, Max={powerManager.GPUClockMax}");
        //}

        private static void CPUBoost_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - in profile switch cooldown");
                return;
            }

            // TEST [ProfileSaveFlags-CPUBoost]: With ProfileSaveCPUBoost unchecked, toggle
            // CPU Boost in-game. Verify the change goes to GlobalProfile, not the per-game
            // profile. Pre-flag baseline: always wrote to CurrentProfile.
            // [2.0 rebuild - AC/DC live-edit fix] See CPUState_PropertyChanged's comment above.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.CPUBoost, "CPUBoost",
                cur => { if (isOnAC) cur.CPUBoost = powerManager.CPUBoost; else cur.CPUBoost_DC = powerManager.CPUBoost; },
                glo => { if (isOnAC) glo.CPUBoost = powerManager.CPUBoost; else glo.CPUBoost_DC = powerManager.CPUBoost; });
        }

        private static void CPUEPP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUEPP_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUEPP_PropertyChanged - in profile switch cooldown");
                return;
            }

            // TEST [ProfileSaveFlags-CPUEPP]: With ProfileSaveCPUEPP unchecked, change CPU EPP
            // in-game. Verify the change goes to GlobalProfile, not the per-game profile.
            // Pre-flag baseline: always wrote to CurrentProfile.
            // [2.0 rebuild - AC/DC live-edit fix] See CPUState_PropertyChanged's comment above.
            bool isOnAC = ResolvesOnAc(profileManager.CurrentProfile.Value); // [A#3] resolve/write base when split off
            RouteProfileSave(ProfileSaveFlagsState.CPUEPP, "CPUEPP",
                cur => { if (isOnAC) cur.CPUEPP = powerManager.CPUEPP; else cur.CPUEPP_DC = powerManager.CPUEPP; },
                glo => { if (isOnAC) glo.CPUEPP = powerManager.CPUEPP; else glo.CPUEPP_DC = powerManager.CPUEPP; });
        }

    }
}
