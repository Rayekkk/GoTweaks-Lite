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

        private void LoadProfileCustomizationSettings()
        {
            // Functional save flags are helper-owned. Until its confirmed snapshot arrives,
            // render the documented defaults without consulting widget LocalSettings.
            ApplyProfileSaveFlagsSnapshot(null);
        }

        private void ApplyProfileSaveFlagsSnapshot(string configJson)
        {
            isLoadingProfileSettings = true;
            try
            {
                var flags = string.IsNullOrEmpty(configJson) ? null : Windows.Data.Json.JsonObject.Parse(configJson);
                bool GetFlag(string key, bool fallback) => flags != null && flags.TryGetValue(key, out var value)
                    ? value.GetBoolean() : fallback;

                _saveTDP = GetFlag("TDP", true);
                _saveCPUBoost = GetFlag("CPUBoost", true);
                _saveCPUEPP = GetFlag("CPUEPP", true);
                _saveCPUState = GetFlag("CPUState", true);
                _saveAMDFeatures = GetFlag("AMDFeatures", false);
                _saveFPSLimit = GetFlag("FPSLimit", true);
                _saveOSPowerMode = GetFlag("OSPowerMode", true);
                _saveHDR = GetFlag("HDR", false);
                _saveResolution = GetFlag("Resolution", false);
                _saveRefreshRate = GetFlag("RefreshRate", false);
                _saveOverlayLevel = GetFlag("OverlayLevel", false);
                _saveNintendoLayout = GetFlag("NintendoLayout", false);
                _saveVibration = GetFlag("Vibration", false);
                _saveLighting = GetFlag("Lighting", false);
                _saveButtonMappings = GetFlag("ButtonMappings", false);
                _saveGyroSettings = GetFlag("GyroSettings", false);

                if (ProfileSaveTDPCheckBox != null) ProfileSaveTDPCheckBox.IsChecked = _saveTDP;
                if (ProfileSaveCPUBoostCheckBox != null) ProfileSaveCPUBoostCheckBox.IsChecked = _saveCPUBoost;
                if (ProfileSaveCPUEPPCheckBox != null) ProfileSaveCPUEPPCheckBox.IsChecked = _saveCPUEPP;
                if (ProfileSaveCPUStateCheckBox != null) ProfileSaveCPUStateCheckBox.IsChecked = _saveCPUState;
                if (ProfileSaveAMDFeaturesCheckBox != null) ProfileSaveAMDFeaturesCheckBox.IsChecked = _saveAMDFeatures;
                if (ProfileSaveFPSLimitCheckBox != null) ProfileSaveFPSLimitCheckBox.IsChecked = _saveFPSLimit;
                if (ProfileSaveOSPowerModeCheckBox != null) ProfileSaveOSPowerModeCheckBox.IsChecked = _saveOSPowerMode;
                if (ProfileSaveHDRCheckBox != null) ProfileSaveHDRCheckBox.IsChecked = _saveHDR;
                if (ProfileSaveResolutionCheckBox != null) ProfileSaveResolutionCheckBox.IsChecked = _saveResolution;
                if (ProfileSaveRefreshRateCheckBox != null) ProfileSaveRefreshRateCheckBox.IsChecked = _saveRefreshRate;
                if (ProfileSaveOverlayLevelCheckBox != null) ProfileSaveOverlayLevelCheckBox.IsChecked = _saveOverlayLevel;
                if (ProfileSaveNintendoLayoutCheckBox != null) ProfileSaveNintendoLayoutCheckBox.IsChecked = _saveNintendoLayout;
                if (ProfileSaveVibrationCheckBox != null) ProfileSaveVibrationCheckBox.IsChecked = _saveVibration;
                if (ProfileSaveLightingCheckBox != null) ProfileSaveLightingCheckBox.IsChecked = _saveLighting;
                if (ProfileSaveButtonMappingsCheckBox != null) ProfileSaveButtonMappingsCheckBox.IsChecked = _saveButtonMappings;
                if (ProfileSaveGyroSettingsCheckBox != null) ProfileSaveGyroSettingsCheckBox.IsChecked = _saveGyroSettings;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to render helper ProfileSaveFlags snapshot: {ex.Message}");
            }
            finally
            {
                isLoadingProfileSettings = false;
            }
        }
        private void ProfileSettingsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingProfileSettings) return;

            // Update backing fields from UI checkboxes
            SyncProfileSettingsBackingFields();
            SendProfileSaveFlagsToHelper();
            // Re-render the profile tables immediately so the row visibility (which
            // categories are shown/hidden) reflects the new Save* selection without
            // requiring the widget to be closed and reopened.
            UpdateProfileDisplay();
            Logger.Info($"Profile customization settings updated");
        }

        /// <summary>
        /// Pushes the current per-setting save flags to the helper so it can route writes:
        /// true => write to CurrentProfile (per-game if active, else global), false => write to
        /// GlobalProfile regardless of the active profile. Sent on startup and on any checkbox
        /// change. Helper stores a snapshot and consults it from AutoTDP / Legion save handlers.
        /// </summary>
        internal async void SendProfileSaveFlagsToHelper()
        {
            try
            {
                if (!App.IsConnected) return;
                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.ProfileSaveFlags },
                    { "Content", BuildProfileSaveFlagsJson() },
                };
                var response = await App.SendMessageAsync(request);
                if (response != null && response.TryGetValue("Content", out object content) && content != null)
                    ApplyProfileSaveFlagsSnapshot(content.ToString());
                Logger.Info("ProfileSaveFlags confirmed by helper");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending ProfileSaveFlags: {ex.Message}");
            }
        }

        internal async Task RequestProfileSaveFlagsFromHelperAsync()
        {
            if (!App.IsConnected) return;
            try
            {
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Get },
                    { "Function", (int)Shared.Enums.Function.ProfileSaveFlags },
                });
                if (response != null && response.TryGetValue("Content", out object content) && content != null)
                    ApplyProfileSaveFlagsSnapshot(content.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get ProfileSaveFlags from helper: {ex.Message}");
            }
        }

        private string BuildProfileSaveFlagsJson()
        {
            var flags = new Windows.Data.Json.JsonObject();
            flags["TDP"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveTDP);
            flags["CPUBoost"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveCPUBoost);
            flags["CPUEPP"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveCPUEPP);
            flags["CPUState"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveCPUState);
            flags["AMDFeatures"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveAMDFeatures);
            flags["FPSLimit"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveFPSLimit);
            flags["OSPowerMode"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveOSPowerMode);
            flags["HDR"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveHDR);
            flags["Resolution"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveResolution);
            flags["RefreshRate"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveRefreshRate);
            flags["OverlayLevel"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveOverlayLevel);
            flags["NintendoLayout"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveNintendoLayout);
            flags["Vibration"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveVibration);
            flags["Lighting"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveLighting);
            flags["ButtonMappings"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveButtonMappings);
            flags["GyroSettings"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_saveGyroSettings);
            return flags.Stringify();
        }

        /// <summary>
        /// Sync backing fields from UI checkboxes. Called when checkboxes change.
        /// This ensures the backing fields are always in sync with the UI.
        /// </summary>
        private void SyncProfileSettingsBackingFields()
        {
            _saveTDP = ProfileSaveTDPCheckBox?.IsChecked ?? true;
            _saveCPUBoost = ProfileSaveCPUBoostCheckBox?.IsChecked ?? true;
            _saveCPUEPP = ProfileSaveCPUEPPCheckBox?.IsChecked ?? true;
            _saveCPUState = ProfileSaveCPUStateCheckBox?.IsChecked ?? true;
            _saveAMDFeatures = ProfileSaveAMDFeaturesCheckBox?.IsChecked ?? false;
            _saveFPSLimit = ProfileSaveFPSLimitCheckBox?.IsChecked ?? true;
            _saveOSPowerMode = ProfileSaveOSPowerModeCheckBox?.IsChecked ?? true;
            _saveHDR = ProfileSaveHDRCheckBox?.IsChecked ?? false;
            _saveResolution = ProfileSaveResolutionCheckBox?.IsChecked ?? false;
            _saveRefreshRate = ProfileSaveRefreshRateCheckBox?.IsChecked ?? false;
            _saveOverlayLevel = ProfileSaveOverlayLevelCheckBox?.IsChecked ?? false;
            _saveNintendoLayout = ProfileSaveNintendoLayoutCheckBox?.IsChecked ?? false;
            _saveVibration = ProfileSaveVibrationCheckBox?.IsChecked ?? false;
            _saveLighting = ProfileSaveLightingCheckBox?.IsChecked ?? false;
            _saveButtonMappings = ProfileSaveButtonMappingsCheckBox?.IsChecked ?? false;
            _saveGyroSettings = ProfileSaveGyroSettingsCheckBox?.IsChecked ?? false;
        }

    }
}
