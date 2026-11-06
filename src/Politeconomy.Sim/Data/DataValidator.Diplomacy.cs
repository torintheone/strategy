namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>Виды договоров, которые знает модель.</summary>
    static readonly HashSet<string> TreatyTypes = ["free_trade", "investment", "visa_free", "alliance", "energy", "science"];

    /// <summary>diplomacy.json: договоры — те, что знает модель; история — существующие страны, разные, без повторов пар.</summary>
    static void ValidateDiplomacy(GameData data, Action<string, string> add)
    {
        const string file = "diplomacy.json";
        var types = data.Diplomacy.Treaties.Select(t => t.Id).ToList();
        if (!TreatyTypes.SetEquals(types) || types.Count != TreatyTypes.Count)
            add(file, $"договоры должны быть ровно такие: {string.Join(", ", TreatyTypes)}");
        foreach (var t in data.Diplomacy.Treaties.Where(t => t.MinRelations is < -100 or > 100))
            add(file, $"{t.Id}: порог отношений — от −100 до 100");
        var countries = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in data.Diplomacy.History)
        {
            if (!countries.Contains(h.A) || !countries.Contains(h.B) || h.A == h.B)
                add(file, $"история {h.A}–{h.B}: нужны две разные существующие страны");
            if (!pairs.Add(Diplomacy.WorldDiplomacy.Key(h.A, h.B)))
                add(file, $"история {h.A}–{h.B} встречается дважды");
            if (h.Value is < -100 or > 100)
                add(file, $"история {h.A}–{h.B}: от −100 до 100");
        }
        ValidateOrganizations(data, countries, add);
    }

    /// <summary>Виды резолюций, которые знает модель.</summary>
    static readonly HashSet<string> ResolutionTypes = ["condemn", "sanctions", "lift", "aid"];

    /// <summary>
    /// diplomacy.json: блоки, союзы, санкции, Совет Наций и ядерные державы ссылаются на существующие страны. Регионы
    /// блока ограничивают только новых членов (Турция в Атлантическом пакте — с 1952 года).
    /// </summary>
    static void ValidateOrganizations(GameData data, HashSet<string> countries, Action<string, string> add)
    {
        const string file = "diplomacy.json";
        var dip = data.Diplomacy;
        var regions = data.Countries.Countries.Select(c => c.Region).ToHashSet(StringComparer.Ordinal);
        var blocs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in dip.Blocs)
        {
            if (!blocs.Add(b.Id))
                add(file, $"блок {b.Id} встречается дважды");
            if (b.Kind is not ("trade" or "military"))
                add(file, $"блок {b.Id}: вид trade или military");
            if (!HexColor().IsMatch(b.Color))
                add(file, $"блок {b.Id}: цвет — #RRGGBB");
            if (b.Trade is < 0 or > 2 || (b.Kind == "military" && b.Trade != 0))
                add(file, $"блок {b.Id}: trade от 0 до 2, у военного блока — 0");
            foreach (var m in b.Members.Where(m => !countries.Contains(m)))
                add(file, $"блок {b.Id}: нет страны {m}");
            if (b.Members.Distinct().Count() != b.Members.Count)
                add(file, $"блок {b.Id}: страна в списке дважды");
            foreach (var r in b.Regions.Where(r => !regions.Contains(r)))
                add(file, $"блок {b.Id}: нет региона «{r}»");
        }
        foreach (var b in dip.Blocs.Where(b => b.Rival != null))
            if (dip.Bloc(b.Rival!) is not { Military: true } rival || rival.Rival != b.Id || !b.Military)
                add(file, $"блок {b.Id}: соперник — военный блок, который считает соперником его");
        var military = dip.Blocs.Where(b => b.Military).SelectMany(b => b.Members).GroupBy(m => m).Where(g => g.Count() > 1);
        foreach (var g in military)
            add(file, $"{g.Key} в нескольких военных блоках");
        foreach (var a in dip.Alliances.Where(a => !countries.Contains(a.A) || !countries.Contains(a.B) || a.A == a.B))
            add(file, $"союз {a.A}–{a.B}: нужны две разные существующие страны");
        foreach (var s in dip.Sanctions)
        {
            if (s.Level is not ("full" or "partial"))
                add(file, $"санкции против {s.Target}: уровень full или partial");
            if (!countries.Contains(s.Target))
                add(file, $"санкции: нет страны {s.Target}");
            foreach (var by in s.By.Where(by => !countries.Contains(by) || by == s.Target))
                add(file, $"санкции против {s.Target}: {by} — нужна другая существующая страна");
        }
        var council = dip.Council;
        foreach (var id in council.Permanent.Concat(council.Elected.Select(e => e.Country)).Concat(council.Sanctions).Concat(dip.Nuclear)
                     .Where(id => !countries.Contains(id)))
            add(file, $"Совет Наций и ядерные державы: нет страны {id}");
        if (council.Elected.Select(e => e.Country).Intersect(council.Permanent).Any())
            add(file, "выборный член Совета безопасности не может быть постоянным");
        var types = council.Resolutions.Select(r => r.Id).ToList();
        if (!ResolutionTypes.SetEquals(types) || types.Count != ResolutionTypes.Count)
            add(file, $"резолюции должны быть ровно такие: {string.Join(", ", ResolutionTypes)}");
        foreach (var r in council.Resolutions.Where(r => r.Chamber is not ("assembly" or "security")))
            add(file, $"резолюция {r.Id}: chamber — assembly или security");
    }
}
