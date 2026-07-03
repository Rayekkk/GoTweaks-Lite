using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NLog;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Optional Labs fix for the "Task View pops up whenever the desktop gains focus (with a
    /// controller vibration)" bug seen on some Legion Go / Go 2 setups that boot with a USB hub
    /// attached. Root cause: at boot the Legion controller composite latches a stale USB state,
    /// and Windows then re-opens Task View every time the desktop gets foreground. Physically
    /// re-plugging ANY USB device (a controller, the hub) clears it — and so does this: a software
    /// USB port power-cycle of the Legion composite via IOCTL_USB_HUB_CYCLE_PORT.
    ///
    /// Why the port-cycle and not Disable/pnputil restart: the composite contains a HID keyboard
    /// child (a boot/system device) that vetoes any PnP disable/remove, so those all defer to a
    /// reboot. IOCTL_USB_HUB_CYCLE_PORT tells the parent hub to power-cycle the physical port —
    /// a hardware surprise-remove + re-arrival, exactly like pulling and reinserting the
    /// connector — which bypasses that veto.
    ///
    /// Persisted helper-side (LocalSettingsHelper). When enabled, the cycle runs once per boot
    /// (first helper start after boot). LegionGo2-only in practice: CyclePort no-ops if the
    /// VID_17EF&amp;PID_61EB composite isn't present.
    /// </summary>
    internal static class TaskViewFixManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string EnabledKey = "TaskViewFixEnabled";
        private const string LastRunBootKey = "TaskViewFixLastRunBoot";
        private const string LegionCompositePrefix = @"USB\VID_17EF&PID_61EB\";

        public static bool IsEnabled()
        {
            return LocalSettingsHelper.TryGetValue<bool>(EnabledKey, out bool v) && v;
        }

        public static void SetEnabled(bool enabled)
        {
            LocalSettingsHelper.SetValue(EnabledKey, enabled);
            Logger.Info($"TaskViewFix: enabled = {enabled}");
        }

        /// <summary>0 = disabled, 1 = enabled. Mirrors the DAService status contract.</summary>
        public static int GetStatus() => IsEnabled() ? 1 : 0;

        /// <summary>action: 0 = disable, 1 = enable, 2 = run once now (doesn't change the toggle).</summary>
        public static void Control(int action)
        {
            if (action == 2) { RunNow(); return; }
            SetEnabled(action == 1);
        }

        /// <summary>One-time, on-demand port cycle (the "Run now" button). Returns success.</summary>
        public static bool RunNow()
        {
            Logger.Info("TaskViewFix: manual run-now requested.");
            return CyclePort();
        }

        /// <summary>
        /// Called once at helper startup. If enabled and not already run this boot, cycles the
        /// controller port a few seconds later (letting USB settle first), then records the boot
        /// so later helper restarts within the same session don't cycle again.
        /// </summary>
        public static void RunOncePerBootIfEnabled()
        {
            try
            {
                if (!IsEnabled()) return;

                string boot = GetBootId();
                if (LocalSettingsHelper.TryGetValue<string>(LastRunBootKey, out string last)
                    && string.Equals(last, boot, StringComparison.Ordinal))
                {
                    Logger.Info("TaskViewFix: already ran this boot — skipping.");
                    return;
                }

                Task.Run(async () =>
                {
                    // Let USB enumeration settle after logon before we cycle the port.
                    await Task.Delay(5000).ConfigureAwait(false);
                    bool ok = CyclePort();
                    if (ok)
                        LocalSettingsHelper.SetValue(LastRunBootKey, boot);
                    else
                        Logger.Warn("TaskViewFix: startup cycle did not complete (controller not present?) — will retry next helper start.");
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"TaskViewFix: RunOncePerBootIfEnabled failed: {ex.Message}");
            }
        }

        // Stable-per-boot id: boot wall-clock (now - uptime) quantized to the minute. GetTickCount64
        // is monotonic from boot, so this is stable across helper restarts within one boot and
        // changes on the next boot.
        private static string GetBootId()
        {
            try
            {
                ulong uptimeMs = GetTickCount64();
                DateTime bootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(uptimeMs);
                return bootUtc.ToString("yyyyMMddHHmm");
            }
            catch { return "unknown"; }
        }

        // ---------------------------------------------------------------------------------------
        // The actual work: find the Legion composite -> its parent hub + port -> IOCTL cycle port.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Simulate a physical unplug/replug of the Legion controller composite via a USB port
        /// power-cycle. Returns true on a successful IOCTL. Safe no-op (returns false) if the
        /// controller isn't present or the OS refuses the cycle.
        /// </summary>
        public static bool CyclePort()
        {
            try
            {
                string compositeId = FindLegionComposite();
                if (compositeId == null)
                {
                    Logger.Info("TaskViewFix: Legion composite (USB\\VID_17EF&PID_61EB\\...) not present — nothing to cycle.");
                    return false;
                }

                if (CM_Locate_DevNodeW(out uint devInst, compositeId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                { Logger.Warn("TaskViewFix: CM_Locate_DevNode failed."); return false; }

                if (CM_Get_Parent(out uint hubInst, devInst, 0) != CR_SUCCESS)
                { Logger.Warn("TaskViewFix: CM_Get_Parent failed."); return false; }

                string hubId = GetDeviceId(hubInst);
                if (!GetPortNumber(devInst, out uint port))
                { Logger.Warn("TaskViewFix: could not read the port number (DEVPKEY_Device_Address)."); return false; }

                string hubPath = GetHubInterfacePath(hubId);
                if (hubPath == null)
                { Logger.Warn("TaskViewFix: could not resolve the parent hub's device interface path."); return false; }

                Logger.Info($"TaskViewFix: cycling port {port} on hub '{hubId}' (composite '{compositeId}').");

                IntPtr h = CreateFileW(hubPath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                { Logger.Warn($"TaskViewFix: CreateFile(hub) failed, err={Marshal.GetLastWin32Error()}."); return false; }

                try
                {
                    var p = new USB_CYCLE_PORT_PARAMS { ConnectionIndex = port, StatusReturned = 0 };
                    uint size = (uint)Marshal.SizeOf(typeof(USB_CYCLE_PORT_PARAMS));
                    bool ok = DeviceIoControl(h, IOCTL_USB_HUB_CYCLE_PORT, ref p, size, ref p, size, out uint _, IntPtr.Zero);
                    if (!ok)
                    {
                        Logger.Warn($"TaskViewFix: IOCTL_USB_HUB_CYCLE_PORT failed, err={Marshal.GetLastWin32Error()} (StatusReturned={p.StatusReturned}).");
                        return false;
                    }
                    Logger.Info($"TaskViewFix: port cycled OK (StatusReturned={p.StatusReturned}). Controllers re-enumerating.");
                    return true;
                }
                finally { CloseHandle(h); }
            }
            catch (Exception ex)
            {
                Logger.Error($"TaskViewFix: CyclePort threw: {ex.Message}");
                return false;
            }
        }

        private static string FindLegionComposite()
        {
            foreach (string id in GetPresentDeviceIds())
            {
                if (id.StartsWith(LegionCompositePrefix, StringComparison.OrdinalIgnoreCase)
                    && id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0
                    && id.IndexOf("&COL", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return id;
                }
            }
            return null;
        }

        private static string[] GetPresentDeviceIds()
        {
            if (CM_Get_Device_ID_List_SizeW(out uint len, null, CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS || len == 0)
                return Array.Empty<string>();
            char[] buf = new char[len];
            if (CM_Get_Device_ID_ListW(null, buf, len, CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS)
                return Array.Empty<string>();
            return new string(buf).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetDeviceId(uint devInst)
        {
            if (CM_Get_Device_ID_Size(out uint len, devInst, 0) != CR_SUCCESS) return null;
            char[] buf = new char[len + 1];
            if (CM_Get_Device_IDW(devInst, buf, (uint)buf.Length, 0) != CR_SUCCESS) return null;
            return new string(buf).TrimEnd('\0');
        }

        private static bool GetPortNumber(uint devInst, out uint port)
        {
            port = 0;
            uint size = 4;
            byte[] buf = new byte[4];
            if (CM_Get_DevNode_PropertyW(devInst, ref DEVPKEY_Device_Address, out _, buf, ref size, 0) != CR_SUCCESS)
                return false;
            port = BitConverter.ToUInt32(buf, 0);
            return true;
        }

        private static string GetHubInterfacePath(string hubInstanceId)
        {
            if (hubInstanceId == null) return null;
            if (CM_Get_Device_Interface_List_SizeW(out uint len, ref GUID_DEVINTERFACE_USB_HUB, hubInstanceId, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len == 0)
                return null;
            char[] buf = new char[len];
            if (CM_Get_Device_Interface_ListW(ref GUID_DEVINTERFACE_USB_HUB, hubInstanceId, buf, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
                return null;
            string[] parts = new string(buf).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : null;
        }

        // ---- P/Invoke: CfgMgr32 ----
        private const uint CR_SUCCESS = 0;
        private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
        private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
        private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        // DEVPKEY_Device_Address = {a45c254e-df1c-4efd-8020-67d146a850e0}, 30 → USB port number on the hub.
        private static DEVPROPKEY DEVPKEY_Device_Address = new DEVPROPKEY
        { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 30 };

        // GUID_DEVINTERFACE_USB_HUB
        private static Guid GUID_DEVINTERFACE_USB_HUB = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8");

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_ID_List_SizeW(out uint len, string filter, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_ID_ListW(string filter, char[] buffer, uint bufferLen, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_Parent(out uint parentInst, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_Device_ID_Size(out uint len, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_IDW(uint devInst, char[] buffer, uint bufferLen, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint propType, byte[] buffer, ref uint size, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_Interface_List_SizeW(out uint len, ref Guid guid, string deviceId, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_Interface_ListW(ref Guid guid, string deviceId, char[] buffer, uint bufferLen, uint flags);

        // ---- P/Invoke: kernel32 ----
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_USB_HUB_CYCLE_PORT = 0x220444;

        [StructLayout(LayoutKind.Sequential)]
        private struct USB_CYCLE_PORT_PARAMS { public uint ConnectionIndex; public uint StatusReturned; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr h, uint code, ref USB_CYCLE_PORT_PARAMS inBuf, uint inSize, ref USB_CYCLE_PORT_PARAMS outBuf, uint outSize, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
