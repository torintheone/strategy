namespace Politeconomy.Sim.Politics;

/// <summary>
/// Группа интересов в стране: влияние (доля среди групп, 0–1), одобрение власти (0–100, 50 — нейтрально) и из чего
/// оно сложилось, взгляды группы на трёх осях и текущие требования. MODEL.md, «Группы интересов».
/// </summary>
public sealed class InterestGroup
{
    public string Id { get; set; } = "";
    public double Influence { get; set; }
    public double Approval { get; set; } = 50;
    public double[] Position { get; set; } = new double[3];
    /// <summary>Составляющие цели одобрения: настроение классов, законы, бюджет, правительство, выполненные требования.</summary>
    public double FromClasses { get; set; }
    public double FromLaws { get; set; }
    public double FromBudget { get; set; }
    public double FromGovernment { get; set; }
    /// <summary>Память о выполненных требованиях: прибавка к одобрению, понемногу тает.</summary>
    public double Memory { get; set; }
    public List<GroupDemand> Demands { get; set; } = [];
}

/// <summary>
/// Требование группы: принять закон (Kind = "law": Target — закон, Variant — вариант), поднять расходы по статье
/// (Kind = "spending": Target — статья, Level — нужный уровень расходов) или сократить дефицит (Kind = "deficit":
/// Level — допустимый дефицит, % ВВП).
/// </summary>
public sealed class GroupDemand
{
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Variant { get; set; }
    public double Level { get; set; }
    public DateOnly Since { get; set; }
}
