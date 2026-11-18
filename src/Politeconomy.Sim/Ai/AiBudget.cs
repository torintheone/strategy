using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Ai;

/// <summary>
/// Бюджет ИИ (1 января, после автопилота, который уже выбрал общий размер расходов под свой долг). ИИ меняет
/// состав: доли статей без прошлогоднего сдвига × (1 + Tilt × перевес приоритета над средним) у статей, которые этому
/// приоритету служат; перед выборами добавляет соцрасходы; в долговом кризисе или если по программе Валютного совета
/// дефицит выше цели — режет расходы. Прошлогодний сдвиг снимается, поэтому сдвиги не копятся. MODEL.md, «ИИ стран».
/// </summary>
public static class AiBudget
{
    /// <summary>Каким приоритетам служит статья расходов.</summary>
    static readonly Dictionary<string, int[]> Serves = new(StringComparer.Ordinal)
    {
        ["social"] = [AiModel.Stability, AiModel.Reelection],
        ["health"] = [AiModel.Stability, AiModel.Reelection],
        ["education"] = [AiModel.Growth],
        ["science"] = [AiModel.Growth],
        ["transport"] = [AiModel.Growth],
        ["energy"] = [AiModel.Growth],
        ["industry"] = [AiModel.Growth],
        ["agriculture"] = [AiModel.Stability, AiModel.Reelection],
        ["defense"] = [AiModel.Power],
        ["police"] = [AiModel.Power, AiModel.Stability],
        ["culture"] = [AiModel.Reelection],
    };

    public static void Plan(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        var items = world.Data.Budget.Spending;
        var e = world.Economy![turn.Id];
        var plan = e.SpendingPlan;
        if (plan.Length != items.Count)
            return;
        // Прошлогодний сдвиг снимается, новый — накладывается: перекосы не копятся, а чужие правки (законы, игрок) остаются.
        var last = turn.Ai.Tilt.Length == plan.Length ? turn.Ai.Tilt : Enumerable.Repeat(1.0, plan.Length).ToArray();
        var total = plan.Sum();
        var w = turn.Ai.Priorities;
        var tilt = new double[items.Count];
        for (var k = 0; k < items.Count; k++)
        {
            var shift = Serves.TryGetValue(items[k].Id, out var serves) ? serves.Sum(p => (w[p] - 0.25) * 4) / serves.Length : 0;
            tilt[k] = Math.Clamp(1 + b.Tilt * shift, 1 - b.Tilt, 1 + b.Tilt);
            // Армию и полицию ИИ не урезает: недовольные силовики — это перевороты.
            if (items[k].Id is "defense" or "police")
                tilt[k] = Math.Max(1, tilt[k]);
        }
        var weights = plan.Select((x, k) => x / last[k] * tilt[k]).ToArray();
        var sum = weights.Sum();
        if (sum <= 0 || total <= 0)
            return;
        var next = weights.Select(x => total * x / sum).ToArray();
        var notes = new List<string>();
        var more = Enumerable.Range(0, items.Count).Where(k => tilt[k] > 1.03).Select(k => items[k].Name.ToLowerInvariant()).ToList();
        var less = Enumerable.Range(0, items.Count).Where(k => tilt[k] < 0.97).Select(k => items[k].Name.ToLowerInvariant()).ToList();
        if (more.Count > 0)
            notes.Add("больше денег — " + string.Join(", ", more));
        if (less.Count > 0)
            notes.Add("меньше — " + string.Join(", ", less));

        var s = AiModel.Assess(world, turn.Country);
        var social = items.ToList().FindIndex(x => x.Id == "social");
        if (s.ElectionSoon && social >= 0)
        {
            var extra = b.Generosity / 100 * Math.Min(1, turn.Ai.Reelection / 0.4);
            next[social] += extra;
            notes.Add($"перед выборами пособия и пенсии щедрее на {Fmt.Number(extra * 100, 1)}% ВВП");
        }
        var program = world.Diplomacy?.Programs.GetValueOrDefault(turn.Id);
        var behind = program is { Status: "active" } && -e.BalanceRatio * 100 > world.Data.DiplomacyBalance.FundDeficit;
        if (s.DebtCrisis(b) || behind)
        {
            var floor = world.Data.Economy.Budget.SpendingFloor;
            var target = Math.Max(floor, next.Sum() - b.Austerity / 100 * (behind ? 2 : 1));
            var scale = target / next.Sum();
            for (var k = 0; k < next.Length; k++)
                next[k] *= scale;
            notes.Add(behind ? "режет расходы: дефицит выше условия Валютного совета" : "режет расходы: долг слишком дорог");
        }
        e.SpendingPlan = next;
        turn.Ai.Tilt = tilt;
        turn.Record("budget", $"Бюджет на {world.Date.Year} год: " + (notes.Count > 0 ? string.Join("; ", notes) : "как в прошлом году") + ".",
            "приоритеты: " + AiModel.Describe(turn.Ai));
    }
}
