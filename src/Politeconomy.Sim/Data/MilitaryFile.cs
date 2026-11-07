namespace Politeconomy.Sim.Data;

// Армии и войны. Поля описаны в data/README.md, смысл — в MODEL.md, «Армии и войны».

/// <summary>data/military.json: поправки к численности армий, территориальные претензии, перемирия на старте.</summary>
public sealed record MilitaryFile(IReadOnlyList<ArmyRecord> Armies, IReadOnlyList<ClaimRecord> Claims, IReadOnlyList<CeasefireRecord> Ceasefires);

/// <summary>Численность армии, если данные Всемирного банка устарели (мобилизация, нет данных).</summary>
public sealed record ArmyRecord(string Country, double Soldiers, string Note);

/// <summary>Претензия страны на регионы, которыми она не владеет или которые не контролирует.</summary>
public sealed record ClaimRecord(string Claimant, IReadOnlyList<string> Regions, string Note);

/// <summary>Перемирие на старте: боёв нет, но война может возобновиться.</summary>
public sealed record CeasefireRecord(string A, string B, string Since, string Note);

/// <summary>data/terrain.json (tools/build_terrain.py): соседи регионов по суше и через проливы, доля гор.</summary>
public sealed record TerrainFile(string Built, string Source, IReadOnlyList<TerrainRecord> Regions);

public sealed record TerrainRecord(string Id, IReadOnlyList<string> Land, IReadOnlyList<string> Sea, double Mountains);

// Коэффициенты войны из data/balance.json (раздел war).

