using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Shared base for the eight Legion remappable-button properties (Y1/Y2/Y3/M1/M2/M3/
    /// Desktop/Page). Each holds a ButtonMapping JSON string and is NOT auto-bound to a single
    /// control — the composite per-button UI (type dropdown + gamepad/mouse/keyboard sub-controls)
    /// is rebuilt from the JSON by the widget.
    ///
    /// [2.0 rebuild - slice 7] Helper-authoritative. When the helper pushes a mapping (batch sync
    /// or an individual Set both set SuppressRemoteSync), OnValueSyncedFromHelper reflects it into
    /// the widget UI via ApplyButtonMappingFromHelper. User edits still flow widget->helper through
    /// SendMapping. The widget no longer seeds these from its LocalSettings ControllerProfile.
    /// </summary>
    internal class LegionButtonMappingProperty : WidgetControlProperty<string, ComboBox>
    {
        public LegionButtonMappingProperty(Function inFunction, ComboBox inUI, Page inOwner)
            : base("", inFunction, inUI, inOwner)
        {
            // Don't auto-bind to the ComboBox - the widget rebuilds the composite UI from JSON.
        }

        /// <summary>
        /// Sends the button mapping JSON to the helper. Sends even for "Disabled" state so the
        /// helper clears the button on the controller. force bypasses the json==Value dedup for
        /// cold-start recovery: the disconnected constructor-time send latches Value even though
        /// the pipe write was suppressed (not yet connected), so a plain re-send would no-op.
        /// </summary>
        public void SendMapping(string json, bool force = false)
        {
            if (json == null) return;
            if (force)
            {
                Logger.Info($"{Function} force-sending mapping: {json}");
                ForceSetValue(json);
            }
            else if (json != Value)
            {
                Logger.Info($"{Function} sending mapping: {json}");
                SetValue(json);
            }
        }

        protected override void OnValueSyncedFromHelper()
        {
            var widget = Owner as GamingWidget;
            var dispatcher = Owner?.Dispatcher;
            if (widget == null || dispatcher == null) return;

            // ApplyButtonMappingFromHelper rebuilds XAML, so it must run on the UI thread. The
            // batch-sync path (WidgetProperties.Sync) can run off the UI apartment - matching the
            // LosslessScaling combo properties that dispatch SyncSelectedIndexFromValue the same way.
            // On the individual-push path (already dispatched by PipeClient) this just re-queues,
            // which is harmless: ApplyButtonMappingFromHelper sets isLoadingControllerProfile itself,
            // so the guard against echoing edits back to the helper doesn't depend on when it runs.
            string json = Value;
            var func = Function;
            var ignore = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => widget.ApplyButtonMappingFromHelper(func, json));
        }
    }
}
