using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;

namespace PhoneShare
{
    static class Program
    {
        static readonly string PipeName = "PhoneShare-" + Environment.UserName;
        static Mutex singleInstance;

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneShare");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            singleInstance = new Mutex(true, @"Local\PhoneShare-" + Environment.UserName, out first);
            if (!first) { PassToRunningCopy(args); return; }

            var app = new Application();
            app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
            MainWindow main;
            try { main = new MainWindow(args); }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show("PhoneShare could not start.\n\n" + ex.Message, "PhoneShare", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            ListenForOtherCopies(main);
            app.Run(main.Window);
            GC.KeepAlive(singleInstance);
        }

        // "Send to → Phone" starts a second copy: it hands its files to the open window and exits.
        static void PassToRunningCopy(string[] args)
        {
            var paths = args.Select(FullPath).Where(p => p != null).ToArray();
            byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", paths));
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        pipe.Connect(500);
                        pipe.Write(data, 0, data.Length);
                        return;
                    }
                }
                catch { Thread.Sleep(250); }
            }
        }

        static void ListenForOtherCopies(MainWindow main)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1))
                        {
                            pipe.WaitForConnection();
                            string text = new StreamReader(pipe, Encoding.UTF8).ReadToEnd();
                            string[] paths = text.Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                            main.Window.Dispatcher.BeginInvoke(new Action(() => main.AddAndShow(paths)));
                        }
                    }
                    catch (Exception ex) { Log(ex); Thread.Sleep(500); }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        static string FullPath(string path)
        {
            try { return Path.GetFullPath(path); } catch { return null; }
        }

        public static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "error.log"), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); }
            catch { }
        }
    }
}