public sealed class WarBalance
{
    /// <summary>Сила армии = солдаты^SoldiersExponent × (оснащение, млн долл.)^EquipmentExponent × (1 + военные технологии) × боевой дух / 70.</summary>
    public double SoldiersExponent { get; init; } = 0.4;
    public double EquipmentExponent { get; init; } = 0.6;
    /// <summary>Оснащение: на старте — EquipmentYears годовых военных расходов; в месяц + Procurement расходов, износ Depreciation в год.</summary>
    public double EquipmentYears { get; init; } = 4;
    public double Procurement { get; init; } = 0.3;
    public double Depreciation { get; init; } = 0.075;
    /// <summary>Мобилизация: на войне армия растёт до (1 + Mobilization) × мирной за MobilizationMonths, после — возвращается за вдвое больший срок.</summary>
    public double Mobilization { get; init; } = 1;
    public int MobilizationMonths { get; init; } = 6;
    /// <summary>Боевой дух: Morale в мирное время, минус MoraleExhaustion × усталость от войны.</summary>
    public double Morale { get; init; } = 70;
    public double MoraleExhaustion { get; init; } = 0.4;
    /// <summary>Какая доля силы воюет: у главных сторон CommitMain, у союзников CommitAlly.</summary>
    public double CommitMain { get; init; } = 0.8;
    public double CommitAlly { get; init; } = 0.35;
    /// <summary>
    /// Фронт: наступление на регион — сила на него / оборона там × оборона региона (1 + Mountains × доля гор +
    /// City × lg плотности + Capital у столицы + Home у своей земли); через пролив сила × SeaPenalty. Прогресс за
    /// месяц — Advance × (перевес − 1) × (AreaRef / площадь)^AreaExponent; без перевеса откатывается на Retreat.
    /// </summary>
    public double SeaPenalty { get; init; } = 0.2;
    /// <summary>Оборона любого региона начинается с Entrenchment: окопанному обороняющемуся нужен перевес.</summary>
    public double Entrenchment { get; init; } = 1.5;
    /// <summary>Наступающие сосредоточивают силы: на спорные регионы (цель войны) — в Focus раз больше, чем на остальные.</summary>
    public double Focus { get; init; } = 2;
    /// <summary>Штурмующая сторона (продвигается хоть где-то) теряет в Assault раз больше.</summary>
    public double Assault { get; init; } = 1.3;
    public double Mountains { get; init; } = 1;
    public double City { get; init; } = 0.15;
    public double Capital { get; init; } = 0.5;
    public double Home { get; init; } = 0.2;
    public double Advance { get; init; } = 20;
    public double Retreat { get; init; } = 15;
    public double AreaRef { get; init; } = 20000;
    public double AreaExponent { get; init; } = 0.3;
    /// <summary>
    /// Потери в месяц: солдат — SoldierLoss воюющих × √(перевес противника) (0,5–2), оснащения — EquipmentLoss; мирных
    /// жителей — CivilianLoss населения регионов фронта; разрушения — Damage × доля населения страны на фронте
    /// (восстанавливается на Recovery в год).
    /// </summary>
    public double SoldierLoss { get; init; } = 0.004;
    public double EquipmentLoss { get; init; } = 0.01;
    public double CivilianLoss { get; init; } = 0.0003;
    public double Damage { get; init; } = 0.02;
    public double Recovery { get; init; } = 0.1;
    /// <summary>Занятые регионы: владелец теряет OccupiedLoss их выпуска, контролирующий получает OccupationGain (в сравнении со стартом).</summary>
    public double OccupiedLoss { get; init; } = 0.8;
    public double OccupationGain { get; init; } = 0.3;
    /// <summary>Война для экономики: оборона + WarSpending % ВВП, рост WarGrowth п. п., премия за риск + WarSpread п. п. (у союзников — доля AllyBurden).</summary>
    public double WarSpending { get; init; } = 2;
    public double WarGrowth { get; init; } = -0.5;
    public double WarSpread { get; init; } = 1.5;
    public double AllyBurden { get; init; } = 0.3;
    /// <summary>
    /// Политика: сплочение вокруг флага — легитимность + Rally, тает за RallyMonths; усталость растёт на Exhaustion в
    /// месяц, + ExhaustionCasualties × погибшие на 10 тыс. жителей, + ExhaustionOccupied × доля занятой своей земли; в
    /// мире спадает на ExhaustionPeace; легитимность − ExhaustionLegitimacy × усталость, настроение классов −
    /// ExhaustionMood × усталость.
    /// </summary>
    public double Rally { get; init; } = 10;
    public int RallyMonths { get; init; } = 6;
    public double Exhaustion { get; init; } = 1.5;
    public double ExhaustionCasualties { get; init; } = 2;
    public double ExhaustionOccupied { get; init; } = 20;
    public double ExhaustionPeace { get; init; } = 3;
    public double ExhaustionLegitimacy { get; init; } = 0.3;
    public double ExhaustionMood { get; init; } = 0.06;
    /// <summary>
    /// Конец войны: сторона просит перемирия при усталости выше CeasefireExhaustion или когда цель взята; соглашается,
    /// если её усталость выше AcceptExhaustion или перевес противника больше AcceptRatio; капитулирует, потеряв
    /// столицу или больше CapitulateShare населения; при усталости обеих выше StalemateExhaustion — перемирие само.
    /// </summary>
    public double CeasefireExhaustion { get; init; } = 60;
    public double AcceptExhaustion { get; init; } = 30;
    public double AcceptRatio { get; init; } = 1.5;
    public double CapitulateShare { get; init; } = 0.5;
    public double StalemateExhaustion { get; init; } = 80;
    /// <summary>Нападение: отношения агрессора со всеми −Aggression (со спором за регионы — вполовину), с жертвой −2 × Aggression.</summary>
    public double Aggression { get; init; } = 15;
    /// <summary>
    /// ИИ: нападает, если перевес сил (с учётом союзников и друзей цели — Deterrence их силы) не меньше WarRatio
    /// (демократии — DemocracyRatio), цель не ядерная держава, есть спор за регионы или отношения ниже Hostile; раз в
    /// WarMonths решает и нападает с вероятностью WarChance в месяц; перемирие — не раньше чем через CeasefireMonths.
    /// </summary>
    public double WarRatio { get; init; } = 2;
    public double DemocracyRatio { get; init; } = 3;
    public double Deterrence { get; init; } = 0.8;
    public double Hostile { get; init; } = -70;
    public int WarMonths { get; init; } = 12;
    public double WarChance { get; init; } = 0.15;
    public int CeasefireMonths { get; init; } = 36;
}
