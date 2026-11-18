namespace Politeconomy.Sim.Data;

// Дипломатия. Поля описаны в data/README.md, смысл — в MODEL.md, «Дипломатия».

public sealed record DiplomacyFile(
    IReadOnlyList<TreatyRecord> Treaties,
    IReadOnlyList<HistoryRecord> History,
    IReadOnlyList<BlocRecord> Blocs,
    IReadOnlyList<AllianceRecord> Alliances,
    IReadOnlyList<SanctionRecord> Sanctions,
    CouncilRecord Council,
    FundRecord Fund,
    IReadOnlyList<string> Nuclear)
{
    public BlocRecord? Bloc(string id) => Blocs.FirstOrDefault(b => b.Id == id);
}

/// <summary>
/// Блок на 1 января 2026 года (вымышленное название, реальный состав). Kind: trade — торговый (Trade — насколько
/// растёт торговля между членами), military — военный (Rival — соперничающий блок). Вступить могут страны из
/// Regions; Democracy — только демократии; Unanimous — нужно согласие каждого члена, иначе большинства.
/// </summary>
public sealed record BlocRecord(
    string Id, string Name, string Kind, string Color, string Description, double Trade, IReadOnlyList<string> Members,
    string? Rival, IReadOnlyList<string> Regions, bool Democracy, bool Unanimous)
{
    public bool Military => Kind == "military";
}

/// <summary>Военный союз двух стран на старте (договор alliance).</summary>
public sealed record AllianceRecord(string A, string B, string Note);

/// <summary>Санкции на старте: кто (By) против кого (Target); Level — full (жёсткие) или partial (частичные).</summary>
public sealed record SanctionRecord(string Target, string Level, IReadOnlyList<string> By, string Note);

/// <summary>
/// «Совет Наций»: постоянные члены Совета безопасности (с правом вето), выборные — до конца какого года, санкции
/// Совета на старте, виды резолюций.
/// </summary>
public sealed record CouncilRecord(
    string Name, IReadOnlyList<string> Permanent, IReadOnlyList<CouncilSeatRecord> Elected, IReadOnlyList<string> Sanctions,
    IReadOnlyList<ResolutionRecord> Resolutions)
{
    public ResolutionRecord? Resolution(string id) => Resolutions.FirstOrDefault(r => r.Id == id);
}

public sealed record CouncilSeatRecord(string Country, int Until);

/// <summary>Вид резолюции: Chamber — assembly (все члены, простое большинство) или security (Совет безопасности); Against — против страны.</summary>
public sealed record ResolutionRecord(string Id, string Name, string Chamber, bool Against, string Description);

/// <summary>«Международный валютный совет».</summary>
public sealed record FundRecord(string Name);

/// <summary>Вид договора: с каких отношений партнёр готов его подписать и насколько он их улучшает.</summary>
public sealed record TreatyRecord(string Id, string Name, string Description, double MinRelations, double Relations);

/// <summary>История отношений пары стран на старте (союзники, соперники, споры) — прибавка к отношениям.</summary>
public sealed record HistoryRecord(string A, string B, double Value, string Note);

// Коэффициенты дипломатии из data/balance.json (раздел diplomacy).

public sealed class DiplomacyBalance
{
    /// <summary>Торговля пары = ВВП × ВВП / расстояние^TradeDistance (соседям × TradeNeighbor); доли — от всей торговли страны.</summary>
    public double TradeDistance { get; init; } = 0.9;
    public double TradeNeighbor { get; init; } = 1.5;
    /// <summary>Отношения от торговли: TradeRelations × √(доля партнёра в торговле — большая из двух).</summary>
    public double TradeRelations { get; init; } = 25;
    /// <summary>Строй: одинаковый режим +RegimeSame, демократия и диктатура −RegimeOpposite; взгляды правящих партий: Ideology × (1 − расстояние / 100).</summary>
    public double RegimeSame { get; init; } = 8;
    public double RegimeOpposite { get; init; } = 8;
    public double Ideology { get; init; } = 10;
    /// <summary>Соседи по суше: +Neighbors.</summary>
    public double Neighbors { get; init; } = 5;
    /// <summary>Память о событиях (визиты, помощь, высылки) тает за MemoryYears лет.</summary>
    public double MemoryYears { get; init; } = 3;
    /// <summary>Визит: отношения +VisitRelations; к той же стране — не чаще раза в VisitMonths; к стране, с которой отношения хуже VisitCriticismBelow, — популярность главы −VisitCriticism.</summary>
    public double VisitRelations { get; init; } = 6;
    public int VisitMonths { get; init; } = 6;
    public double VisitCriticismBelow { get; init; } = -20;
    public double VisitCriticism { get; init; } = 2;
    /// <summary>Помощь: AidShare % своего ВВП; отношения + AidRelations × ln(1 + помощь в % ВВП получателя), не больше AidMax.</summary>
    public double AidShare { get; init; } = 0.1;
    public double AidRelations { get; init; } = 10;
    public double AidMax { get; init; } = 25;
    /// <summary>Высылка дипломатов и разрыв договора: −ExpelRelations и −CancelRelations.</summary>
    public double ExpelRelations { get; init; } = 15;
    public double CancelRelations { get; init; } = 10;
    /// <summary>Свободная торговля: торговля с партнёром + FreeTrade; производительность + TradeGain × открытость × рост торговли.</summary>
    public double FreeTrade { get; init; } = 0.25;
    public double TradeGain { get; init; } = 0.5;
    /// <summary>Конкуренция с импортом: удовлетворённость рабочих и фермеров − ImportMood × доля партнёра в торговле.</summary>
    public double ImportMood { get; init; } = 5;
    /// <summary>Защита инвестиций: рост + InvestmentGrowth п. п. × вес партнёра (его ВВП / сумма двух ВВП).</summary>
    public double InvestmentGrowth { get; init; } = 0.15;
    /// <summary>Безвизовый режим: рост + VisaGrowth п. п. × вес партнёра (туризм), миграция + VisaMigration, неквалифицированные − VisaMood × вес.</summary>
    public double VisaGrowth { get; init; } = 0.05;
    public double VisaMigration { get; init; } = 0.05;
    public double VisaMood { get; init; } = 1;
    /// <summary>Энергетическая сделка: экспортёр — рента нефти и газа больше EnergyRent % ВВП; покупателю топливо дешевле на EnergyFuel, продавцу рост + EnergyGrowth п. п. × вес.</summary>
    public double EnergyRent { get; init; } = 2;
    public double EnergyFuel { get; init; } = 0.1;
    public double EnergyGrowth { get; init; } = 0.05;
    /// <summary>Научное сотрудничество: технология, которая есть у партнёра, дешевле на ScienceDiscount (со всеми партнёрами — не больше ScienceMax).</summary>
    public double ScienceDiscount { get; init; } = 0.15;
    public double ScienceMax { get; init; } = 0.4;
    /// <summary>Правительство с экономическими взглядами левее Protectionist не любит свободную торговлю: порог согласия + ProtectionistPenalty.</summary>
    public double Protectionist { get; init; } = -30;
    public double ProtectionistPenalty { get; init; } = 15;

