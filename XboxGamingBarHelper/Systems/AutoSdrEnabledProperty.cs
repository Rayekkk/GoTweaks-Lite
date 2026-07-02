using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Helper-side master toggle for Auto SDR white-level matching (Go2HDR integration).
    /// The actual enable/disable + hardware apply is routed through SystemManager, which owns
    /// the AutoSdrManager and the current HDR state and also persists the flag across restarts.
    /// </summary>
    internal class AutoSdrEnabledProperty : HelperProperty<bool, SystemManager>
    {
        public AutoSdrEnabledProperty(bool inValue, SystemManager inManager)
            : base(inValue, null, Function.AutoSdrEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetAutoSdrEnabled(Value);
        }
    }
}
