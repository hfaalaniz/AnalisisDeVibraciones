using System.Numerics;

namespace VibrationAnalysis.Core.Dsp;

/// <summary>
/// Transformada rápida de Fourier sin dependencias externas.
/// Radix-2 iterativa para potencias de 2 y algoritmo de Bluestein para cualquier otro largo.
/// Convención: X[k] = Σ x[n]·e^(−j2πkn/N) (sin normalizar).
/// </summary>
public static class Fft
{
    public static Complex[] Forward(IReadOnlyList<double> x)
    {
        var a = new Complex[x.Count];
        for (int i = 0; i < a.Length; i++) a[i] = new Complex(x[i], 0);
        Transform(a, inverse: false);
        return a;
    }

    public static void Transform(Complex[] a, bool inverse)
    {
        int n = a.Length;
        if (n <= 1) return;
        if (IsPowerOfTwo(n)) Radix2(a, inverse);
        else Bluestein(a, inverse);
    }

    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    public static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    private static void Radix2(Complex[] a, bool inverse)
    {
        int n = a.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                int half = len / 2;
                for (int j = 0; j < half; j++)
                {
                    var u = a[i + j];
                    var v = a[i + j + half] * w;
                    a[i + j] = u + v;
                    a[i + j + half] = u - v;
                    w *= wlen;
                }
            }
        }
        if (inverse)
            for (int i = 0; i < n; i++) a[i] /= n;
    }

    private static void Bluestein(Complex[] a, bool inverse)
    {
        int n = a.Length;
        int m = NextPowerOfTwo(2 * n - 1);
        double sign = inverse ? 1 : -1;
        var w = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            // k² mod 2n evita pérdida de precisión con k grandes
            long kk = (long)k * k % (2L * n);
            double ang = sign * Math.PI * kk / n;
            w[k] = new Complex(Math.Cos(ang), Math.Sin(ang));
        }
        var A = new Complex[m];
        var B = new Complex[m];
        for (int k = 0; k < n; k++) A[k] = a[k] * w[k];
        B[0] = Complex.Conjugate(w[0]);
        for (int k = 1; k < n; k++) B[k] = B[m - k] = Complex.Conjugate(w[k]);
        Radix2(A, false);
        Radix2(B, false);
        for (int i = 0; i < m; i++) A[i] *= B[i];
        Radix2(A, true);
        for (int k = 0; k < n; k++)
        {
            a[k] = A[k] * w[k];
            if (inverse) a[k] /= n;
        }
    }
}
