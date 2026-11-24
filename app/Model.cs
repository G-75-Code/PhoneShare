using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace PhoneShare
{
    static class Fmt
    {
        public static string Size(double bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824).ToString("0.00") + " GB";
            if (bytes >= 1048576) return (bytes / 1048576).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024).ToString("0") + " KB";
            return bytes.ToString("0") + " B";
        }
    }

    // One shared file. Server threads update the counters; the window calls Refresh() once a second.
    class SharedFile : INotifyPropertyChanged
    {
        public static Brush MutedBrush, AccentBrush, OkBrush;
        public event PropertyChangedEventHandler PropertyChanged;

        public int Id { get; private set; }
        public string FullPath { get; private set; }
        public string Name { get; private set; }
        public long Size { get; private set; }
        public string SizeText { get { return Fmt.Size(Size); } }
        public string Glyph { get; private set; }

        public string Status { get; private set; }
        public Brush StatusBrush { get; private set; }
        public double Progress { get; private set; }
        public Visibility ProgressVisibility { get; private set; }

        readonly object gate = new object();
        int active, completed;
        long sessionBytes, totalSent, lastTotal;

        public SharedFile(int id, string path, long size)
        {
            Id = id;
            FullPath = path;
            Name = System.IO.Path.GetFileName(path);
            Size = size;
            Glyph = GlyphFor(System.IO.Path.GetExtension(path));
            Status = "Waiting for phone";
            StatusBrush = MutedBrush;
            ProgressVisibility = Visibility.Collapsed;
        }

        public int ActiveDownloads { get { lock (gate) return active; } }

        // A download (or a resumed part of one) starts at the given byte offset.
        public void BeginTransfer(long offset) { lock (gate) { if (active == 0) sessionBytes = offset; active++; } }
        public void AddSent(int bytes) { lock (gate) { sessionBytes += bytes; totalSent += bytes; } }
        public void EndTransfer() { lock (gate) { active--; if (active == 0 && sessionBytes >= Size) completed++; } }

        public void Refresh()
        {
            int running, done; long session, total;
            lock (gate) { running = active; done = completed; session = sessionBytes; total = totalSent; }
            long rate = total - lastTotal;
            lastTotal = total;
            double pct = Size > 0 ? Math.Min(100, session * 100.0 / Size) : 100;

            string status; Brush brush; bool bar;
            if (running > 0)
            {
                status = string.Format("Sending to phone  ·  {0:0}%  ·  {1}/s", pct, Fmt.Size(rate));
                brush = AccentBrush; bar = true;
            }
            else if (done > 0 && session >= Size)
            {
                status = done > 1 ? string.Format("Sent to phone ✓  ({0} times)", done) : "Sent to phone ✓";
                brush = OkBrush; bar = false;
            }
            else if (session >= Math.Max(1048576, Size / 100)) // ignore tiny probe requests some phones make
            {
                status = string.Format("Stopped at {0:0}%  ·  tap it again on the phone to resume", pct);
                brush = MutedBrush; bar = true;
            }
            else
            {
                status = "Waiting for phone";
                brush = MutedBrush; bar = false;
            }

            if (status == Status && pct == Progress) return;
            Status = status;
            StatusBrush = brush;
            Progress = pct;
            ProgressVisibility = bar ? Visibility.Visible : Visibility.Collapsed;
            Raise("Status"); Raise("StatusBrush"); Raise("Progress"); Raise("ProgressVisibility");
        }

        void Raise(string property)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }

        // Segoe icon glyph that matches the file type.
        static string GlyphFor(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".mp4": case ".mkv": case ".avi": case ".mov": case ".webm":
                case ".m4v": case ".wmv": case ".flv": case ".3gp": case ".ts":
                    return "";
                case ".mp3": case ".wav": case ".flac": case ".m4a": case ".aac":
                case ".ogg": case ".opus": case ".wma":
                    return "";
                case ".jpg": case ".jpeg": case ".png": case ".gif": case ".webp":
                case ".heic": case ".bmp": case ".tif": case ".tiff":
                    return "";
                case ".zip": case ".rar": case ".7z": case ".tar": case ".gz":
                    return "";
                case ".apk":
                    return "";
                default:
                    return "";
            }
        }
    }
}
