namespace Politeconomy.Sim.Data;

// Коэффициенты законов и институтов из data/balance.json (раздел laws). Смысл — в MODEL.md, «Законы» и «Институты».
// Списки из трёх чисел — по режимам: демократия, гибридный, авторитарный; из четырёх — по группам дохода.

public sealed class LawsBalance
{
    /// <summary>Удовлетворённость класса меняется на столько пунктов за каждую единицу отношения к закону (−3…+3).</summary>
    public double AttitudeWeight { get; init; } = 1.5;
    /// <summary>Одобрение законопроекта в обществе: 0,5 + 0,5·th(сдвиг отношения, взвешенный по влиянию / SupportScale).</summary>
    public double SupportScale { get; init; } = 1.0;
    /// <summary>Подготовка: срок × (DraftingBase − компетентность/100), в пределах DraftingMin…DraftingMax; реформатор — × ReformerSpeed.</summary>
    public double DraftingBase { get; init; } = 1.6;
    public double DraftingMin { get; init; } = 0.6;
    public double DraftingMax { get; init; } = 1.4;
    public double ReformerSpeed { get; init; } = 0.75;
    /// <summary>Сколько завершённых законопроектов страна помнит (для экрана законов).</summary>
    public int History { get; init; } = 12;

    /// <summary>Индексы институтов на старте по режиму («практика»): законы сдвигают их от этого уровня.</summary>
    public IReadOnlyList<double> Democracy { get; init; } = [85, 50, 15];
    public IReadOnlyList<double> RuleOfLaw { get; init; } = [75, 42, 25];
    public IReadOnlyList<double> JudicialIndependence { get; init; } = [80, 40, 15];
    public IReadOnlyList<double> PressFreedom { get; init; } = [80, 40, 12];
    public IReadOnlyList<double> CivilLiberties { get; init; } = [85, 50, 20];
    /// <summary>Коррупция на старте: по группе дохода плюс поправка по режиму; министры-коррупционеры добавляют.</summary>
    public IReadOnlyList<double> Corruption { get; init; } = [70, 60, 48, 28];
    public IReadOnlyList<double> CorruptionRegime { get; init; } = [-6, 2, 10];
    public double CorruptionMinisters { get; init; } = 0.3;

    /// <summary>Одобрение власти, %: ApprovalBase + ApprovalSatisfaction × (удовлетворённость − 50) + ApprovalPopularity × (популярность главы − 45).</summary>
    public double ApprovalBase { get; init; } = 50;
    public double ApprovalSatisfaction { get; init; } = 1.5;
    public double ApprovalPopularity { get; init; } = 0.25;
}
