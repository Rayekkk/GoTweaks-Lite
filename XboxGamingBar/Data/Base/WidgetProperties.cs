using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperties : FunctionalProperties
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public WidgetProperties(params FunctionalProperty[] inProperties) : base(inProperties) { }

        /// <summary>
        /// Batch sync all properties in a single message for much faster performance.
        /// Falls back to individual sync if batch fails.
        /// </summary>
        public async Task Sync()
        {
            var syncTimer = Stopwatch.StartNew();

            // Try batch sync first
            if (await TryBatchSync())
            {
                // Batch sync bypasses individual Sync() calls which enable controls.
                // Call OnBatchSyncCompleted() on each property to enable controls.
                foreach (var property in properties.Values)
                {
                    await property.OnBatchSyncCompleted();
                }

                syncTimer.Stop();
                Logger.Info($"[TIMING] Batch sync {properties.Count} properties: {syncTimer.ElapsedMilliseconds}ms");
                return;
            }

            // Fallback to individual sync
            Logger.Warn("Batch sync failed, falling back to individual sync");
            int count = 0;
            foreach (var property in properties)
            {
                await property.Value.Sync();
                count++;
            }
            syncTimer.Stop();
            Logger.Info($"[TIMING] Individual sync {count} properties: {syncTimer.ElapsedMilliseconds}ms ({syncTimer.ElapsedMilliseconds / Math.Max(1, count)}ms avg)");
        }

        // Properties that should NEVER be synced from helper - widget is always the source of truth.
        // The helper applies values to hardware but doesn't persist them across restarts,
        // so the widget (which loads from controller profiles) is authoritative.
        private static readonly HashSet<Function> NeverSyncFromHelper = new HashSet<Function>
        {
            // [2.0 rebuild - slice 1] Function.OSD REMOVED: the helper is now the source of
            // truth for OSD level. It already persists it durably (Settings.Default.OSDLevel,
            // loaded at startup, saved on every change) and pushes changes to the widget, so
            // the widget must ACCEPT the synced value instead of guarding its own LocalSettings
            // copy. The widget no longer loads/saves PerformanceOverlayLevel to LocalSettings
            // (see GamingWidget.PerformanceOverlay.cs). Helper push -> osd property ->
            // PerformanceOverlaySlider -> ValueChanged -> ComboBox keeps the UI in sync.
            // [2.0 rebuild - slice 6] Lighting REMOVED: helper-authoritative. The helper persists
            // all lighting fields via RouteProfileSave (Lighting save-flag, §29; global-persistence
            // bug fixed) and applies+pushes them at startup (now before BatchGet) / game switch. The
            // bound properties reflect helper pushes into the mode combobox / color picker /
            // brightness+speed sliders / power-light toggle; every save handler
            // (ControllerSettingChanged / ControllerSliderSettingChanged) is already guarded by
            // isApplyingHelperUpdate, and the view updates (preview, visibility) correctly follow.
            // Widget no longer seeds lighting (ApplyControllerProfile + the seed-path
            // SendLightingToHelper calls); user edits still send via ApplyControllerSettingChange.
            // [2.0 rebuild - slice 4] Vibration REMOVED: helper-authoritative. The old comment here
            // ("helper does NOT persist across restarts") is STALE - since §29 the helper persists
            // LegionVibration/Mode into its profile via RouteProfileSave (per-game if the Vibration
            // save-flag is on, else global) and applies+pushes them at startup / game switch
            // (ApplyLegionControllerSettingsFromProfile). The widget now reflects the helper's value
            // (bound comboboxes; the SelectionChanged->ControllerSettingChanged->ApplyControllerSettingChange
            // save/send is already guarded by isApplyingHelperUpdate) and no longer seeds them.

            // ── NeverSyncFromHelper is now EMPTY (2.0 rebuild complete for controller settings) ──
            // Every controller-profile setting has migrated to helper-authoritative. This HashSet is
            // kept (rather than deleted) as the documented mechanism + history for the migration.
            //
            // DELIBERATELY EXCLUDED (do NOT add): Function.LegionDesktopControls and
            // Function.LegionJoystickAsMouseMode. Those two are toggled by HOTKEY on the helper and
            // pushed back to the widget (Program.HotkeyHandlers.cs ForceSetValue) so the UI reflects
            // the hardware-button state — they are genuinely bidirectional and MUST keep syncing from
            // the helper. (Their defaults are the disabled state, so the startup clobber is benign.)
            // [2.0 rebuild - slice 7] The eight Y1-Page button remaps REMOVED: helper-authoritative.
            // The helper persists each mapping via RouteProfileSave (buttons save-flag, §29) and
            // applies+pushes them at startup (before BatchGet) / game switch
            // (ApplyLegionControllerSettingsFromProfile). The widget reflects the pushed JSON by
            // rebuilding the composite per-button UI in ApplyButtonMappingFromHelper
            // (LegionButtonMappingProperty.OnValueSyncedFromHelper). User edits still send via
            // SendButtonMappingsToHelper; the widget no longer seeds them in ApplyControllerProfile.
            // [2.0 rebuild - slice 8] LegionGamepadButtonMapping (the arbitrary face/D-pad/stick-click
            // remap dict, written only by the Nintendo-layout and Desktop-Controls presets - no
            // dedicated UI exists for it since §29 removed the standalone "Gamepad Buttons" section)
            // + LegionNintendoLayout (bool toggle) REMOVED: helper-authoritative, same RouteProfileSave
            // pattern. LegionGamepadMappingProperty.OnValueSyncedFromHelper deserializes a helper push
            // straight into the in-memory gamepadButtonMappings dict (no UI to rebuild). Nintendo's
            // toggle is a plain auto-bound WidgetToggleProperty, and a helper-driven IsOn change fires
            // the existing LegionNintendoLayout_Toggled handler, which recomputes A/B/X/Y into
            // gamepadButtonMappings and resends it. [Section-1 audit correction, superseding the
            // original note here] This is NOT the same "bidirectional toggle" pattern as
            // LegionDesktopControls/LegionJoystickAsMouseMode above - those two are genuinely
            // hotkey-toggleable on the helper, so a push there is real external state the widget must
            // reflect by recomputing. NintendoLayout has no hotkey (verified against
            // Program.HotkeyHandlers.cs) - it's an ordinary profile-persisted setting, and the handler
            // was simply missing an isApplyingHelperUpdate guard (fixed in
            // GamingWidget.NintendoLayoutPreset.cs - now reflects without recomputing/resending on a
            // helper push, same as every other toggle handler). The widget no longer seeds either
            // from ApplyControllerProfile, and the seed-path resend of gamepadButtonMappings
            // (SendGamepadButtonMappingsToHelper) is removed from ApplyControllerProfile /
            // ResendActiveControllerProfileToHelper - only the live-edit path (Nintendo/Desktop toggle
            // handlers via SaveAndSendGamepadMappings) still sends it.
            // [2.0 rebuild - slice 5] Gyro REMOVED: helper-authoritative. The helper persists all
            // gyro fields via RouteProfileSave (GyroSettings save-flag, §29; global-persistence bug
            // fixed) and applies+pushes them at startup (now before BatchGet) / game switch. All gyro
            // controls (comboboxes, sliders, invert toggles) use the already-guarded
            // ControllerSettingChanged / ControllerSliderSettingChanged (skip save/send when
            // isApplyingHelperUpdate), so the widget reflects the helper's pushed value cleanly.
            // [2.0 rebuild - slice 2] Stick deadzones REMOVED: helper-authoritative. The helper
            // persists them into its GameProfile (Program.LegionControllerHandlers.cs, save-flag
            // gated, §29) and applies+pushes them at startup / game switch
            // (ApplyLegionControllerSettingsFromProfile). The widget now REFLECTS the helper's
            // pushed value (bound sliders + UpdateControllerSliderDisplays text), and no longer
            // seeds them from its own LocalSettings ControllerProfile (removed from
            // ApplyControllerProfile + SendControllerSettingsToHelper).
            // [2.0 rebuild - slice 3] Trigger travel (Left/Right Start/End) + HairTriggers REMOVED:
            // helper-authoritative (same as deadzones - persisted §29, applied+pushed by
            // ApplyLegionControllerSettingsFromProfile). The 4 travel sliders reflect via the
            // already-guarded ControllerSliderSettingChanged (skips save/send when
            // isApplyingHelperUpdate); the HairTriggers toggle reflects via a guarded
            // LegionHairTriggers_Toggled (enablement always, preset+save only on user toggle).
        };

        /// <summary>
        /// Attempt to sync all properties in a single batch request.
        /// Retries if helper returns NotReady (managers still initializing).
        /// </summary>
        private async Task<bool> TryBatchSync()
        {
            const int maxNotReadyRetries = 30;  // 30 retries x 500ms = 15 seconds max wait
            const int notReadyRetryDelayMs = 500;

            for (int attempt = 0; attempt < maxNotReadyRetries; attempt++)
            {
                var result = await TryBatchSyncOnce(attempt > 0);
                if (result == BatchSyncResult.Success)
                {
                    return true;
                }
                else if (result == BatchSyncResult.NotReady)
                {
                    Logger.Info($"Batch sync: Helper not ready (attempt {attempt + 1}/{maxNotReadyRetries}), retrying in {notReadyRetryDelayMs}ms...");
                    await Task.Delay(notReadyRetryDelayMs);
                    continue;
                }
                else
                {
                    // Other failure - don't retry
                    return false;
                }
            }

            Logger.Warn($"Batch sync: Helper still not ready after {maxNotReadyRetries} attempts");
            return false;
        }

        private enum BatchSyncResult
        {
            Success,
            NotReady,
            Failed
        }

        /// <summary>
        /// Single attempt at batch sync.
        /// </summary>
        private async Task<BatchSyncResult> TryBatchSyncOnce(bool isRetry)
        {
            try
            {
                if (!App.IsConnected)
                {
                    Logger.Warn("Cannot batch sync - no connection");
                    return BatchSyncResult.Failed;
                }

                // Build list of function IDs to request as JSON array
                var jsonArray = new JsonArray();
                foreach (var prop in properties.Values)
                {
                    // Always skip properties that should never be synced from helper
                    if (NeverSyncFromHelper.Contains(prop.Function))
                    {
                        continue;
                    }
                    jsonArray.Add(JsonValue.CreateNumberValue((int)prop.Function));
                }

                // Create batch request
                var request = new ValueSet
                {
                    { "Command", (int)Command.BatchGet },
                    { "Functions", jsonArray.Stringify() }
                };

                var response = await App.SendMessageAsync(request);
                if (response == null)
                {
                    Logger.Warn("Batch sync got null response");
                    return BatchSyncResult.Failed;
                }

                // Check if helper returned NotReady (managers still initializing)
                if (response.TryGetValue("NotReady", out object notReadyObj) && notReadyObj is bool notReady && notReady)
                {
                    return BatchSyncResult.NotReady;
                }

                if (!response.TryGetValue("BatchData", out object batchDataObj) || !(batchDataObj is string batchDataJson))
                {
                    Logger.Warn("Batch sync response missing BatchData");
                    return BatchSyncResult.Failed;
                }

                // Parse batch response - format: { "functionId": { "Content": value, "UpdatedTime": time }, ... }
                if (!JsonObject.TryParse(batchDataJson, out JsonObject batchData))
                {
                    Logger.Warn("Failed to parse batch data JSON");
                    return BatchSyncResult.Failed;
                }

                int updated = 0;
                foreach (var property in properties.Values)
                {
                    var funcId = ((int)property.Function).ToString();
                    if (batchData.ContainsKey(funcId))
                    {
                        try
                        {
                            var propData = batchData.GetNamedObject(funcId);
                            if (propData.ContainsKey("Content") && propData.ContainsKey("UpdatedTime"))
                            {
                                var updatedTime = (long)propData.GetNamedNumber("UpdatedTime");
                                object content = GetJsonValue(propData, "Content");
                                if (content != null)
                                {
                                    // Suppress remote sync to avoid echoing values back to helper
                                    // This prevents widget from overwriting helper-owned properties like RunningGame, DGP
                                    property.SuppressRemoteSync = true;
                                    try
                                    {
                                        if (property.SetValue(content, updatedTime))
                                        {
                                            updated++;
                                        }
                                    }
                                    finally
                                    {
                                        property.SuppressRemoteSync = false;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Failed to parse property {property.Function}: {ex.Message}");
                        }
                    }
                }

                Logger.Info($"Batch sync updated {updated}/{properties.Count} properties");
                return BatchSyncResult.Success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Batch sync failed: {ex.Message}");
                return BatchSyncResult.Failed;
            }
        }

        /// <summary>
        /// Get a value from a JsonObject, handling different value types.
        /// </summary>
        private object GetJsonValue(JsonObject obj, string key)
        {
            var value = obj.GetNamedValue(key);
            switch (value.ValueType)
            {
                case JsonValueType.String:
                    return value.GetString();
                case JsonValueType.Number:
                    var num = value.GetNumber();
                    // Return as int if it's a whole number
                    if (num == Math.Floor(num) && num >= int.MinValue && num <= int.MaxValue)
                        return (int)num;
                    return num;
                case JsonValueType.Boolean:
                    return value.GetBoolean();
                case JsonValueType.Null:
                    return null;
                case JsonValueType.Array:
                    // Convert JSON array to comma-separated string for GenericProperty.SetValue
                    // which expects "1920x1080,1280x720" not ["1920x1080","1280x720"]
                    var array = value.GetArray();
                    var items = new List<string>();
                    foreach (var item in array)
                    {
                        if (item.ValueType == JsonValueType.String)
                            items.Add(item.GetString());
                        else if (item.ValueType == JsonValueType.Number)
                            items.Add(((int)item.GetNumber()).ToString());
                        else
                            items.Add(item.Stringify());
                    }
                    return string.Join(",", items);
                case JsonValueType.Object:
                    return value.Stringify();
                default:
                    return null;
            }
        }

        public void Cleanup()
        {
            foreach (var property in properties)
            {
                if (property.Value is WidgetSliderProperty sliderProperty)
                {
                    sliderProperty.Cleanup();
                }
            }
        }

        public void StopPendingUpdates()
        {
            foreach (var property in properties)
            {
                if (property.Value is WidgetSliderProperty sliderProperty)
                {
                    sliderProperty.StopDebounceTimer();
                }
            }
        }
    }
}
