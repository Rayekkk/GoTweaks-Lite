using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDImageSharpeningSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDImageSharpeningSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDImageSharpeningSupported, inManager)
        {
        }
    }

    internal class AMDImageSharpeningEnabledProperty : HelperProperty<bool, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDImageSharpeningEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDImageSharpeningEnabled, inManager)
        {
        }

        // [2026-07-20 simplification] GoTweaks->AMD is now the only sync direction - always push
        // to the driver.
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            bool prev = Value;
            bool result = base.SetValue(newValue, updatedTime);
            // Display-tab fix (same as AMDFluidMotionFrameEnabledProperty) - see that class for
            // the full explanation. Push to driver whenever the result was accepted, regardless
            // of whether the cached value changed.
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
            LastApplySucceeded = Manager.AMDImageSharpeningSetting.SetEnabled(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Image Sharpening could not be applied.";
        }
    }

    internal class AMDImageSharpeningSharpnessProperty : HelperProperty<int, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDImageSharpeningSharpnessProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDImageSharpeningSharpness, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // [bug fix] The widget's slider now sends the driver's raw sharpness scale directly
            // (XAML Minimum="10" Maximum="100", matching this device's AMD Adrenalin range 1:1 -
            // WYSIWYG), not a 0-100 percentage. The old proportional remap
            // (min + Value/100*(max-min)) stretched the widget's 0-100 range onto the real
            // 10-100 driver range, so e.g. a widget value of 50 became 55 on the driver -
            // confirmed by on-device comparison against Adrenalin's own slider. Clamp to the
            // queried range for safety (in case a different Radeon GPU ever reports a range
            // that doesn't match the widget's hardcoded 10-100).
            (int min, int max) = Manager.AMDImageSharpeningSetting.GetSharpnessRange();
            int clamped = Math.Max(min, Math.Min(max, Value));
            LastApplySucceeded = Manager.AMDImageSharpeningSetting.SetSharpness(clamped);
            LastApplyFailureReason = LastApplySucceeded ? null : "Image Sharpening level could not be applied.";
        }
    }
}
