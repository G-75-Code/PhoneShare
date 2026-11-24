using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace PhoneShare
{
    // Behaviour of the main window (the layout is in MainWindow.xaml).
    class MainWindow
    {
        public readonly Window Window;
        readonly Server server = new Server();
        readonly ObservableCollection<SharedFile> files = new ObservableCollection<SharedFile>();
        readonly List<double> speeds = Enumerable.Repeat(0.0, 60).ToList();
        long lastBytes;
        string url;
        bool addressShown;

        static string SaveFile { get { return IOPath.Combine(Program.DataDir, "shared.txt"); } }

        public MainWindow(string[] startupPaths)
        {
            Window = Ui.LoadWindow("MainWindow.xaml");
            SharedFile.MutedBrush = Res("Muted");
            SharedFile.AccentBrush = Res("Accent");
            SharedFile.OkBrush = Res("Ok");

            server.Start();
            Get<ItemsControl>("FileList").ItemsSource = files;
            AddPaths(LoadSaved(), false);
            AddPaths(startupPaths, true);
            HookUpEvents();
            ShowAddress();
            NetworkChange.NetworkAddressChanged += delegate { Window.Dispatcher.BeginInvoke(new Action(ShowAddress)); };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { Tick(); };
            timer.Start();
            Window.Loaded += delegate { Tick(); };
        }

        T Get<T>(string name) where T : class { return Window.FindName(name) as T; }
        Brush Res(string key) { return (Brush)Window.FindResource(key); }

        void HookUpEvents()
        {
            Get<Button>("AddFilesBtn").Click += delegate { PickFiles(); };
            Get<Button>("AddFolderBtn").Click += delegate
            {
                string folder = Native.PickFolder(Window, "Choose a folder to share with your phone");
                if (folder != null) AddPaths(new[] { folder }, true);
            };
            Get<Button>("CopyBtn").Click += delegate { CopyLink(); };
            Get<Button>("ClearBtn").Click += delegate { ClearAll(); };

            // The × button on each row.
            Get<ItemsControl>("FileList").AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var button = e.OriginalSource as Button;
                var file = button == null ? null : button.Tag as SharedFile;
                if (file == null) return;
                server.Remove(file);
                files.Remove(file);
                Save();
                UpdateList();
            }));

            Window.DragEnter += (s, e) => Highlight(e.Data.GetDataPresent(DataFormats.FileDrop));
            Window.DragLeave += (s, e) => Highlight(false);
            Window.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Window.Drop += (s, e) =>
            {
                Highlight(false);
                var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null) AddPaths(dropped, true);
            };
        }

        // Called when Explorer's "Send to → Phone" is used while the app is already open.
        public void AddAndShow(string[] paths)
        {
            AddPaths(paths, true);
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Activate();
            Window.Topmost = true;
            Window.Topmost = false;
        }

        void PickFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Choose files to share with your phone" };
            if (dialog.ShowDialog(Window) == true) AddPaths(dialog.FileNames, true);
        }

        void ClearAll()
        {
            if (files.Count == 0) return;
            var answer = MessageBox.Show(Window,
                "Remove all files from the list?\n\nYour files stay on this laptop. They just stop being shared.",
                "PhoneShare", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            server.Clear();
            files.Clear();
            Save();
            UpdateList();
        }

        void AddPaths(IEnumerable<string> paths, bool save)
        {
            var list = (paths ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (list.Length == 0) return;
            Mouse.OverrideCursor = Cursors.Wait;
            try { foreach (var file in server.Add(list)) files.Add(file); }
            finally { Mouse.OverrideCursor = null; }
            if (save) Save();
            UpdateList();
        }

        void Save()
        {
            try { IOFile.WriteAllLines(SaveFile, files.Select(f => f.FullPath)); } catch { }
        }

        static IEnumerable<string> LoadSaved()
        {
            try { return IOFile.Exists(SaveFile) ? IOFile.ReadAllLines(SaveFile) : new string[0]; }
            catch { return new string[0]; }
        }

        void UpdateList()
        {
            bool any = files.Count > 0;
            Get<TextBlock>("ListTitle").Text = any
                ? string.Format("Shared files  ·  {0}  ·  {1}", files.Count, Fmt.Size(files.Sum(f => f.Size)))
                : "Shared files";
            Get<UIElement>("EmptyState").Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            Get<UIElement>("ClearBtn").Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        void ShowAddress()
        {
            var ips = Native.LanAddresses();
            string next = ips.Count > 0 ? "http://" + ips[0] + ":" + server.Port : null;
            var other = Get<TextBlock>("OtherText");
            other.Text = ips.Count > 1
                ? "Other network: " + string.Join(", ", ips.Skip(1).Select(ip => ip + ":" + server.Port))
                : "";
            other.Visibility = ips.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (addressShown && next == url) return;
            addressShown = true;
            url = next;

            var image = Get<Image>("QrImage");
            if (url == null)
            {
                image.Source = null;
                Get<TextBlock>("UrlText").Text = "Not connected";
                return;
            }
            var qr = Qr.Render(Qr.Encode(url), 2);
            int scale = Math.Max(1, 176 / qr.PixelWidth);
            image.Source = qr;
            image.Width = image.Height = qr.PixelWidth * scale;
            Get<TextBlock>("UrlText").Text = url;
        }

        void CopyLink()
        {
            if (url == null) return;
            try { Clipboard.SetText(url); } catch { return; }
            var button = Get<Button>("CopyBtn");
            button.Content = ""; // check mark
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += delegate { button.Content = ""; timer.Stop(); };
            timer.Start();
        }

        void Highlight(bool on)
        {
            var rect = Get<Rectangle>("DropRect");
            rect.Stroke = Res(on ? "Accent" : "Line");
            rect.Fill = Res(on ? "AccentSoft" : "Card");
        }

        // Once a second: speed, graph, per-file progress and the status line.
        void Tick()
        {
            long total = server.BytesSent;
            double mbps = (total - lastBytes) / 1048576.0;
            lastBytes = total;
            speeds.RemoveAt(0);
            speeds.Add(mbps);

            int active = 0;
            foreach (var file in files) { file.Refresh(); active += file.ActiveDownloads; }

            Get<TextBlock>("SpeedText").Text = mbps >= 100 ? mbps.ToString("0") : mbps.ToString("0.0");
            Get<TextBlock>("SpeedSub").Text = active > 0
                ? string.Format("{0:0.00} Gbps  ·  {1} download{2} running", mbps * 8 * 1048576 / 1e9, active, active == 1 ? "" : "s")
                : total > 0 ? "Sent this session: " + Fmt.Size(total) : "Waiting for your phone";
            DrawGraph();

            var dot = Get<Ellipse>("StatusDot");
            var status = Get<TextBlock>("StatusText");
            if (url == null) { dot.Fill = Res("Danger"); status.Text = "Not connected to Wi‑Fi"; }
            else if (active > 0) { dot.Fill = Res("Accent"); status.Text = "Sending to phone…"; }
            else if (server.PhoneSeenRecently) { dot.Fill = Res("Ok"); status.Text = "Phone connected"; }
            else { dot.Fill = Res("Ok"); status.Text = "Ready  ·  waiting for phone"; }
        }

        void DrawGraph()
        {
            var canvas = Get<Canvas>("Graph");
            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 2 || h < 2) return;
            double max = Math.Max(1, speeds.Max()) * 1.15;
            var line = new PointCollection();
            for (int i = 0; i < speeds.Count; i++)
                line.Add(new Point(w * i / (speeds.Count - 1), h - 1 - (h - 2) * speeds[i] / max));
            var fill = new PointCollection(line) { new Point(w, h), new Point(0, h) };
            Get<Polyline>("GraphLine").Points = line;
            Get<Polygon>("GraphFill").Points = fill;
        }
    }
}
