namespace Politeconomy.Sim.Data;

// Коэффициенты смены власти без выборов из data/balance.json (раздел power). Смысл — в MODEL.md, «Смена власти без выборов».

public sealed class PowerBalance
{
    /// <summary>Вотум недоверия: вероятность в месяц = NoConfidence × (1 у правительства меньшинства + злость при одобрении ниже ApprovalLow + беспорядки / UnrestScale).</summary>
    public double NoConfidence { get; init; } = 0.015;
    public double ApprovalLow { get; init; } = 40;
    public double ApprovalScale { get; init; } = 20;
    public double UnrestScale { get; init; } = 50;
    /// <summary>Сколько месяцев после формирования правительства его не трогают.</summary>
    public int GraceMonths { get; init; } = 6;
    /// <summary>После переворота новая власть держится дольше: элиты получили своё (прибавка CoupAppeasement к их одобрению), CoupGrace месяцев покоя.</summary>
    public int CoupGrace { get; init; } = 24;
    public double CoupAppeasement { get; init; } = 15;
    /// <summary>Партнёры по коалиции голосуют против правительства с вероятностью (Defect − одобрение) / DefectScale, не больше DefectMax.</summary>
    public double Defect { get; init; } = 45;
    public double DefectScale { get; init; } = 40;
    public double DefectMax { get; init; } = 0.6;
    /// <summary>Импичмент: вероятность в месяц = Impeachment × (ImpeachScandal × скандал + злость при одобрении ниже ImpeachApproval); нужно две трети голосов.</summary>
    public double Impeachment { get; init; } = 0.01;
    public double ImpeachScandal { get; init; } = 2;
    public double ImpeachApproval { get; init; } = 25;
    public double ImpeachScale { get; init; } = 15;
    /// <summary>Дворцовый переворот: вероятность = PalaceCoup × ((EliteLow − одобрение элит) / EliteScale + (LegitimacyLow − легитимность) / LegitimacyScale); при гибридном режиме × HybridCoup.</summary>
    public double PalaceCoup { get; init; } = 0.01;
    public double EliteLow { get; init; } = 40;
    public double EliteScale { get; init; } = 20;
    public double LegitimacyLow { get; init; } = 35;
    public double LegitimacyScale { get; init; } = 20;
    public double HybridCoup { get; init; } = 0.5;
    /// <summary>Склонность к переворотам по группам дохода (бедные страны — чаще): множитель для дворцового и военного переворота.</summary>
    public IReadOnlyList<double> CoupProne { get; init; } = [3, 1.5, 0.7, 0.3];
    /// <summary>Группы элит для дворцового переворота.</summary>
    public IReadOnlyList<string> Elites { get; init; } = ["security", "industrialists", "financiers", "resource_lobby"];
    /// <summary>Военный переворот: вероятность = MilitaryCoup × (ArmyLow − лояльность армии) / ArmyScale × (1 + злость от легитимности + беспорядки / CoupUnrest); у демократии × DemocracyCoup, у устойчивой (индекс демократии от StableDemocracy) × StableCoup.</summary>
    public double MilitaryCoup { get; init; } = 0.01;
    public double ArmyLow { get; init; } = 40;
    public double ArmyScale { get; init; } = 20;
    public double CoupUnrest { get; init; } = 40;
    public double DemocracyCoup { get; init; } = 0.5;
    public double StableDemocracy { get; init; } = 70;
    public double StableCoup { get; init; } = 0.2;
    /// <summary>Хунта обещает вернуть власть гражданским с вероятностью HandOverChance через HandOverYears (от, до) лет.</summary>
    public double HandOverChance { get; init; } = 0.6;
    public IReadOnlyList<int> HandOverYears { get; init; } = [2, 5];
    /// <summary>Репрессии после переворота.</summary>
    public double CoupRepression { get; init; } = 20;
    /// <summary>Революция: восстание свергает власть с вероятностью Revolution × (1 − лояльность армии / 100) в месяц.</summary>
    public double Revolution { get; init; } = 0.2;
    /// <summary>После революции: радикализм всех классов снижается, выборы — через ElectionMonths.</summary>
    public double RevolutionCalm { get; init; } = 8;
    public int ElectionMonths { get; init; } = 6;
    /// <summary>После передачи власти гражданским выборы — через HandOverElection месяцев.</summary>
    public int HandOverElection { get; init; } = 3;
}
