using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Maximum CPU State percentage (0-100).
    ///
    /// [2.0 rebuild - Faza C-CPU] Headless (no bound control), so a helper push wouldn't
    /// otherwise reach MaxCPUStateComboBox - OnValueSyncedFromHelper reflects it into the UI via
    /// GamingWidget.ApplyCPUStateFromHelper (same shape as LegionGamepadMappingProperty).
    /// </summary>
    internal class MaxCPUStateProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public MaxCPUStateProperty(GamingWidget inOwner) : base(100, null, Function.MaxCPUState)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            if (owner == null) return;
            int value = Value;
            var ignore = owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => owner.ApplyCPUStateFromHelper(isMax: true, value));
        }
    }
}
