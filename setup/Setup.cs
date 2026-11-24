using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PhoneShare
{
    // PhoneShare-Setup.exe installs the embedded PhoneShare.exe to Program Files and adds the firewall rule,
    // shortcuts and an "Apps & features" entry. A copy saved as Uninstall.exe removes it all again.
    static class Setup
    {
        const string Version = "1.0.0";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PhoneShare";
        static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PhoneShare");
        static readonly string AppExe = Path.Combine(InstallDir, "PhoneShare.exe");

        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PhoneShare.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PhoneShare.lnk"); } }
        static string SendToLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Phone (PhoneShare).lnk"); } }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))) { Uninstall(); return; }

            var app = new Application();
            var window = Ui.LoadWindow("SetupWindow.xaml");
            Func<string, object> find = window.FindName;
            ((TextBlock)find("PathText")).Text = InstallDir;
            ((TextBlock)find("VersionText")).Text = "Version " + Version;
            var installBtn = (Button)find("InstallBtn");
            var cancelBtn = (Button)find("CancelBtn");
            var status = (TextBlock)find("StatusText");
            bool installed = false;

            cancelBtn.Click += delegate { window.Close(); };
            installBtn.Click += async delegate
            {
                if (installed) { OpenApp(); window.Close(); return; }
                bool desktop = ((CheckBox)find("DesktopChk")).IsChecked == true;
                bool sendTo = ((CheckBox)find("SendToChk")).IsChecked == true;
                installBtn.IsEnabled = false;
                cancelBtn.IsEnabled = false;
                status.Text = "Installing…";
                try { await Task.Run(() => Install(desktop, sendTo)); }
                catch (Exception ex)
                {
                    status.Text = "Install failed: " + ex.Message;
                    installBtn.IsEnabled = true;
                    cancelBtn.IsEnabled = true;
                    return;
                }
                installed = true;
                ((UIElement)find("OptionsPanel")).Visibility = Visibility.Collapsed;
                ((UIElement)find("DonePanel")).Visibility = Visibility.Visible;
                status.Text = "";
                installBtn.Content = "Open PhoneShare";
                cancelBtn.Content = "Close";
                installBtn.IsEnabled = true;
                cancelBtn.IsEnabled = true;
            };
            app.Run(window);
        }

        static void Install(bool desktop, bool sendTo)
        {
            StopRunningApp();
            Directory.CreateDirectory(InstallDir);
            var me = Assembly.GetExecutingAssembly();
            using (var source = me.GetManifestResourceStream("PhoneShare.exe"))
            using (var target = File.Create(AppExe))
                source.CopyTo(target);
            File.Copy(me.Location, Path.Combine(InstallDir, "Uninstall.exe"), true);

            // Let phones on the same Wi-Fi reach PhoneShare without a firewall popup.
            Run("netsh", "advfirewall firewall delete rule name=\"PhoneShare\"");
            Run("netsh", "advfirewall firewall add rule name=\"PhoneShare\" dir=in action=allow enable=yes profile=any program=\"" + AppExe + "\"");

            CreateShortcut(StartMenuLink);
            if (desktop) CreateShortcut(DesktopLink); else TryDelete(DesktopLink);
            if (sendTo) CreateShortcut(SendToLink); else TryDelete(SendToLink);

            using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "PhoneShare");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "PhoneShare");
                key.SetValue("DisplayIcon", AppExe);
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "Uninstall.exe") + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(AppExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        static void Uninstall()
        {
            var answer = MessageBox.Show("Remove PhoneShare from this PC?\n\nYour own files are not touched.", "PhoneShare",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            StopRunningApp();
            Run("netsh", "advfirewall firewall delete rule name=\"PhoneShare\"");
            foreach (var link in new[] { StartMenuLink, DesktopLink, SendToLink }) TryDelete(link);
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneShare"), true); } catch { }
            MessageBox.Show("PhoneShare has been removed.", "PhoneShare", MessageBoxButton.OK, MessageBoxImage.Information);

            // Remove the install folder (including this Uninstall.exe) a moment after we exit.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath()
            });
        }

        static void StopRunningApp()
        {
            foreach (var p in Process.GetProcessesByName("PhoneShare"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        static void Run(string exe, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false }))
                p.WaitForExit(20000);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // Start the app as the normal (non-admin) user, so drag and drop from Explorer works.
        static void OpenApp()
        {
            try { Process.Start("explorer.exe", "\"" + AppExe + "\""); } catch { }
        }

        static void CreateShortcut(string link)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic s = shell.CreateShortcut(link);
            s.TargetPath = AppExe;
            s.Arguments = "";
            s.WorkingDirectory = InstallDir;
            s.WindowStyle = 1;
            s.IconLocation = AppExe + ",0";
            s.Description = "Send files to your phone over Wi-Fi";
            s.Save();
        }
    }
}
