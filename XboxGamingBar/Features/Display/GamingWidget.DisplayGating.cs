using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Fail-open default (true = internal panel assumed active) so a query failure or a
        // pre-BatchGet render never blocks functionality that worked before this gate existed.
        private bool _internalPanelActive = true;

        /// <summary>
        /// True when the built-in panel is the active display right now (vs. docked with only
        /// an external monitor active). Read by the Resolution/Refresh Rate Quick tile click
        /// handlers (GamingWidget.QuickSettings.Actions.cs) to no-op instead of requesting a
        /// change that only makes sense against the internal panel.
        /// </summary>
        internal bool IsInternalPanelActive => _internalPanelActive;

        /// <summary>
        /// Applies the internal-panel-active gate to every control that only makes sense against
        /// the built-in panel: the Display tab's Auto SDR toggle + Resolution/Refresh Rate combo
        /// boxes, and the Resolution/Refresh Rate Quick tiles. Settings stay visible (per design -
        /// this is a functional/visual block, not a hide) but are disabled and dimmed. Called from
        /// InternalPanelActiveProperty whenever the helper pushes a new value (dock/undock) and
        /// once after the initial BatchGet completes.
        /// </summary>
        internal void ApplyInternalPanelActiveGate(bool internalActive)
        {
            _internalPanelActive = internalActive;

            if (AutoSdrToggle != null) AutoSdrToggle.IsEnabled = internalActive;
            if (AutoSdrRow != null) AutoSdrRow.Opacity = internalActive ? 1.0 : 0.4;

            if (ResolutionComboBox != null) ResolutionComboBox.IsEnabled = internalActive;
            if (ResolutionCard != null) ResolutionCard.Opacity = internalActive ? 1.0 : 0.4;

            if (RefreshRatesComboBox != null) RefreshRatesComboBox.IsEnabled = internalActive;
            if (RefreshRateCard != null) RefreshRateCard.Opacity = internalActive ? 1.0 : 0.4;

            // Refresh the Resolution/Refresh Rate Quick tiles so they show the same blocked state.
            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Shows/hides the Auto SDR section based on the detected device type. Auto SDR (Go2HDR)
        /// was built specifically for the Legion Go 2's panel and was never validated against
        /// other devices, so it's hidden entirely (not just disabled) everywhere else - unlike
        /// the external-monitor gate above, this is a permanent device capability, not a runtime
        /// state that can change while the widget is open.
        /// </summary>
        internal void ApplyDeviceTypeGate(int deviceType)
        {
            if (AutoSdrSection == null) return;
            bool isLegionGo2 = deviceType == (int)Shared.Enums.DeviceType.LegionGo2;
            AutoSdrSection.Visibility = isLegionGo2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
