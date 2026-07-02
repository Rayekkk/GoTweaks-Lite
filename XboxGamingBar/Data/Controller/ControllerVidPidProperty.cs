using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for controller VID:PID (e.g., "17EF:6182").
    /// </summary>
    internal class ControllerVidPidProperty : WidgetProperty<string>
    {
        public ControllerVidPidProperty() : base("", null, Function.ControllerVidPid)
        {
        }

        /// <summary>
        /// Reject an empty VID:PID that arrives during BatchGet sync once we already hold a
        /// real one. The helper snapshots this property for BatchGet before the controller is
        /// detected (LegionButtonMonitor runs deferred), sending "" with UpdatedTime=0; the
        /// generic issue-#79 timestamp guard is shadowed by the object overload's 0->now
        /// coercion, so guard by value here (mirrors RunningGameProperty). A genuine clear on
        /// detach arrives as an async push (SuppressRemoteSync=false) and is still accepted.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            if (SuppressRemoteSync && !string.IsNullOrEmpty(Value)
                && newValue is string incoming && string.IsNullOrEmpty(incoming))
            {
                Logger.Info("Rejecting empty VID:PID during batch sync - preserving current value");
                return false;
            }

            return base.SetValue(newValue, updatedTime);
        }
    }
}
