using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonSuperResolutionSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonSuperResolutionSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionSupported, inManager)
        {
        }
    }

    internal class AMDRadeonSuperResolutionEnabledProperty : HelperProperty<bool, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonSuperResolutionEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionEnabled, inManager)
        {
        }

        // [2026-07-20 simplification] GoTweaks->AMD is now the only sync direction - always push
        // to the driver. GenericProperty.SetValue's equality skip prevents NotifyPropertyChanged
        // from firing when our cached value matches the incoming value, so the SetEnabled call
        // below never reaches ADLX unless we force it here too - the cache can drift from actual
        // driver state (a prior SetEnabled silently failed, etc.), so the Display-tab toggle would
        // persist in the widget UI while the driver stays in its previous state.
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            bool prev = Value;
            bool result = base.SetValue(newValue, updatedTime);
            if (result && prev == Value)
            {
                ApplyToDriver();
            }
            return result;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            ApplyToDriver();
        }

        private void ApplyToDriver()
        {
            LastApplySucceeded = Manager.AMDRadeonSuperResolutionSetting.SetEnabled(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Super Resolution could not be applied.";
        }
    }

    internal class AMDRadeonSuperResolutionSharpnessProperty : HelperProperty<int, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonSuperResolutionSharpnessProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionSharpness, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            (int min, int max) = Manager.AMDRadeonSuperResolutionSetting.GetSharpnessRange();
            LastApplySucceeded = (min == 0 && max == 100)
                ? Manager.AMDRadeonSuperResolutionSetting.SetSharpness(Value)
                : Manager.AMDRadeonSuperResolutionSetting.SetSharpness((int)Math.Round(min + Value / 100.0f * (max - min)));
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Super Resolution sharpness could not be applied.";
        }
    }
}
