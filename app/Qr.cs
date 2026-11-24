using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhoneShare
{
    // Small QR code encoder: byte mode, error correction level M, versions 1-6
    // (up to 106 bytes, plenty for a link like http://192.168.1.20:8080).
    static class Qr
    {
        // Per version 1..6 at level M: data codewords per block, ECC codewords per block, block count.
        static readonly int[,] Spec = { { 16, 10, 1 }, { 28, 16, 1 }, { 44, 26, 1 }, { 32, 18, 2 }, { 43, 24, 2 }, { 27, 16, 4 } };

        public static bool[,] Encode(string text, int forceMask = -1)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int version = 0;
            for (int v = 1; v <= 6 && version == 0; v++)
                if (Spec[v - 1, 0] * Spec[v - 1, 2] * 8 >= 12 + data.Length * 8) version = v;
            if (version == 0) throw new ArgumentException("Text is too long for the QR code.");
            int dataLen = Spec[version - 1, 0], eccLen = Spec[version - 1, 1], blocks = Spec[version - 1, 2];
            int capacity = dataLen * blocks;

            // Bit stream: mode 0100, 8-bit length, the bytes, terminator, then pad bytes.
            var bits = new List<bool>();
            Append(bits, 4, 4);
            Append(bits, data.Length, 8);
            foreach (byte b in data) Append(bits, b, 8);
            Append(bits, 0, Math.Min(4, capacity * 8 - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);
            var codewords = new byte[capacity];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(0x80 >> (i & 7));
            for (int i = bits.Count / 8, k = 0; i < capacity; i++, k++) codewords[i] = (byte)(k % 2 == 0 ? 0xEC : 0x11);

            // Error correction per block, then interleave blocks.
            byte[] divisor = RsDivisor(eccLen);
            var ecc = new byte[blocks][];
            for (int b = 0; b < blocks; b++) ecc[b] = RsRemainder(codewords, b * dataLen, dataLen, divisor);
            var all = new List<byte>();
            for (int i = 0; i < dataLen; i++) for (int b = 0; b < blocks; b++) all.Add(codewords[b * dataLen + i]);
            for (int i = 0; i < eccLen; i++) for (int b = 0; b < blocks; b++) all.Add(ecc[b][i]);

            int size = version * 4 + 17;
            var m = new bool[size, size];
            var fn = new bool[size, size];
            DrawFunctionPatterns(m, fn, size, version);
            PlaceData(m, fn, size, all);

            int mask = forceMask;
            if (mask < 0)
            {
                int best = int.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    ApplyMask(m, fn, size, k);
                    DrawFormat(m, fn, size, k);
                    int score = Penalty(m, size);
                    if (score < best) { best = score; mask = k; }
                    ApplyMask(m, fn, size, k); // XOR again to undo
                }
            }
            ApplyMask(m, fn, size, mask);
            DrawFormat(m, fn, size, mask);
            return m;
        }

        // Black-and-white bitmap, one pixel per module, with a light border ("quiet zone").
        public static BitmapSource Render(bool[,] m, int quiet)
        {
            int n = m.GetLength(0), w = n + quiet * 2;
            var px = new byte[w * w];
            for (int i = 0; i < px.Length; i++) px[i] = 255;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x]) px[(y + quiet) * w + x + quiet] = 0;
            var bmp = BitmapSource.Create(w, w, 96, 96, PixelFormats.Gray8, null, px, w);
            bmp.Freeze();
            return bmp;
        }

        static void Append(List<bool> bits, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark) { m[y, x] = dark; fn[y, x] = true; }

        static void DrawFunctionPatterns(bool[,] m, bool[,] fn, int size, int version)
        {
            for (int i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
            Finder(m, fn, size, 3, 3);
            Finder(m, fn, size, size - 4, 3);
            Finder(m, fn, size, 3, size - 4);
            if (version > 1)
            {
                int c = size - 7;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(m, fn, c + dx, c + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            DrawFormat(m, fn, size, 0); // reserves the format areas; real bits are drawn after masking
        }

        static void Finder(bool[,] m, bool[,] fn, int size, int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (x >= 0 && x < size && y >= 0 && y < size) Set(m, fn, x, y, d != 2 && d != 4);
                }
        }

        static void DrawFormat(bool[,] m, bool[,] fn, int size, int mask)
        {
            int data = mask; // level M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) Set(m, fn, 8, i, Bit(bits, i));
            Set(m, fn, 8, 7, Bit(bits, 6));
            Set(m, fn, 8, 8, Bit(bits, 7));
            Set(m, fn, 7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) Set(m, fn, size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) Set(m, fn, 8, size - 15 + i, Bit(bits, i));
            Set(m, fn, 8, size - 8, true);
        }

        static bool Bit(int value, int i) { return ((value >> i) & 1) != 0; }

        static void PlaceData(bool[,] m, bool[,] fn, int size, List<byte> data)
        {
            int i = 0, total = data.Count * 8;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (!fn[y, x] && i < total)
                        {
                            m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] fn, int size, int mask)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (fn[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) m[y, x] = !m[y, x];
                }
        }

        static readonly bool[] FinderLike1 = { true, false, true, true, true, false, true, false, false, false, false };
        static readonly bool[] FinderLike2 = { false, false, false, false, true, false, true, true, true, false, true };

        // Standard mask penalty: long runs, 2x2 blocks, finder-like patterns, dark/light balance.
        static int Penalty(bool[,] m, int size)
        {
            int score = 0, dark = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 0;
                    bool prev = false;
                    for (int b = 0; b < size; b++)
                    {
                        bool c = pass == 0 ? m[a, b] : m[b, a];
                        if (b > 0 && c == prev) { run++; if (run == 5) score += 3; else if (run > 5) score++; }
                        else run = 1;
                        prev = c;
                        if (b < 10) continue;
                        bool p1 = true, p2 = true;
                        for (int k = 0; k < 11; k++)
                        {
                            bool v = pass == 0 ? m[a, b - 10 + k] : m[b - 10 + k, a];
                            if (v != FinderLike1[k]) p1 = false;
                            if (v != FinderLike2[k]) p2 = false;
                        }
                        if (p1) score += 40;
                        if (p2) score += 40;
                    }
                }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                        score += 3;
                }
            int total = size * size;
            return score + Math.Abs(dark * 20 - total * 10) / total * 10;
        }

        static byte[] RsDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Mul(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Mul(root, 2);
            }
            return result;
        }

        static byte[] RsRemainder(byte[] data, int offset, int count, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            for (int i = offset; i < offset + count; i++)
            {
                int factor = data[i] ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int j = 0; j < result.Length; j++) result[j] ^= Mul(divisor[j], factor);
            }
            return result;
        }

        static byte Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }
    }
}
