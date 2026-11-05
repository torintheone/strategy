namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>technologies.json: ветви, уровни по порядку, известные эффекты, требования — существующие технологии более раннего или того же уровня.</summary>
    static void ValidateScience(GameData data, Action<string, string> add)
    {
        const string file = "technologies.json";
        var branches = data.Science.Branches.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        if (branches.Count != data.Science.Branches.Count)
            add(file, "ветви повторяются");
        var b = data.ScienceBalance;
        var ids = new Dictionary<string, TechnologyRecord>(StringComparer.Ordinal);
        foreach (var t in data.Science.Technologies)
        {
            if (!ids.TryAdd(t.Id, t))
                add(file, $"технология {t.Id} встречается дважды");
            if (!branches.Contains(t.Branch))
                add(file, $"{t.Id}: нет ветви «{t.Branch}»");
            if (t.Tier < 1 || t.Tier > b.TierCost.Count || t.Tier > b.Start.Count)
                add(file, $"{t.Id}: уровень — от 1 до {Math.Min(b.TierCost.Count, b.Start.Count)}");
            foreach (var key in t.Effects.Keys.Where(k => !Laws.LawModel.IsKnownEffect(data, k)))
                add(file, $"{t.Id}: модель не знает эффекта «{key}»");
        }
        foreach (var t in data.Science.Technologies)
        {
            if (t.Tier > 1 && !data.Science.Technologies.Any(x => x.Branch == t.Branch && x.Tier == t.Tier - 1))
                add(file, $"{t.Id}: в ветви нет уровня {t.Tier - 1}");
            foreach (var r in t.Requires ?? [])
                if (!ids.TryGetValue(r, out var need))
                    add(file, $"{t.Id}: нет технологии «{r}»");
                else if (need.Tier > t.Tier)
                    add(file, $"{t.Id}: требует «{r}» более высокого уровня");
        }
    }
}
