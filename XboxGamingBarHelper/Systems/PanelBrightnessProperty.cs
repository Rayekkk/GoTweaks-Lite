using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Sidebar;

namespace XboxGamingBarHelper.Systems
{
    // Built-in display (panel) brightness, 0-100 %. Applies live to the OS via the WMI brightness
    // path (BrightnessManager -> WmiMonitorBrightnessMethods). Seeded from the real current
    // brightness at startup so the widget slider reflects reality on BatchGet. Not persisted here —
    // the panel brightness is OS-owned state, we just read/write it.
    internal class PanelBrightnessProperty : HelperProperty<int, SystemManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public PanelBrightnessProperty(SystemManager inManager)
            : base(BrightnessManager.GetBrightness(), null, Function.PanelBrightness, inManager)
        {
            Logger.Info($"PanelBrightness seeded from hardware: {Value}%");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            // Apply to the built-in panel. When the change originated from the widget slider,
            // SuppressRemoteSync is set so no echo goes back, but the hardware apply still runs.
            LastApplySucceeded = BrightnessManager.SetBrightness(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Panel brightness could not be applied.";
        }
    }
}
