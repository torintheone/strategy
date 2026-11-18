namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>Группы интересов (interest_groups.json): ссылки на классы, отрасли, статьи бюджета, законы и эффекты.</summary>
    static void ValidateGroups(GameData data, Action<string, string> add)
    {
        const string file = "interest_groups.json";
        var classes = data.Classes.Classes.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var industries = data.Industries.Industries.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var spending = data.Budget.Spending.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in data.InterestGroups.Groups)
        {
            if (!ids.Add(g.Id))
                add(file, $"группа {g.Id} встречается дважды");
            if (!HexColor().IsMatch(g.Color))
                add(file, $"{g.Id}: цвет «{g.Color}» не в формате #rrggbb");
            if (g.Lean.Count != 3 || g.Lean.Any(x => x is < -100 or > 100))
                add(file, $"{g.Id}: lean — три числа от −100 до 100");
            if (g.Regime.Count != 3 || g.Regime.Any(x => x < 0))
                add(file, $"{g.Id}: regime — три неотрицательных множителя (демократия, гибридный, авторитарный)");
            if (g.Classes.Count == 0 && g.Industries is not { Count: > 0 })
                add(file, $"{g.Id}: группа не опирается ни на классы, ни на отрасли");
            foreach (var (c, w) in g.Classes)
                if (!classes.Contains(c) || w <= 0 || w > 1)
                    add(file, $"{g.Id}: класс «{c}» не найден или доля не в пределах 0–1");
            foreach (var i in g.Industries ?? [])
                if (!industries.Contains(i))
                    add(file, $"{g.Id}: отрасль «{i}» не найдена в industries.json");
            foreach (var s in g.Spending)
                if (!spending.Contains(s))
                    add(file, $"{g.Id}: статья «{s}» не найдена в budget.json");
            foreach (var (law, variants) in g.Laws)
            {
                var record = data.Laws.Laws.FirstOrDefault(l => l.Id == law);
                if (record == null)
                    add(file, $"{g.Id}: закон «{law}» не найден в laws.json");
                else
                    foreach (var v in variants.Keys.Where(v => record.Variants.All(x => x.Id != v)))
                        add(file, $"{g.Id}: у закона {law} нет варианта «{v}»");
            }
            foreach (var key in g.Effects.Keys.Where(k => !Laws.LawModel.IsKnownEffect(data, k)))
                add(file, $"{g.Id}: модель не знает эффекта «{key}»");
            if (string.IsNullOrWhiteSpace(g.Pressure) || string.IsNullOrWhiteSpace(g.Support))
                add(file, $"{g.Id}: нет описания давления (pressure) или помощи (support)");
        }
        var r = data.InterestGroups.Religiosity;
        if (r.Default is < 0 or > 1 || r.Subregions.Values.Concat(r.Countries.Values).Any(v => v is < 0 or > 1))
            add(file, "религиозность — от 0 до 1");
        var countries = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in r.Countries.Keys.Where(c => !countries.Contains(c)))
            add(file, $"религиозность: страна {c} не найдена");
        var b = data.GroupsBalance;
        if (b.Angry >= b.Calm || b.FundAngry >= b.FundHappy || b.ApprovalMonths < 1)
            add("balance.json", "interest_groups: angry < calm, fund_angry < fund_happy, approval_months ≥ 1");
    }

    /// <summary>Программы ведомств (programs.json): посты, ключи эффектов, стартовые значения по группам дохода.</summary>
    static void ValidatePrograms(GameData data, Action<string, string> add)
    {
        const string file = "programs.json";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in data.Programs.Programs)
        {
            if (!ids.Add(p.Id))
                add(file, $"программа {p.Id} встречается дважды");
            if (data.Government.Posts.All(x => x.Id != p.Post))
                add(file, $"{p.Id}: нет поста «{p.Post}» в government.json");
            if (p.Cost < 0 || p.Cost > 3)
                add(file, $"{p.Id}: цена — от 0 до 3% ВВП");
            if (p.Start.Count != 4)
                add(file, $"{p.Id}: start — четыре значения по группам дохода");
            foreach (var key in p.Effects.Keys.Where(k => !Laws.LawModel.IsKnownEffect(data, k)))
                add(file, $"{p.Id}: модель не знает эффекта «{key}»");
        }
    }
}
