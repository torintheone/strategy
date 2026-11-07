using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Military;

/// <summary>Армии стран, идущие войны и перемирия. MODEL.md, «Армии и войны».</summary>
public sealed class WorldWar
{
    public SortedDictionary<string, Army> Armies { get; set; } = new(StringComparer.Ordinal);
    public List<War> Wars { get; set; } = [];
    public List<Ceasefire> Ceasefires { get; set; } = [];
    /// <summary>Законченные войны: кто, с кем, когда, чем кончилось.</summary>
    public List<WarRecord> History { get; set; } = [];
    public int NextId { get; set; } = 1;

    /// <summary>Соседи регионов, их население и площадь, население стран по регионам, столицы — из данных (не сохраняются).</summary>
    [JsonIgnore] public WarMap Map { get; set; } = WarMap.Empty;
    /// <summary>Доля выпуска от земель на старте (занятые на 1 января 2026 года — уже в данных экономики).</summary>
    [JsonIgnore] public Dictionary<string, double> StartTerritory { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Сила армий за этот месяц (не сохраняется: пересчитывается из армий).</summary>
    [JsonIgnore] public Dictionary<string, double> Strength { get; set; } = new(StringComparer.Ordinal);

    public Army this[string country] => Armies[country];
}

/// <summary>
/// Армия страны: солдаты (мирная численность — Base), оснащение (долл.), боевой дух, усталость от войны (0–100),
/// разрушения (доля капитала), потери за всё время; поправки к модели (оборона, рост, ставки, легитимность),
/// настроение классов и множитель выпуска (земли и разрушения в сравнении со стартом).
/// </summary>
public sealed class Army
{
    public double Soldiers { get; set; }
    public double Base { get; set; }
    public double Equipment { get; set; }
    public double Morale { get; set; } = 70;
    public double Exhaustion { get; set; }
    public double Damage { get; set; }
    public double Losses { get; set; }
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
    public double[] Mood { get; set; } = [];
    public double Output { get; set; } = 1;
}

/// <summary>
/// Война: нападающие и обороняющиеся (первые в списках — главные стороны), цель (регионы, за которые спор), когда
/// началась, фронт (регион → продвижение противника к его взятию, 0–100), потери сторон, новости, кто и когда
/// предложил перемирие.
/// </summary>
public sealed class War
{
    public int Id { get; set; }
    public List<string> Attackers { get; set; } = [];
    public List<string> Defenders { get; set; } = [];
    public List<string> Goal { get; set; } = [];
    public DateOnly Start { get; set; }
    public SortedDictionary<string, double> Front { get; set; } = new(StringComparer.Ordinal);
    public double AttackerLosses { get; set; }
    public double DefenderLosses { get; set; }
    public List<WarNews> News { get; set; } = [];
    public string? Offer { get; set; }
    public DateOnly? OfferDate { get; set; }

    public string Attacker => Attackers[0];
    public string Defender => Defenders[0];
    public bool Involves(string country) => Attackers.Contains(country) || Defenders.Contains(country);
    /// <summary>1 — нападающие, 2 — обороняющиеся, 0 — не участвует.</summary>
    public int Side(string? country) => country == null ? 0 : Attackers.Contains(country) ? 1 : Defenders.Contains(country) ? 2 : 0;
    public bool IsMain(string country) => country == Attacker || country == Defender;
}

public sealed record WarNews(DateOnly Date, string Text);

/// <summary>Законченная война: стороны, начало и конец, итог, потери сторон, сколько регионов сменили контроль.</summary>
public sealed record WarRecord(int Id, string Attacker, string Defender, DateOnly Start, DateOnly End, string Result,
    double AttackerLosses, double DefenderLosses, int Regions);

/// <summary>Перемирие двух стран: боёв нет, линия соприкосновения заморожена, война может возобновиться.</summary>
public sealed record Ceasefire(string A, string B, DateOnly Since)
{
    public bool Between(string x, string y) => (A == x && B == y) || (A == y && B == x);
}
