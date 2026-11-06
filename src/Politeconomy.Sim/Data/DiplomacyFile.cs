namespace Politeconomy.Sim.Data;

// Дипломатия. Поля описаны в data/README.md, смысл — в MODEL.md, «Дипломатия».

public sealed record DiplomacyFile(IReadOnlyList<TreatyRecord> Treaties, IReadOnlyList<HistoryRecord> History);

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
}
