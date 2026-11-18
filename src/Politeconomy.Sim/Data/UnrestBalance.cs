namespace Politeconomy.Sim.Data;

// Коэффициенты протестов, силовиков и жёстких мер из data/balance.json (раздел unrest). Смысл — в MODEL.md, «Протесты».
// Списки из пяти чисел — по стадиям протеста: забастовка, митинги, массовые протесты, беспорядки, восстание.

public sealed class UnrestBalance
{
    /// <summary>Классы, которые не выходят на улицы (их недовольство идёт через группы интересов и элиты).</summary>
    public IReadOnlyList<string> Quiet { get; init; } = ["capitalists", "officials"];
    /// <summary>
    /// Давление класса = ((радикализм − Floor) / Scale + (Discontent − удовлетворённость) / DiscontentScale, от 0 до 2) ×
    /// (OrganizationBase + организованность).
    /// </summary>
    public double Floor { get; init; } = 6;
    public double Scale { get; init; } = 20;
    public double OrganizationBase { get; init; } = 0.4;
    /// <summary>Злость: удовлетворённость ниже Discontent добавляет к давлению (Discontent − удовлетворённость) / DiscontentScale.</summary>
    public double Discontent { get; init; } = 45;
    public double DiscontentScale { get; init; } = 25;
    /// <summary>Вероятность нового протеста в месяц на единицу давления; класс меньше SmallClass взрослых протестует реже.</summary>
    public double StartRate { get; init; } = 0.04;
    public double SmallClass { get; init; } = 0.03;
    /// <summary>Эскалация: вероятность = Escalate × (накал − EscalateFrom); затухание = Calm × (CalmFrom − накал).</summary>
    public double Escalate { get; init; } = 0.3;
    public double EscalateFrom { get; init; } = 0.35;
    public double Calm { get; init; } = 0.3;
    public double CalmFrom { get; init; } = 1.2;
    public double MaxChance { get; init; } = 0.6;
    /// <summary>Доля протестов, которые начинаются сразу с митингов; случайные новости месяца сдвигают накал (стандартное отклонение).</summary>
    public double StartRally { get; init; } = 0.3;
    public double HeatNoise { get; init; } = 0.3;
    /// <summary>Усталость: минус к накалу за каждый месяц протеста; как быстро остывают повод, гнев и успокоение.</summary>
    public double Fatigue { get; init; } = 0.04;
    public double HeatDecay { get; init; } = 0.8;
    /// <summary>До восстания доходит только класс с радикализмом не ниже этого.</summary>
    public double UprisingRadicalism { get; init; } = 35;
    /// <summary>Вес стадии в «силе» протеста и масштаб класса (доля взрослых, при которой масштаб — 1).</summary>
    public IReadOnlyList<double> Severity { get; init; } = [0.3, 0.5, 1, 2, 3.5];
    public double SizeScale { get; init; } = 0.15;
    /// <summary>Сколько людей класса выходит на каждой стадии (доля взрослых класса).</summary>
    public IReadOnlyList<double> Turnout { get; init; } = [0.03, 0.02, 0.08, 0.12, 0.2];
    /// <summary>Цена протеста в месяц: удар по выпуску (доля разрыва выпуска), легитимности, спред по долгу (п. п.), дрейф правящих.</summary>
    public IReadOnlyList<double> Output { get; init; } = [0.0015, 0.0003, 0.0008, 0.002, 0.004];
    public IReadOnlyList<double> Legitimacy { get; init; } = [0.5, 1, 3, 6, 12];
    public IReadOnlyList<double> Spread { get; init; } = [0, 0, 0.1, 0.3, 0.8];
    public double RulingDrift { get; init; } = 0.01;
    /// <summary>Индекс беспорядков = UnrestIndex × сумма сил протестов (не больше 100).</summary>
    public double UnrestIndex { get; init; } = 20;

    /// <summary>Повод: закон, который ухудшает отношение класса хотя бы на LawShift, выводит его на митинг с вероятностью LawChance + LawOrganization × организованность.</summary>
    public double LawShift { get; init; } = 2;
    public double LawChance { get; init; } = 0.3;
    public double LawOrganization { get; init; } = 0.6;
    /// <summary>Накал от повода (закон, фальсификации, аресты) в начале протеста.</summary>
    public double CauseHeat { get; init; } = 1;

    /// <summary>Уступки: прибавка к расходам на статью класса (доля ВВП × масштаб), к настроению класса, успокоение.</summary>
    public double ConcessionSpending { get; init; } = 0.002;
    public double ConcessionMood { get; init; } = 6;
    public double ConcessionCalm { get; init; } = 0.8;
    /// <summary>Как тает прибавка к настроению от уступок и переговоров.</summary>
    public double MoodDecay { get; init; } = 0.92;
    /// <summary>Переговоры: шанс = NegotiateBase + NegotiateOrganization × организованность − NegotiateStage × стадия.</summary>
    public double NegotiateBase { get; init; } = 0.35;
    public double NegotiateOrganization { get; init; } = 0.4;
    public double NegotiateStage { get; init; } = 0.08;
    public double NegotiateMood { get; init; } = 2;
    public double NegotiateCalm { get; init; } = 0.5;
    /// <summary>Разгон: шанс = DisperseBase + DisperseLoyalty × лояльность / 100 − DisperseStage × стадия; гнев, радикализм класса, репрессии.</summary>
    public double DisperseBase { get; init; } = 0.25;
    public double DisperseLoyalty { get; init; } = 0.75;
    public double DisperseStage { get; init; } = 0.12;
    public double DisperseAnger { get; init; } = 0.6;
    public double DisperseRadicalism { get; init; } = 4;
    public double DisperseRepression { get; init; } = 4;
    /// <summary>Нагрузка на полицию (и армию — с беспорядков) за разгон, × (стадия + 1); провал — ещё FailedStrain.</summary>
    public double DisperseStrain { get; init; } = 3;
    public double FailedStrain { get; init; } = 10;
    public double StrainDecay { get; init; } = 0.9;
    /// <summary>Стадия, начиная с которой разгоняет армия (беспорядки).</summary>
    public int ArmyStage { get; init; } = 3;

    /// <summary>Репрессии (0–100, тают на RepressionDecay в месяц): минус легитимности, индексам свобод, плюс радикализму.</summary>
    public double RepressionDecay { get; init; } = 0.95;
    public double RepressionLegitimacy { get; init; } = 0.15;
    public double RepressionLiberties { get; init; } = 0.2;
    public double RepressionDemocracy { get; init; } = 0.1;
    public double RepressionPress { get; init; } = 0.1;
    public double RepressionRadicalism { get; init; } = 0.05;
    /// <summary>Аресты оппозиции: удар по партии, репрессии, скандал при свободной прессе, перерыв между арестами (месяцы).</summary>
    public double ArrestMomentum { get; init; } = 0.6;
    public double ArrestOrganization { get; init; } = 0.75;
    public double ArrestRepression { get; init; } = 25;
    public double ArrestScandal { get; init; } = 0.3;
    public int ArrestCooldown { get; init; } = 12;
    /// <summary>Лояльность силовиков: одобрение группы «Силовики» − нагрузка; плюс главе-силовику и армии при военном режиме.</summary>
    public double SilovikLeader { get; init; } = 5;
    public double MilitaryArmy { get; init; } = 10;
}
