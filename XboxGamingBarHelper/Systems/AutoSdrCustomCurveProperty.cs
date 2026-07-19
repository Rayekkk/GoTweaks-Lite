using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// The Custom preset's brightness->SDR curve, as a Go2HDR-compatible flat JSON array. Set
    /// from the widget when the user edits a point (+/-1 spinner) or imports a curve file.
    /// Persistence + validation are routed through SystemManager, same split as
    /// AutoSdrEnabledProperty.
    /// </summary>
    internal class AutoSdrCustomCurveProperty : HelperProperty<string, SystemManager>
    {
        public AutoSdrCustomCurveProperty(string inValue, SystemManager inManager)
            : base(inValue, null, Function.AutoSdrCustomCurve, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            if (!(newValue is string curveJson))
            {
                Logger.Warn("Auto SDR curve rejected before state update: not a string");
                return false;
            }
            if (!AutoSdrManager.TryNormalizeCurveJson(curveJson, out string normalized, out string error))
            {
                Logger.Warn($"Auto SDR curve rejected before state update: {error}");
                return false;
            }
            return base.SetValue(normalized, updatedTime);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetAutoSdrCustomCurve(Value);
        }
    }
}
