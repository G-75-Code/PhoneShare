using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PhoneShare
{
    // Small HTTP/1.1 server for the phone:
    //   /                 phone page        /api/list   shared files as JSON
    //   /f/<id>/<name>    download (supports resume via Range requests)
    class Server
    {
        readonly List<SharedFile> files = new List<SharedFile>();
        readonly string phonePage = Ui.Resource("phone.html");
        TcpListener listener;
        int nextId = 1;
        long bytesSent, lastPhoneTicks;

        public int Port { get; private set; }
        public long BytesSent { get { return Interlocked.Read(ref bytesSent); } }
        public bool PhoneSeenRecently
        {
            get { return DateTime.UtcNow.Ticks - Interlocked.Read(ref lastPhoneTicks) < TimeSpan.TicksPerSecond * 10; }
        }

        public void Start()
        {
            for (int port = 8080; port < 8100 && listener == null; port++)
            {
                try { listener = new TcpListener(IPAddress.Any, port); listener.Start(); Port = port; }
                catch (SocketException) { listener = null; }
            }
            if (listener == null) throw new InvalidOperationException("No free network port between 8080 and 8099.");
            new Thread(AcceptLoop) { IsBackground = true }.Start();
        }

        // ---------- shared file list (called from the UI thread) ----------

        public List<SharedFile> Add(IEnumerable<string> paths)
        {
            var added = new List<SharedFile>();
            foreach (var path in paths) Collect(path, added, 0);
            return added;
        }

        public void Remove(SharedFile file) { lock (files) files.Remove(file); }
        public void Clear() { lock (files) files.Clear(); }

        void Collect(string path, List<SharedFile> added, int depth)
        {
            try
            {
                path = Path.GetFullPath(path.Trim().Trim('"'));
                if (Directory.Exists(path))
                {
                    if (depth > 32) return;
                    var entries = Directory.GetFileSystemEntries(path);
                    Array.Sort(entries, (a, b) => Native.StrCmpLogicalW(a, b));
                    foreach (var entry in entries) Collect(entry, added, depth + 1);
                    return;
                }
                if (!File.Exists(path)) return;
                // Inside folders, skip hidden/system files such as desktop.ini and Thumbs.db.
                if (depth > 0 && (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0) return;
                long size = new FileInfo(path).Length;
                lock (files)
                {
                    if (files.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))) return;
                    var file = new SharedFile(nextId++, path, size);
                    files.Add(file);
                    added.Add(file);
                }
            }
            catch { }
        }

        // ---------- HTTP ----------

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { return; }
                new Thread(() => Handle(client)) { IsBackground = true }.Start();
            }
        }

        void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    client.NoDelay = true;
                    stream.ReadTimeout = 15000;
                    stream.WriteTimeout = 60000;
                    string method, target;
                    Dictionary<string, string> headers;
                    if (!ReadRequest(stream, out method, out target, out headers)) return;
                    var remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                    if (!IPAddress.IsLoopback(remote)) Interlocked.Exchange(ref lastPhoneTicks, DateTime.UtcNow.Ticks);
                    Route(stream, method, target, headers);
                }
            }
            catch { }
        }

        static bool ReadRequest(Stream s, out string method, out string target, out Dictionary<string, string> headers)
        {
            method = target = null;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buf = new byte[16384];
            int len = 0, end = -1;
            while (end < 0)
            {
                if (len == buf.Length) return false;
                int n = s.Read(buf, len, buf.Length - len);
                if (n <= 0) return false;
                len += n;
                for (int i = Math.Max(0, len - n - 3); i + 3 < len; i++)
                    if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10) { end = i; break; }
            }
            var lines = Encoding.ASCII.GetString(buf, 0, end).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 3) return false;
            method = first[0];
            target = first[1];
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            return true;
        }

        void Route(Stream s, string method, string target, Dictionary<string, string> headers)
        {
            bool head = method == "HEAD";
            if (method != "GET" && !head) { SendText(s, false, 405, "text/plain", "Method not allowed"); return; }
            string path = target.Split('?')[0];
            if (path == "/") { SendText(s, head, 200, "text/html; charset=utf-8", phonePage); return; }
            if (path == "/api/list") { SendText(s, head, 200, "application/json; charset=utf-8", ListJson()); return; }
            if (path.StartsWith("/f/"))
            {
                int id;
                SharedFile file = null;
                if (int.TryParse(path.Substring(3).Split('/')[0], out id))
                    lock (files) file = files.FirstOrDefault(f => f.Id == id);
                if (file != null) SendFile(s, head, headers, file);
                else SendText(s, head, 404, "text/plain; charset=utf-8", "This file is no longer shared.");
                return;
            }
            SendText(s, head, 404, "text/plain; charset=utf-8", "Not found");
        }

        void SendFile(Stream s, bool head, Dictionary<string, string> headers, SharedFile file)
        {
            FileStream fs;
            try { fs = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan); }
            catch { SendText(s, head, 404, "text/plain; charset=utf-8", "The file is missing on the laptop."); return; }

            using (fs)
            {
                long size = fs.Length, start = 0, end = size - 1;
                bool partial = false;
                string range;
                if (headers.TryGetValue("Range", out range) && range.StartsWith("bytes="))
                {
                    string r = range.Substring(6).Split(',')[0].Trim();
                    int dash = r.IndexOf('-');
                    long a, b;
                    if (dash > 0 && long.TryParse(r.Substring(0, dash), out a))
                    {
                        start = a; partial = true;
                        if (long.TryParse(r.Substring(dash + 1), out b)) end = Math.Min(b, size - 1);
                    }
                    else if (dash == 0 && long.TryParse(r.Substring(1), out b))
                    {
                        start = Math.Max(0, size - b); partial = true;
                    }
                    if (partial && (start >= size || start > end))
                    {
                        WriteHead(s, 416, new[] { "Content-Range: bytes */" + size, "Content-Length: 0" });
                        return;
                    }
                }

                long length = Math.Max(0, end - start + 1);
                var head2 = new List<string>
                {
                    "Content-Type: application/octet-stream",
                    "Content-Length: " + length,
                    "Accept-Ranges: bytes",
                    "Content-Disposition: attachment; filename*=UTF-8''" + Uri.EscapeDataString(file.Name)
                };
                if (partial) head2.Add(string.Format("Content-Range: bytes {0}-{1}/{2}", start, end, size));
                WriteHead(s, partial ? 206 : 200, head2);
                if (head || length == 0) return;

                fs.Position = start;
                file.BeginTransfer(start);
                try
                {
                    var buf = new byte[1 << 20];
                    long left = length;
                    while (left > 0)
                    {
                        int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                        if (n <= 0) break;
                        s.Write(buf, 0, n);
                        left -= n;
                        file.AddSent(n);
                        Interlocked.Add(ref bytesSent, n);
                    }
                }
                finally { file.EndTransfer(); }
            }
        }

        static void SendText(Stream s, bool head, int code, string type, string body)
        {
            byte[] data = Encoding.UTF8.GetBytes(body);
            WriteHead(s, code, new[] { "Content-Type: " + type, "Content-Length: " + data.Length, "Cache-Control: no-store" });
            if (!head) s.Write(data, 0, data.Length);
        }

        static void WriteHead(Stream s, int code, IEnumerable<string> headers)
        {
            string reason = code == 200 ? "OK" : code == 206 ? "Partial Content" : code == 404 ? "Not Found"
                : code == 405 ? "Method Not Allowed" : code == 416 ? "Range Not Satisfiable" : "OK";
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            foreach (var h in headers) sb.Append(h).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
            s.Write(bytes, 0, bytes.Length);
        }

        string ListJson()
        {
            var sb = new StringBuilder("[");
            lock (files)
                for (int i = 0; i < files.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":").Append(files[i].Id)
                      .Append(",\"name\":").Append(Json(files[i].Name))
                      .Append(",\"size\":").Append(files[i].Size).Append('}');
                }
            return sb.Append(']').ToString();
        }

        static string Json(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
