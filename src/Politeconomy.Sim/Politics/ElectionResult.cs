namespace Politeconomy.Sim.Politics;

/// <summary>
/// Итог выборов. Kind — parliament (парламент) или president (президент). Votes — доли партий (у президентских —
/// доли их кандидатов в первом туре), Runoff — второй тур (два кандидата), Seats — места (парламент), Candidates —
/// имена кандидатов по партиям, Winner — партия победителя (у парламентских — крупнейшая), Regions — кто победил
/// в каждом регионе. PowerChanged — сменилась ли власть.
/// </summary>
public sealed class ElectionResult
{
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "parliament";
    public double Turnout { get; set; }
    public Dictionary<string, double> Votes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double>? Runoff { get; set; }
    public Dictionary<string, int> Seats { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Candidates { get; set; } = new(StringComparer.Ordinal);
    public string? Winner { get; set; }
    public Dictionary<string, string> Regions { get; set; } = new(StringComparer.Ordinal);
    public bool PowerChanged { get; set; }
}

/// <summary>
/// Переговоры о коалиции в стране игрока: после выборов у партии главы нет большинства. Предложения партий
/// (Offers) действуют до Deadline; если игрок не решит, коалицию соберут без него.
/// </summary>
public sealed class CoalitionTalks
{
    public DateOnly Date { get; set; }
    public DateOnly Deadline { get; set; }
    public List<CoalitionOffer> Offers { get; set; } = [];
}

/// <summary>
/// Условия партии для вступления в коалицию: сколько постов она попросит (на каждые её места), какой закон
/// потребует принять (Law, Variant; может не быть) и отказывается ли она вообще (слишком далека по взглядам).
/// </summary>
public sealed class CoalitionOffer
{
    public string Party { get; set; } = "";
    public int Seats { get; set; }
    public string? Law { get; set; }
    public string? Variant { get; set; }
    public bool Refuses { get; set; }
}

/// <summary>Обещание партнёру по коалиции: принять закон в таком-то варианте до Deadline, иначе партия выйдет из коалиции.</summary>
public sealed class CoalitionPromise
{
    public string Party { get; set; } = "";
    public string Law { get; set; } = "";
    public string Variant { get; set; } = "";
    public DateOnly Deadline { get; set; }
}
