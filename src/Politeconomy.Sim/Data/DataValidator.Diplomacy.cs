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
    }
}
