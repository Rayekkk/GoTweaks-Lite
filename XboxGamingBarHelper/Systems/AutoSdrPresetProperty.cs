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

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            int preset;
            if (newValue is int intValue) preset = intValue;
            else if (!int.TryParse(newValue?.ToString(), out preset)) return false;
            if (preset != (int)AutoSdrManager.CurvePreset.LegionGo2
                && preset != (int)AutoSdrManager.CurvePreset.Custom)
            {
                Logger.Warn($"Auto SDR preset rejected: {preset}");
                return false;
            }
            // preset is statically typed int, so this binds to the protected
            // SetValue(ValueType,long) overload directly, skipping the object-overload's
            // updatedTime==0 -> "now" coercion. See LegionPerformanceModeProperty.SetValue
            // for the full failure mode this caused (silently ignored no-timestamp callers).
            if (updatedTime == 0) updatedTime = System.DateTime.Now.Ticks;
            return base.SetValue(preset, updatedTime);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetAutoSdrPreset(Value);
        }
    }
}
