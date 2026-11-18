namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>
    /// military.json и terrain.json: армии, претензии и перемирия — у существующих стран и регионов; у каждого региона
    /// карты есть запись о соседях, соседи существуют и связи взаимны, доля гор — от 0 до 1.
    /// </summary>
    static void ValidateMilitary(GameData data, Action<string, string> add)
    {
        var countries = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var regions = data.Regions.Regions.ToDictionary(r => r.Id, StringComparer.Ordinal);
        const string military = "military.json";
        var peace = data.PeaceBalance;
        if (peace.ReparationLevels.Count == 0 || peace.ReparationLevels.Any(l => l <= 0) || !peace.ReparationLevels.SequenceEqual(peace.ReparationLevels.Order()))
            add("balance.json", "peace.reparation_levels: нужны положительные размеры репараций по возрастанию");
        if (peace.ReparationYears <= 0 || peace.PeaceYears <= 0 || peace.PeaceMonths <= 0 || peace.OfferMonths <= 0)
            add("balance.json", "peace: сроки (reparation_years, peace_years, peace_months, offer_months) должны быть больше нуля");
        foreach (var a in data.Military.Armies)
        {
            if (!countries.Contains(a.Country))
                add(military, $"армия: нет страны {a.Country}");
            if (a.Soldiers <= 0)
                add(military, $"армия {a.Country}: численность должна быть больше нуля");
        }
        foreach (var c in data.Military.Claims)
        {
            if (!countries.Contains(c.Claimant))
                add(military, $"претензия: нет страны {c.Claimant}");
            foreach (var r in c.Regions)
            {
                if (!regions.TryGetValue(r, out var region))
                    add(military, $"претензия {c.Claimant}: нет региона {r}");
                else if (region.Country == c.Claimant && !data.Control.Control.Any(x => x.Region == r && x.Controller != c.Claimant))
                    add(military, $"претензия {c.Claimant}: регион {r} и так её и под её контролем");
            }
        }
        foreach (var c in data.Military.Ceasefires)
        {
            if (!countries.Contains(c.A) || !countries.Contains(c.B) || c.A == c.B)
                add(military, $"перемирие {c.A}–{c.B}: нужны две разные существующие страны");
            if (!DateOnly.TryParse(c.Since, out _))
                add(military, $"перемирие {c.A}–{c.B}: дата {c.Since}");
        }
        const string terrain = "terrain.json";
        var links = data.Terrain.Regions.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var id in regions.Keys.Where(id => !links.ContainsKey(id)))
            add(terrain, $"нет записи о регионе {id} — пересоберите tools/build_terrain.py");
        foreach (var t in data.Terrain.Regions)
        {
            if (t.Mountains is < 0 or > 1)
                add(terrain, $"{t.Id}: доля гор от 0 до 1");
            foreach (var (n, kind) in t.Land.Select(n => (n, "Land")).Concat(t.Sea.Select(n => (n, "Sea"))))
            {
                if (!links.TryGetValue(n, out var other))
                    add(terrain, $"{t.Id}: нет соседа {n}");
                else if (!(kind == "Land" ? other.Land : other.Sea).Contains(t.Id))
                    add(terrain, $"{t.Id}–{n}: связь не взаимна");
            }
        }
    }
}
