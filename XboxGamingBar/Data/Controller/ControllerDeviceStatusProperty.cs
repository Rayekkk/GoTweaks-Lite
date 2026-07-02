using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property carrying a JSON snapshot of the controller's device status
    /// (firmware, RGB, brightness, mode, speed, vibration, touchpad). Pushed by the
    /// helper from the b0:01 status report; rendered into the Legion Info card.
    /// </summary>
    internal class ControllerDeviceStatusProperty : WidgetProperty<string>
    {
        public ControllerDeviceStatusProperty() : base("", null, Function.ControllerDeviceStatus)
        {
        }

        /// <summary>
        /// Reject an empty status snapshot that arrives during BatchGet sync once we already
        /// hold a real one. Same race as VID:PID: the helper snapshots this before the
        /// controller's b0:01 status report has populated it, sending "" with UpdatedTime=0,
        /// and the generic issue-#79 timestamp guard is shadowed by the object overload's
        /// 0->now coercion. Guard by value (mirrors RunningGameProperty); genuine updates
        /// arrive as async pushes (SuppressRemoteSync=false) and are still accepted.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            if (SuppressRemoteSync && !string.IsNullOrEmpty(Value)
                && newValue is string incoming && string.IsNullOrEmpty(incoming))
            {
                Logger.Info("Rejecting empty controller device status during batch sync - preserving current value");
                return false;
            }

            return base.SetValue(newValue, updatedTime);
        }
    }
}
