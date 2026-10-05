using System;

namespace CityLife.World
{
    // Arithmetic port of source rng.ts and noise.ts. Keep doubles and unchecked uint overflow.
    public sealed class SeededRandom
    {
        private uint state;
        public SeededRandom(int seed) { state = unchecked((uint)seed); }
        public double Next()
        {
            unchecked
            {
                state += 0x6d2b79f5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        public int Int(int min, int max) => (int)Math.Floor(min + Next() * (max + 1.0 - min));
    }
    public sealed class SeededNoise
    {
        private readonly byte[] perm = new byte[512];
        public SeededNoise(SeededRandom random)
        {
            var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--) { int j = random.Int(0, i); byte t = p[i]; p[i] = p[j]; p[j] = t; }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }
        private double Hash(int x, int y) => perm[(x + perm[y & 255]) & 255] / 255.0;
        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        public double Value(double x, double y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double u = Fade(x - ix), v = Fade(y - iy);
            double a = Hash(ix, iy) + (Hash(ix + 1, iy) - Hash(ix, iy)) * u;
            double b = Hash(ix, iy + 1) + (Hash(ix + 1, iy + 1) - Hash(ix, iy + 1)) * u;
            return (a + (b - a) * v) * 2 - 1;
        }
        public double Fractal(double x, double y, int octaves, bool ridged = false)
        {
            double amp = 0.5, freq = 1, sum = 0, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                double n = Value(x * freq, y * freq);
                if (ridged) { n = 1 - Math.Abs(n); n *= n; }
                sum += amp * n; norm += amp; amp *= 0.5; freq *= 2;
            }
            return sum / norm;
        }
        public static double Scatter(uint n)
        {
            unchecked { n ^= n >> 16; n *= 0x85ebca6b; n ^= n >> 13; n *= 0xc2b2ae35; n ^= n >> 16; }
            return n / 4294967296.0;
        }
    }
}
