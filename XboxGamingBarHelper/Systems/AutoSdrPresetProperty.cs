using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Auto SDR curve preset (0=Legion Go 2 default, 1=Custom). Persistence + the actual
    /// preset switch are routed through SystemManager, same split as AutoSdrEnabledProperty.
    /// </summary>
    internal class AutoSdrPresetProperty : HelperProperty<int, SystemManager>
    {
        public AutoSdrPresetProperty(int inValue, SystemManager inManager)
            : base(inValue, null, Function.AutoSdrPreset, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetAutoSdrPreset(Value);
        }
    }
}
