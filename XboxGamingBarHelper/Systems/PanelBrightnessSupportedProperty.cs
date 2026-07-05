using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Sidebar;

namespace XboxGamingBarHelper.Systems
{
    // Read-only status: is the built-in panel brightness controllable right now? False when the
    // internal panel is off/disconnected (WmiMonitorBrightness returns no instance) — the widget
    // grays out and blocks the brightness slider. Re-read on each RefreshPanelBrightness (panel open).
    internal class PanelBrightnessSupportedProperty : HelperProperty<bool, SystemManager>
    {
        public PanelBrightnessSupportedProperty(SystemManager inManager)
            : base(BrightnessManager.IsSupported(), null, Function.PanelBrightnessSupported, inManager)
        {
            Logger.Info($"PanelBrightnessSupported seeded: {Value}");
        }
    }
}
