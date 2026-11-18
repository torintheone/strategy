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
    /// <summary>Мирные договоры (действующие и прошлые).</summary>
    public List<PeaceTreaty> Treaties { get; set; } = [];
    /// <summary>Предложения мира игроку, которые ждут его решения.</summary>
    public List<PeaceOffer> Offers { get; set; } = [];
    /// <summary>
    /// Переданные по договору регионы: регион → у кого он учтён в экономике (новый владелец; при передаче экономика
    /// региона перенесена явно, и выпуск от занятых земель его больше не считает).
    /// </summary>
    public SortedDictionary<string, string> Baseline { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Соседи регионов, их население и площадь, население стран по регионам, столицы — из данных (не сохраняются).</summary>
    [JsonIgnore] public WarMap Map { get; set; } = WarMap.Empty;
    /// <summary>Сила армий, посчитанная в начале месяца (сохраняется: потери и передачи регионов за месяц меняют армию, но не силу до следующего месяца).</summary>
    public SortedDictionary<string, double> Strength { get; set; } = new(StringComparer.Ordinal);

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

/// <summary>
/// Перемирие двух стран: боёв нет, линия соприкосновения заморожена, война может возобновиться. Capitulated — кто
/// капитулировал (если так кончилась война); Talks — когда в последний раз пробовали договориться о мире.
/// </summary>
public sealed record Ceasefire(string A, string B, DateOnly Since, string? Capitulated = null, DateOnly? Talks = null)
{
    public bool Between(string x, string y) => (A == x && B == y) || (A == y && B == x);
    public string Other(string country) => A == country ? B : A;
}

/// <summary>
/// Условия мира, которые уступающий (Loser) даёт получающему (Winner): регионы (Annex — земли уступающего, которые
/// держит получающий), репарации (% ВВП уступающего в год), торговые уступки (снять санкции, свободная торговля),
/// нейтралитет. Остальное занятое обе стороны возвращают. Без условий — белый мир.
/// </summary>
public sealed record PeaceTerms(List<string> Annex, double Reparations, bool Trade, bool Neutrality)
{
    public static PeaceTerms White => new([], 0, false, false);
    public bool IsWhite => Annex.Count == 0 && Reparations <= 0 && !Trade && !Neutrality;
}

/// <summary>Предложение мира игроку: от кого, кому, кто получает условия, кто уступает, условия, когда предложено.</summary>
public sealed record PeaceOffer(string From, string To, string Winner, string Loser, PeaceTerms Terms, DateOnly Date);

/// <summary>Мирный договор: стороны, условия, до какой даты действуют мир (и нейтралитет) и репарации, сколько выплачено (долл.).</summary>
public sealed class PeaceTreaty
{
    public int Id { get; set; }
    public string Winner { get; set; } = "";
    public string Loser { get; set; } = "";
    public DateOnly Date { get; set; }
    public PeaceTerms Terms { get; set; } = PeaceTerms.White;
    public DateOnly Until { get; set; }
    public DateOnly? ReparationsUntil { get; set; }
    public double Paid { get; set; }
    /// <summary>Условия словами — для журнала и экрана.</summary>
    public string Text { get; set; } = "";

    public bool Between(string x, string y) => (Winner == x && Loser == y) || (Winner == y && Loser == x);
}
