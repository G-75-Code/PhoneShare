using System;
using System.Text;

namespace PhoneShare
{
    // Test helper: prints a hash of the QR matrix for each of the 8 masks, to compare with another encoder.
    static class QrTest
    {
        static void Main(string[] args)
        {
            for (int mask = 0; mask < 8; mask++)
            {
                var m = Qr.Encode(args[0], mask);
                int n = m.GetLength(0);
                var sb = new StringBuilder();
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) sb.Append(m[y, x] ? '1' : '0');
                uint h = 2166136261;
                foreach (char c in sb.ToString()) { h ^= c; h *= 16777619; }
                Console.WriteLine(mask + " " + n + " " + h);
            }
        }
    }
}
