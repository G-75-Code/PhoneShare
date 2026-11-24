using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhoneShare
{
    static class Native
    {
        // Explorer-style "natural" sort: Episode 2 comes before Episode 10.
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string a, string b);

        // LAN IPv4 addresses of this laptop, best first (Wi-Fi with a gateway wins).
        public static List<string> LanAddresses()
        {
            var found = new List<KeyValuePair<int, string>>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                IPInterfaceProperties props;
                try { props = ni.GetIPProperties(); } catch { continue; }
                string label = (ni.Name + " " + ni.Description).ToLowerInvariant();
                bool isVirtual = label.Contains("virtual") || label.Contains("vmware") || label.Contains("hyper-v")
                    || label.Contains("vethernet") || label.Contains("bluetooth") || label.Contains("vpn") || label.Contains("tap-");
                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                int score = (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 4 : 0) + (hasGateway ? 2 : 0) + (isVirtual ? 0 : 1);
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                    found.Add(new KeyValuePair<int, string>(score, ip));
                }
            }
            return found.OrderByDescending(p => p.Key).Select(p => p.Value).Distinct().ToList();
        }

        // Modern Windows folder picker (IFileOpenDialog in "pick folders" mode).
        public static string PickFolder(Window owner, string title)
        {
            var dialog = (IFileDialog)new FileOpenDialogCom();
            uint options;
            dialog.GetOptions(out options);
            dialog.SetOptions(options | 0x20 | 0x40); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
            dialog.SetTitle(title);
            if (dialog.Show(new WindowInteropHelper(owner).Handle) != 0) return null; // cancelled
            IShellItem item;
            dialog.GetResult(out item);
            string path;
            item.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
            return path;
        }

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }
    }
}
