namespace Politeconomy.Sim.Politics;

/// <summary>
/// Протест класса: стадия (0 — забастовка, 1 — митинги, 2 — массовые протесты, 3 — беспорядки, 4 — восстание),
/// повод (mood — недовольство жизнью, law — закон, fraud — фальсификации, arrests — аресты оппозиции) и требование
/// (для закона — вернуть прежний вариант). Heat — накал от повода, Anger — гнев после разгона, Calm — успокоение после
/// уступок и переговоров; все три со временем остывают. MODEL.md, «Протесты».
/// </summary>
public sealed class Protest
{
    public int Id { get; set; }
    public int Class { get; set; }
    public int Stage { get; set; }
    public string Cause { get; set; } = "mood";
    /// <summary>Требование отменить закон: какой закон и к какому варианту вернуться.</summary>
    public string? Law { get; set; }
    public string? Variant { get; set; }
    public DateOnly Started { get; set; }
    public int Months { get; set; }
    public double Heat { get; set; }
    public double Anger { get; set; }
    public double Calm { get; set; }
    /// <summary>Когда власть отвечала в последний раз (не чаще раза в месяц) и как.</summary>
    public DateOnly? Answered { get; set; }
    public string? Response { get; set; }
}

/// <summary>
/// Силовики страны: нагрузка на полицию и армию от разгонов (снижает лояльность, остывает), уровень репрессий
/// (0–100, тает) и когда в последний раз арестовывали оппозицию. Лояльность считается каждый месяц.
/// </summary>
public sealed class Security
{
    public double PoliceLoyalty { get; set; } = 50;
    public double ArmyLoyalty { get; set; } = 50;
    public double PoliceStrain { get; set; }
    public double ArmyStrain { get; set; }
    public double Repression { get; set; }
    public DateOnly? Arrests { get; set; }
}
