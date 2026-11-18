namespace Politeconomy.Sim.Model;

/// <summary>
/// Генератор xoshiro256** со своим кодом: System.Random может поменять алгоритм в новой версии .NET,
/// а партия с тем же seed обязана повторяться точно.
/// </summary>
public sealed class DeterministicRandom
{
    ulong _s0, _s1, _s2, _s3;

    public DeterministicRandom(ulong seed)
    {
        // Состояние заполняется через SplitMix64, как рекомендуют авторы xoshiro.
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    DeterministicRandom(ulong[] state) => State = state;

    public static DeterministicRandom FromState(ulong[] state) => new(state);

    public ulong[] State
    {
        get => [_s0, _s1, _s2, _s3];
        private init
        {
            if (value.Length != 4)
                throw new ArgumentException("Состояние генератора — 4 числа", nameof(value));
            (_s0, _s1, _s2, _s3) = (value[0], value[1], value[2], value[3]);
        }
    }

    public ulong NextUInt64()
    {
        var result = RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Число в [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Целое в [min, max).</summary>
    public int Next(int min, int max)
    {
        if (max <= min)
            throw new ArgumentOutOfRangeException(nameof(max));
        return min + (int)(NextUInt64() % (ulong)(max - min));
    }

    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Нормальное распределение со средним 0 и разбросом 1 (метод Бокса — Мюллера, два числа на вызов).</summary>
    public double NextGaussian()
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

    static ulong SplitMix(ref ulong x)
    {
        var z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
