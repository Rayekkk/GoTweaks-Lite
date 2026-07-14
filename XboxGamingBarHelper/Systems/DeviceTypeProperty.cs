using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;

namespace XboxGamingBarHelper.Systems
{
    // Read-only status: the detected device type (Shared.Enums.DeviceType, sent as its raw int
    // value). Static for the process lifetime - device identity doesn't change at runtime, so
    // this is seeded once and never re-pushed. DeviceDetector.DetectDevice() is memoized
    // internally (pre-populated once in Program.cs before manager init), so this call is cheap.
    internal class DeviceTypeProperty : HelperProperty<int, SystemManager>
    {
        public DeviceTypeProperty(SystemManager inManager)
            : base((int)DeviceDetector.DetectDevice().DeviceType, null, Function.DeviceType, inManager)
        {
            Logger.Info($"DeviceType seeded: {(DeviceType)Value}");
        }
    }
}