    /// <summary>Блоки: члены одного торгового блока +BlocTradeRelations, военного +BlocMilitaryRelations, соперничающих военных −RivalRelations.</summary>
    public double BlocTradeRelations { get; init; } = 5;
    public double BlocMilitaryRelations { get; init; } = 10;
    public double RivalRelations { get; init; } = 8;
    /// <summary>Вступление: отношения с членом блока не ниже JoinRelations (военный блок — JoinMilitaryRelations), чтобы он был «за». Выход: −LeaveRelations с каждым членом.</summary>
    public double JoinRelations { get; init; } = 20;
    public double JoinMilitaryRelations { get; init; } = 35;
    public double LeaveRelations { get; init; } = 10;
    /// <summary>Санкции: торговля пары × (1 − SanctionFull) или × (1 − SanctionPartial); отношения −SanctionRelations за жёсткие (за частичные — половина).</summary>
    public double SanctionFull { get; init; } = 0.8;
    public double SanctionPartial { get; init; } = 0.3;
    public double SanctionRelations { get; init; } = 8;
    /// <summary>Цель санкций теряет доступ к деньгам: рост −SanctionGrowth п. п. и премия за риск +SanctionSpread п. п. × доля санкционеров в мировом ВВП (с учётом уровня) — в сравнении со стартом.</summary>
    public double SanctionGrowth { get; init; } = 1;
    public double SanctionSpread { get; init; } = 2;
    /// <summary>Совет Наций: игрок выносит резолюцию не чаще раза в CouncilMonths; голос «за» — если перевес отношений больше VoteThreshold, «против» — если меньше −VoteThreshold.</summary>
    public int CouncilMonths { get; init; } = 3;
    public double VoteThreshold { get; init; } = 5;
    /// <summary>Совет безопасности: нужно SecurityVotes голосов «за» и ни одного «против» у постоянных членов.</summary>
    public int SecurityVotes { get; init; } = 9;
    /// <summary>Осуждение: отношения цели с проголосовавшими «за» −CondemnRelations. Помощь Совета: долг цели −CouncilAid % её ВВП, отношения с голосовавшими «за» +CouncilAidRelations.</summary>
    public double CondemnRelations { get; init; } = 4;
    public double CouncilAid { get; init; } = 2;
    public double CouncilAidRelations { get; init; } = 3;
    /// <summary>
    /// Валютный совет: кредит FundShare % ВВП под ставку ФРС + FundMargin п. п. странам с рейтингом не выше FundRating
    /// (ниже инвестиционного). Члены голосуют весом своего ВВП: против, если отношения с заёмщиком ниже FundRelations.
    /// Условие: дефицит не больше FundDeficit % ВВП через FundTargetYears лет; программа длится FundYears лет; пока она
    /// идёт, премия за риск −FundSpread п. п.; нарушил условия — программа прекращена, премия +FundPenalty п. п. на FundPenaltyYears лет.
    /// </summary>
    public double FundShare { get; init; } = 5;
    public double FundMargin { get; init; } = 1;
    public double FundRating { get; init; } = 11.5;
    public double FundRelations { get; init; } = -20;
    public double FundDeficit { get; init; } = 3;
    public int FundTargetYears { get; init; } = 2;
    public int FundYears { get; init; } = 3;
    public double FundSpread { get; init; } = 1;
    public double FundPenalty { get; init; } = 1.5;
    public int FundPenaltyYears { get; init; } = 3;
    /// <summary>Индекс мощи: веса экономики, армии, технологий и влияния; ядерное оружие умножает армию на Nuclear.</summary>
    public double PowerEconomy { get; init; } = 0.4;
    public double PowerMilitary { get; init; } = 0.3;
    public double PowerTechnology { get; init; } = 0.15;
    public double PowerInfluence { get; init; } = 0.15;
    public double Nuclear { get; init; } = 2;
    /// <summary>Постоянное место в Совете безопасности добавляет к влиянию PermanentSeat долю мирового влияния.</summary>
    public double PermanentSeat { get; init; } = 0.03;
}
