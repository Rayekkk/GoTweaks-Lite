using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonBoostSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonBoostSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonBoostSupported, inManager)
        {
        }
    }

    internal class AMDRadeonBoostEnabledProperty : HelperProperty<bool, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonBoostEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonBoostEnabled, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            bool prev = Value;
            bool result = base.SetValue(newValue, updatedTime);
            // Display-tab fix (same as AMDFluidMotionFrameEnabledProperty) - see that class for
            // the full explanation. Push to driver whenever the result was accepted, regardless
            // of whether the cached value changed.
            if (result && prev == Value)
            {
                Manager.AMD3DSettingsChangedListener?.NotifyBoostChanged();
                ApplyToDriver();
            }
            return result;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Start cooldown before writing, same as AFMF/RSR/RIS - without this the native ADLX
            // callback (AMD3DSettingsChangedListener) can read back a stale pre-commit value
            // immediately after our own write and undo it.
            Manager.AMD3DSettingsChangedListener?.NotifyBoostChanged();
            ApplyToDriver();
        }

        private void ApplyToDriver()
        {
            LastApplySucceeded = Manager.AMDRadeonBoostSetting.SetEnabled(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Boost could not be applied.";
        }
    }

    internal class AMDRadeonBoostResolutionProperty : HelperProperty<int, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonBoostResolutionProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonBoostResolution, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            (int min, int max) = Manager.AMDRadeonBoostSetting.GetResolutionRange();
            LastApplySucceeded = Manager.AMDRadeonBoostSetting.SetResolution(Value == 0 ? min : max);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Boost resolution could not be applied.";
        }
    }
}
