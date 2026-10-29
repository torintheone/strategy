using Politeconomy.Sim.Laws;

namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>Законы (laws.json) и коэффициенты раздела laws.</summary>
    static void ValidateLaws(GameData data, Action<string, string> add)
    {
        const string file = "laws.json";
        var laws = data.Laws;
        var groups = laws.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var posts = data.Government.Posts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var classes = data.Classes.Classes.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var countries = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in laws.Effects.Keys.Where(k => !LawModel.IsKnownEffect(data, k)))
            add(file, $"эффект «{key}» модель не умеет применять");
        if (laws.Laws.Count != 40)
            add(file, $"законов {laws.Laws.Count}, на этапе 3 — 40");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var law in laws.Laws)
        {
            if (!ids.Add(law.Id))
                add(file, $"закон {law.Id} встречается дважды");
            if (!groups.Contains(law.Group))
                add(file, $"{law.Id}: группы «{law.Group}» нет в groups");
            if (!posts.Contains(law.Post))
                add(file, $"{law.Id}: поста «{law.Post}» нет в government.json");
            if (law.DraftingMonths < 1 || law.TransitionMonths < 0)
                add(file, $"{law.Id}: подготовка — хотя бы месяц, переходный период — не меньше нуля");
            if (law.Variants.Count < 2)
                add(file, $"{law.Id}: нужно хотя бы два варианта");
            var variants = law.Variants.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            if (variants.Count != law.Variants.Count)
                add(file, $"{law.Id}: варианты с одинаковым id");
            foreach (var v in law.Variants)
            {
                foreach (var key in v.Effects.Keys.Where(k => !laws.Effects.ContainsKey(k)))
                    add(file, $"{law.Id}.{v.Id}: эффект «{key}» не описан в effects");
                foreach (var (cls, value) in v.Attitudes)
                {
                    if (!classes.Contains(cls))
                        add(file, $"{law.Id}.{v.Id}: класса «{cls}» нет в classes.json");
                    if (value is < -3 or > 3)
                        add(file, $"{law.Id}.{v.Id}: отношение класса {cls} вне −3…+3");
                }
            }
            var s = law.Start;
            var starts = new[] { s.Default }
                .Concat(s.Income?.Values ?? []).Concat(s.System?.Values ?? []).Concat(s.Regime?.Values ?? []).Concat(s.Countries?.Values ?? []);
            foreach (var v in starts.Where(v => !variants.Contains(v)))
                add(file, $"{law.Id}: стартовый вариант «{v}» не найден");
            foreach (var c in (s.Countries?.Keys ?? []).Where(c => !countries.Contains(c)))
                add(file, $"{law.Id}: страна {c} не найдена в countries.json");
            foreach (var k in (s.Income?.Keys ?? []).Where(k => !IncomeKeys.Contains(k)))
                add(file, $"{law.Id}: группа дохода «{k}» неизвестна");
            foreach (var k in (s.Regime?.Keys ?? []).Where(k => k is not ("democracy" or "hybrid" or "authoritarian")))
                add(file, $"{law.Id}: режим «{k}» неизвестен");
            foreach (var k in (s.System?.Keys ?? []).Where(k => !data.Politics.Systems.ContainsKey(k)))
                add(file, $"{law.Id}: тип системы «{k}» неизвестен");
        }

        var b = data.LawsBalance;
        if (data.Balance.Laws is null)
            add("balance.json", "нет раздела laws с коэффициентами законов и институтов");
        foreach (var (name, list) in new[] { ("democracy", b.Democracy), ("rule_of_law", b.RuleOfLaw), ("judicial_independence", b.JudicialIndependence),
                     ("press_freedom", b.PressFreedom), ("civil_liberties", b.CivilLiberties), ("corruption_regime", b.CorruptionRegime) })
            if (list.Count != 3)
                add("balance.json", $"laws.{name}: три числа — демократия, гибридный, авторитарный");
        if (b.Corruption.Count != 4)
            add("balance.json", "laws.corruption: четыре числа — по группам дохода");
    }
}
