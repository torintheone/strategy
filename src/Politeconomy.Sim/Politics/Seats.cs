namespace Politeconomy.Sim.Politics;

/// <summary>
/// Распределение мест в парламенте по голосам. Пропорциональная система — метод д’Онта среди партий, прошедших
/// барьер; мажоритарная — доля мест пропорциональна доле голосов в степени power (крупные партии выигрывают округа);
/// смешанная — половина мест так, половина так. MODEL.md, «Партии».
/// </summary>
public static class Seats
{
    public static int[] Allocate(string electoral, IReadOnlyList<double> votes, int seats, double threshold, double power) =>
        electoral switch
        {
            "majoritarian" => Majoritarian(votes, seats, power),
            "mixed" => Add(Proportional(votes, seats / 2, threshold), Majoritarian(votes, seats - seats / 2, power)),
            _ => Proportional(votes, seats, threshold),
        };

    /// <summary>Метод д’Онта. Threshold — барьер в процентах голосов; если его не прошёл никто, барьера нет.</summary>
    public static int[] Proportional(IReadOnlyList<double> votes, int seats, double threshold)
    {
        var n = votes.Count;
        var result = new int[n];
        var total = votes.Sum();
        if (seats <= 0 || n == 0 || total <= 0)
            return result;
        var passed = Enumerable.Range(0, n).Where(i => votes[i] / total * 100 >= threshold).ToList();
        if (passed.Count == 0)
            passed = Enumerable.Range(0, n).ToList();
        for (var s = 0; s < seats; s++)
        {
            var best = passed[0];
            foreach (var i in passed)
                if (votes[i] / (result[i] + 1) > votes[best] / (result[best] + 1))
                    best = i;
            result[best]++;
        }
        return result;
    }

    /// <summary>Мажоритарная система: доли мест ∝ голоса^power, округление по наибольшим остаткам.</summary>
    public static int[] Majoritarian(IReadOnlyList<double> votes, int seats, double power) =>
        LargestRemainder(votes.Select(v => Math.Pow(Math.Max(0, v), power)).ToArray(), seats);

    /// <summary>Места пропорционально весам; остатки достаются тем, у кого дробная часть больше.</summary>
    public static int[] LargestRemainder(IReadOnlyList<double> weights, int seats)
    {
        var n = weights.Count;
        var result = new int[n];
        var total = weights.Sum();
        if (seats <= 0 || n == 0 || total <= 0)
            return result;
        var exact = weights.Select(w => w / total * seats).ToArray();
        for (var i = 0; i < n; i++)
            result[i] = (int)Math.Floor(exact[i]);
        var left = seats - result.Sum();
        foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => exact[i] - result[i]).ThenBy(i => i).Take(left))
            result[i]++;
        return result;
    }

    static int[] Add(int[] a, int[] b) => a.Select((x, i) => x + b[i]).ToArray();
}
