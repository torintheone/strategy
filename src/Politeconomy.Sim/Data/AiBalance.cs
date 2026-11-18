namespace Politeconomy.Sim.Data;

// Коэффициенты ИИ стран из data/balance.json (раздел ai). Смысл — в MODEL.md, «ИИ стран».

public sealed class AiBalance
{
    /// <summary>Приоритеты (рост, стабильность, сила, переизбрание): у всех по PriorityBase, потом прибавки, потом доли.</summary>
    public double PriorityBase { get; init; } = 1;
    /// <summary>Демократия: переизбрание +RegimeReelection; гибрид — половина этого и половина силы; диктатура: сила +RegimePower, стабильность +RegimeStability.</summary>
    public double RegimeReelection { get; init; } = 1;
    public double RegimePower { get; init; } = 1;
    public double RegimeStability { get; init; } = 0.5;
    /// <summary>Экономические взгляды правящей партии: правее — рост, левее — стабильность (± PartyEconomy × позиция / 100).</summary>
    public double PartyEconomy { get; init; } = 0.8;
    /// <summary>Черты главы: технократ и реформатор — рост, силовик — сила, популист и харизматик — переизбрание (+Trait).</summary>
    public double Trait { get; init; } = 0.6;
    /// <summary>Кризис (инфляция выше InflationCrisis %, безработица выше UnemploymentCrisis %, проценты по долгу больше InterestCrisis доходов, массовые протесты): стабильность +Crisis.</summary>
    public double Crisis { get; init; } = 1;
    public double InflationCrisis { get; init; } = 30;
    public double UnemploymentCrisis { get; init; } = 20;
    public double InterestCrisis { get; init; } = 0.25;
    /// <summary>До выборов меньше ElectionMonths месяцев: переизбрание +Election.</summary>
    public double Election { get; init; } = 1;
    public int ElectionMonths { get; init; } = 12;

    /// <summary>
    /// Бюджет: доли статей сдвигаются к приоритетам не больше чем на Tilt; перед выборами — соцрасходы до +Generosity % ВВП;
    /// в долговом кризисе — расходы −Austerity % ВВП в год, если по программе Валютного совета дефицит выше цели — вдвое больше.
    /// </summary>
    public double Tilt { get; init; } = 0.15;
    public double Generosity { get; init; } = 0.5;
    public double Austerity { get; init; } = 1;

    /// <summary>
    /// Законы: не чаще раза в LawMonths; вносит, если польза больше LawThreshold и парламент, по прогнозу, примет.
    /// LawInertia — цена самой перемены: без заметной пользы правительство законы не трогает.
    /// </summary>
    public int LawMonths { get; init; } = 36;
    public double LawThreshold { get; init; } = 0.15;
    public double LawInertia { get; init; } = 0.1;

    /// <summary>Визиты: раз в VisitMonths, к той же стране — не чаще раза в VisitAgainMonths.</summary>
    public int VisitMonths { get; init; } = 6;
    public int VisitAgainMonths { get; init; } = 24;
    /// <summary>Договоры: раз в TreatyMonths, не больше MaxTreaties; польза больше TreatyThreshold у обеих сторон; партнёры — TreatyPartners главных по торговле.</summary>
    public int TreatyMonths { get; init; } = 24;
    public int MaxTreaties { get; init; } = 6;
    public double TreatyThreshold { get; init; } = 0.15;
    public int TreatyPartners { get; init; } = 10;
    /// <summary>Санкции: против страны с отношениями ниже SanctionBelow (жёсткие — ниже FullBelow), снимает свои при отношениях выше LiftAbove; решение не чаще раза в SanctionMonths.</summary>
    public double SanctionBelow { get; init; } = -60;
    public double FullBelow { get; init; } = -80;
    public double LiftAbove { get; init; } = 10;
    public int SanctionMonths { get; init; } = 12;
    /// <summary>Блоки: решение не чаще раза в BlocMonths; вступает, если польза больше BlocThreshold; выходит, если отношения с членами в среднем ниже LeaveBelow.</summary>
    public int BlocMonths { get; init; } = 36;
    public double BlocThreshold { get; init; } = 0.3;
    public double LeaveBelow { get; init; } = -10;
    /// <summary>Совет Наций: резолюция не чаще раза в CouncilMonths; выносят страны из первых CouncilRank по мощи и постоянные члены Совета безопасности.</summary>
    public int CouncilMonths { get; init; } = 24;
    public int CouncilRank { get; init; } = 30;
    /// <summary>Кредит Валютного совета: просит в долговом кризисе, если дефицит не больше FundMaxDeficit % ВВП (больший за два года не сократить).</summary>
    public double FundMaxDeficit { get; init; } = 5;
    /// <summary>Журнал решений: последние Journal записей у каждой страны.</summary>
    public int Journal { get; init; } = 30;
}
