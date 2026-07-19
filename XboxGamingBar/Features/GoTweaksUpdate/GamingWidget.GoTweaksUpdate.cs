using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Banner visibility is purely presentational and remains widget-local.
        private const string GoTweaksHideBannerKey   = "GoTweaksUpdate_HideBanner";

        // Helper-confirmed functional preferences. This widget copy is display cache only.
        private bool _helperGoTweaksCheckOnStart = true;
        private bool _helperDriverCheckOnStart = true;

        // Cached latest update payload so the banner can drive an install
        // without re-asking the helper (helper caches too, but this saves
        // a pipe round-trip).
        private string _goTweaksLatestVersion;
        private string _goTweaksDownloadUrl;
        private string _goTweaksReleasePageUrl;

        // Set while SyncUpdatePreferenceCheckboxes programmatically
        // assigns IsChecked on the five update-preference checkboxes (GoTweaks +
        // Lenovo driver updates). Their Checked/Unchecked handlers early-return
        // so rendering confirmed state cannot emit a new user intent.
        private bool _isLoadingUpdatePreferenceCheckboxes;

        /// <summary>
        /// Renders helper-confirmed functional startup policy and widget-local
        /// presentation preferences without firing user-intent handlers.
        /// </summary>
        private void SyncUpdatePreferenceCheckboxes()
        {
            _isLoadingUpdatePreferenceCheckboxes = true;
            try
            {
                if (GoTweaksUpdateOnStartCheckbox != null)
                    GoTweaksUpdateOnStartCheckbox.IsOn = GoTweaksCheckOnStart;
                if (GoTweaksHideBannerCheckbox != null)
                    GoTweaksHideBannerCheckbox.IsOn = GoTweaksHideBanner;
                if (DriverUpdatesUpdateOnStartCheckbox != null)
                    DriverUpdatesUpdateOnStartCheckbox.IsChecked = DriverUpdatesCheckOnStart;
                if (DriverUpdatesHideBannerCheckbox != null)
                    DriverUpdatesHideBannerCheckbox.IsChecked = DriverUpdatesHideBanner;
                if (DriverUpdatesShowUtilitiesCheckbox != null)
                    DriverUpdatesShowUtilitiesCheckbox.IsChecked = DriverUpdatesShowUtilities;
            }
            finally
            {
                _isLoadingUpdatePreferenceCheckboxes = false;
            }
        }

        private bool GoTweaksCheckOnStart
        {
            get => _helperGoTweaksCheckOnStart;
        }
        private bool GoTweaksHideBanner
        {
            get => GetBoolSetting(GoTweaksHideBannerKey, false);
            set => SetBoolSetting(GoTweaksHideBannerKey, value);
        }

        private async void GoTweaksUpdateOnStartCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUpdatePreferenceCheckboxes) return;
            if (GoTweaksUpdateOnStartCheckbox == null) return;
            bool on = GoTweaksUpdateOnStartCheckbox.IsOn;
            GoTweaksUpdateOnStartCheckbox.IsEnabled = false;
            // Forward to helper so its next startup honours the toggle. Mirrors
            // the DriverCheckOnStart path; helper persists it via
            // LocalSettingsHelper and reads synchronously before scheduling
            // its GitHub probe.
            try
            {
                if (!App.IsConnected) throw new InvalidOperationException("Helper is disconnected");
                var req = new ValueSet { { "SetGoTweaksCheckOnStart", on } };
                var response = await App.SendMessageAsync(req);
                if (!IsSuccessfulPipeAck(response))
                    throw new InvalidOperationException("Helper rejected the preference");
            }
            catch (Exception ex) { Logger.Warn($"SetGoTweaksCheckOnStart failed: {ex.Message}"); }
            await SyncUpdateCheckPreferencesFromHelperAsync();
            GoTweaksUpdateOnStartCheckbox.IsEnabled = true;
        }

        private static bool IsSuccessfulPipeAck(ValueSet response)
        {
            return response != null && response.TryGetValue("Success", out object value)
                   && value is bool success && success;
        }

        private async Task SyncUpdateCheckPreferencesFromHelperAsync()
        {
            if (!App.IsConnected)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, SyncUpdatePreferenceCheckboxes);
                return;
            }

            try
            {
                var response = await App.SendMessageAsync(new ValueSet
                {
                    { "GetUpdateCheckPreferences", true },
                });
                if (response == null)
                    throw new InvalidOperationException("Helper returned no preference snapshot");

                if (response.TryGetValue("DriverCheckOnStart", out object driverValue) && driverValue is bool driver)
                    _helperDriverCheckOnStart = driver;
                if (response.TryGetValue("GoTweaksCheckOnStart", out object appValue) && appValue is bool app)
                    _helperGoTweaksCheckOnStart = app;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to sync helper update preferences: {ex.Message}");
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, SyncUpdatePreferenceCheckboxes);
        }

        private void GoTweaksHideBannerCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUpdatePreferenceCheckboxes) return;
            if (GoTweaksHideBannerCheckbox == null) return;
            GoTweaksHideBanner = GoTweaksHideBannerCheckbox.IsOn;
            if (QuickGoTweaksUpdateTile != null && GoTweaksHideBanner)
            {
                QuickGoTweaksUpdateTile.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Called from the pipe-message handler when the helper pushes a
        /// startup or on-demand self-update result. Keeps the Quick-tab
        /// banner in sync and leaves the System tab's existing "Check for
        /// Update" flow unchanged — it has its own manual fetch and
        /// UpdateStatusText/UpdateButton for install.
        /// </summary>
        internal async void HandleGoTweaksUpdatePush(string payload)
        {
            try
            {
                if (!JsonObject.TryParse(payload, out var root)) return;

                bool isUpdate = root.TryGetValue("isUpdateAvailable", out var uv)
                                && uv.ValueType == JsonValueType.Boolean && uv.GetBoolean();
                string latest = JsonString(root, "latestVersion");
                string url = JsonString(root, "downloadUrl");
                string pageUrl = JsonString(root, "releasePageUrl");

                _goTweaksLatestVersion = latest;
                _goTweaksDownloadUrl = url;
                _goTweaksReleasePageUrl = pageUrl;

                bool hideBanner = GoTweaksHideBanner;
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (GoTweaksHideBannerCheckbox != null &&
                        GoTweaksHideBannerCheckbox.IsOn != hideBanner)
                        GoTweaksHideBannerCheckbox.IsOn = hideBanner;

                    bool showBanner = isUpdate && !string.IsNullOrWhiteSpace(url) && !hideBanner;
                    if (QuickGoTweaksUpdateTile != null)
                        QuickGoTweaksUpdateTile.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
                    if (QuickGoTweaksTitleText != null && isUpdate)
                        QuickGoTweaksTitleText.Text = $"GoTweaks Lite {latest} available";
                    if (QuickGoTweaksSubtitleText != null)
                        QuickGoTweaksSubtitleText.Text = "Tap to install";
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"HandleGoTweaksUpdatePush failed: {ex.Message}");
            }
        }

        private static string JsonString(JsonObject obj, string key)
        {
            if (obj.TryGetValue(key, out var v) && v.ValueType == JsonValueType.String)
                return v.GetString();
            return "";
        }

        private async void QuickGoTweaksUpdateTile_Click(object sender, RoutedEventArgs e)
        {
            await TriggerGoTweaksInstallAsync();
        }

        /// <summary>
        /// Sends the install-url pipe message. Helper downloads the signed
        /// .msixbundle and runs Add-AppxPackage via PowerShell. Status text
        /// updates happen against the Quick-tab banner subtitle since we no
        /// longer have a separate GoTweaks status label (the System tab
        /// Debug panel's UpdateStatusText is owned by the existing manual
        /// update flow and we don't want to collide with it).
        /// </summary>
        private async Task TriggerGoTweaksInstallAsync()
        {
            if (string.IsNullOrWhiteSpace(_goTweaksDownloadUrl))
            {
                if (QuickGoTweaksSubtitleText != null)
                    QuickGoTweaksSubtitleText.Text = "No download URL cached.";
                return;
            }
            if (QuickGoTweaksSubtitleText != null)
                QuickGoTweaksSubtitleText.Text = "Downloading GoTweaks update\u2026";
            try
            {
                if (!App.IsConnected)
                {
                    if (QuickGoTweaksSubtitleText != null)
                        QuickGoTweaksSubtitleText.Text = "Helper not connected.";
                    return;
                }
                var request = new ValueSet();
                request.Add("InstallGoTweaksUpdate", _goTweaksDownloadUrl);
                var response = await App.SendMessageAsync(request);
                string message = "Install started.";
                if (response != null && response.TryGetValue("GoTweaksUpdateInstallResult", out var payloadObj)
                    && payloadObj is string payload)
                {
                    if (JsonObject.TryParse(payload, out var root))
                    {
                        string msg = JsonString(root, "message");
                        if (!string.IsNullOrWhiteSpace(msg)) message = msg;
                    }
                }
                if (QuickGoTweaksSubtitleText != null)
                    QuickGoTweaksSubtitleText.Text = message;
            }
            catch (Exception ex)
            {
                Logger.Warn($"TriggerGoTweaksInstallAsync failed: {ex.Message}");
                if (QuickGoTweaksSubtitleText != null)
                    QuickGoTweaksSubtitleText.Text = $"Install failed: {ex.Message}";
            }
        }
    }
}
