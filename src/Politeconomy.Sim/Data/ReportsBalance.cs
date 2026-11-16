namespace Politeconomy.Sim.Data;

/// <summary>
/// Честность докладов министров из data/balance.json (раздел reports). Слабый министр (компетентность ниже
/// HonestCompetence) приукрашивает цифры до WeakSpin, нечестный (коррупционность выше CorruptFrom) — до CorruptSpin;
/// черты «коррупционер» и «популист» прибавляют TraitSpin и PopulistSpin; всего не больше MaxSpin (доля значения,
/// на которую цифра в докладе лучше настоящей). Министр с компетентностью ниже BadAdviceCompetence даёт плохой совет.
/// MODEL.md, «Доклады министров».
/// </summary>
public sealed class ReportsBalance
{
    public double HonestCompetence { get; init; } = 45;
    public double WeakSpin { get; init; } = 0.2;
    public double CorruptFrom { get; init; } = 55;
    public double CorruptSpin { get; init; } = 0.3;
    public double TraitSpin { get; init; } = 0.1;
    public double PopulistSpin { get; init; } = 0.05;
    public double MaxSpin { get; init; } = 0.35;
    public double BadAdviceCompetence { get; init; } = 35;
}
