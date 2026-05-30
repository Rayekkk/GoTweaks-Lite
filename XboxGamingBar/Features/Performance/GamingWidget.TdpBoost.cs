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
        private void TDPBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (TDPBoostToggle == null) return;
            if (isApplyingHelperUpdate) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;
            // Skip during widget init / explicit profile loads. UWP ToggleSwitch.IsOn=X queues the
            // Toggled event on the dispatcher, so a LoadProfileSettings that sets IsOn=profile.value
            // sees Toggled fire AFTER its isLoadingProfile=false reset — and at that moment the
            // slider Values are still the XAML defaults (1/3) because their ValueChanged is also
            // queued behind this one. Without these guards the Toggled handler then reads 1/3 from
            // the still-stale sliders and saves 1/3 to the profile, clobbering the just-loaded
            // per-profile boost deltas (verified on 2429: user set 2/10, killed helper, the
            // reactivation Toggled handler wrote 1/3 over the saved 2/10).
            if (isInitialSync || isLoadingProfile || isSwitchingProfile) return;

            Logger.Info($"TDP Boost toggled to: {TDPBoostToggle.IsOn}");

            // Send to helper
            tdpBoostEnabled?.SetValue(TDPBoostToggle.IsOn);

            // Save to local settings for persistence across widget restarts
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostEnabled"] = TDPBoostToggle.IsOn;

            // When enabling boost, also send current SPPT/FPPT values to ensure helper has them
            if (TDPBoostToggle.IsOn)
            {
                int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                tdpBoostSPPT?.SetValue(spptBoost);
                tdpBoostFPPT?.SetValue(fpptBoost);
                Logger.Info($"TDP Boost enabled - sent SPPT={spptBoost}W, FPPT={fpptBoost}W to helper");
            }

            // Save to profile if not loading
            if (!isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void TDPBoostSPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostSPPTSlider == null) return;
            // Block writes during widget construction / initial helper sync / explicit profile
            // load. XAML's Slider Value="1" default fires ValueChanged at construction time
            // BEFORE LoadTDPBoostSettings / LoadProfileSettings have run; without this guard
            // every reactivation saves the XAML default (1) to the active profile, clobbering
            // the user's last value. Matches the AutoTDP / OS-power-mode handlers' pattern.
            if (isInitialSync || isApplyingHelperUpdate || isLoadingProfile) return;

            int spptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost SPPT changed to: {spptBoost}W");

            if (TDPBoostSPPTValue != null)
            {
                TDPBoostSPPTValue.Text = $"+{spptBoost}W";
            }
            UpdateTDPBoostCardActiveText();

            // Send to helper
            tdpBoostSPPT?.SetValue(spptBoost);

            // Persist on the active profile so the choice is per-game; LocalSettings still
            // keeps the last-edited value so the UI can rehydrate the slider on widget reload
            // before the profile sync arrives.
            SaveCurrentTDPBoostDeltasToProfile();
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostSPPT"] = spptBoost;
        }

        private void TDPBoostFPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostFPPTSlider == null) return;
            // Same construction-time / sync-time gating as the SPPT handler above (the XAML
            // Value="3" default would otherwise stomp the user's per-profile FPPT delta).
            if (isInitialSync || isApplyingHelperUpdate || isLoadingProfile) return;

            int fpptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost FPPT changed to: {fpptBoost}W");

            if (TDPBoostFPPTValue != null)
            {
                TDPBoostFPPTValue.Text = $"+{fpptBoost}W";
            }
            UpdateTDPBoostCardActiveText();

            // Send to helper
            tdpBoostFPPT?.SetValue(fpptBoost);

            // Persist on the active profile (per-game boost) + last-edited cache.
            SaveCurrentTDPBoostDeltasToProfile();
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostFPPT"] = fpptBoost;
        }

        /// <summary>
        /// Refresh the TDP card's "TDP Boost active: Fast +XW, Peak +YW" indicator from the
        /// current SPPT/FPPT slider values. Called from the slider value-changed handlers and
        /// from LoadTDPBoostSettings so the card text stays in sync with the inline sliders.
        /// </summary>
        private void UpdateTDPBoostCardActiveText()
        {
            if (TDPBoostCardActiveText == null) return;
            int spptDelta = TDPBoostSPPTSlider != null ? (int)Math.Round(TDPBoostSPPTSlider.Value) : 1;
            int fpptDelta = TDPBoostFPPTSlider != null ? (int)Math.Round(TDPBoostFPPTSlider.Value) : 3;
            TDPBoostCardActiveText.Text = $"TDP Boost active: Fast +{spptDelta}W, Peak +{fpptDelta}W";
        }

        /// <summary>
        /// Write the current SPPT/FPPT boost deltas into the active profile in memory so the next
        /// profile save persists them. The profile save itself is debounced by the existing
        /// SaveCurrentSettingsToProfile machinery — no immediate disk hit needed here.
        /// </summary>
        private void SaveCurrentTDPBoostDeltasToProfile()
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostSPPTSlider == null || TDPBoostFPPTSlider == null) return;
            try
            {
                int sppt = (int)Math.Round(TDPBoostSPPTSlider.Value);
                int fppt = (int)Math.Round(TDPBoostFPPTSlider.Value);
                // GetProfile returns a reference to the in-memory PerformanceProfile (class, not
                // struct), so direct mutation is enough — SaveCurrentSettingsToProfile pushes the
                // updated values to disk and to the helper via the existing profile-save path.
                var profile = GetProfile(currentProfileName);
                profile.TDPBoostSPPT = sppt;
                profile.TDPBoostFPPT = fppt;
                SaveCurrentSettingsToProfile(currentProfileName);
            }
            catch (Exception ex) { Logger.Debug($"SaveCurrentTDPBoostDeltasToProfile failed: {ex.Message}"); }
        }

        private void LoadTDPBoostSettings()
        {
            // SPPT / FPPT deltas are now per-profile (PerformanceProfile.TDPBoostSPPT / FPPT). The
            // previous global UWP LocalSettings load path here was racing the user's last edit:
            // Slider.Value = X queues ValueChanged on the dispatcher, which fires AFTER our
            // isLoadingTDPBoostSettings finally block resets the flag — so the deferred event
            // wrote the stale UWP default back to the profile, clobbering the user's intent
            // (verified on 2428: user set 2/10, Kill GoTweaks, LocalSettings still had stale 1/3,
            // post-reactivation load reset sliders to 1/3 and the deferred handler saved 1/3 to
            // global.xml). Leaving this function as a no-op makes the profile the sole source of
            // truth for the deltas; LoadProfileSettings populates the sliders from disk and the
            // value-changed handler only fires on real user edits, persisting per-profile.
            isLoadingTDPBoostSettings = false;
        }

        private void TDPBoostEnabled_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // NOTE: This callback is triggered when helper syncs TDPBoostEnabled.
            // We do NOT update the toggle from this callback because:
            // 1. The widget (LocalSettings) is the source of truth for this setting
            // 2. The helper doesn't persist TDPBoostEnabled, so it always sends False on fresh start
            // 3. Profile loading explicitly sets the toggle in LoadProfileSettings()
            //
            // If boost is enabled, we just need to ensure SPPT/FPPT values are sent to helper.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (TDPBoostToggle == null || tdpBoostEnabled == null) return;

                // Only send SPPT/FPPT to helper if boost is currently enabled in the UI
                // (regardless of what the helper sent us)
                if (TDPBoostToggle.IsOn)
                {
                    int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                    int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                    tdpBoostSPPT?.SetValue(spptBoost);
                    tdpBoostFPPT?.SetValue(fpptBoost);
                    Logger.Debug($"TDP Boost PropertyChanged - ensuring SPPT={spptBoost}W, FPPT={fpptBoost}W sent to helper");
                }
            });
        }

    }
}
