using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Ai;

/// <summary>
/// Внешняя политика ИИ. По очереди, у каждого вида — свой срок между решениями: кредит Валютного совета в кризис;
/// санкции против заклятых соперников и их снятие, когда вражда утихла; визит к важному партнёру; договор, выгодный
/// обеим сторонам; вступление в блок или выход из него; резолюция Совета Наций (только крупные державы). Договоры —
/// только между странами ИИ: игроку ИИ пока не предлагает. MODEL.md, «ИИ стран».
/// </summary>
public static class AiDiplomacy
{
    public static void Consider(AiTurn turn)
    {
        Fund(turn);
        Sanctions(turn);
        Visit(turn);
        Treaty(turn);
        Blocs(turn);
        Council(turn);
    }

    static IEnumerable<Country> Others(AiTurn turn) => turn.World.Countries.Where(c => c.Id != turn.Id);

    /// <summary>Союзники страны: члены её военных блоков и партнёры по договорам о военном союзе.</summary>
    static HashSet<string> AlliesOf(AiTurn turn)
    {
        var world = turn.World;
        var allies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bloc in DiplomacyModel.BlocsOf(world, turn.Id).Where(x => x.Military))
            allies.UnionWith(DiplomacyModel.Members(world, bloc.Id));
        foreach (var t in world.Diplomacy!.Treaties.Where(t => t.Type == "alliance" && t.Involves(turn.Id)))
            allies.Add(t.Other(turn.Id));
        allies.Remove(turn.Id);
        return allies;
    }

    static double Relations(AiTurn turn, string other) => DiplomacyModel.Relations(turn.World, turn.Id, other);

    /// <summary>Кредит Валютного совета: «мусорный» рейтинг и долговой кризис, дефицит не больше FundMaxDeficit (иначе условия не выполнить), Совет согласен.</summary>
    static void Fund(AiTurn turn)
    {
        var world = turn.World;
        if (!turn.Ai.Ready("fund", world.Date, 12))
            return;
        var e = world.Economy![turn.Id];
        var s = AiModel.Assess(world, turn.Country);
        var deficit = -e.BalanceRatio * 100;
        if (!s.DebtCrisis(turn.Balance) || deficit > turn.Balance.FundMaxDeficit)
            return;
        var offer = DiplomacyModel.FundOffer(world, turn.Id);
        if (!offer.Accepts)
            return;
        turn.Why = $"долг дорог: проценты съедают {Fmt.Number(s.InterestBurden * 100, 0)}% доходов, дефицит {Fmt.Number(deficit, 1)}% ВВП";
        if (DiplomacyModel.RequestLoan(world, turn.Id))
            turn.Done("fund");
    }

    /// <summary>
    /// Санкции: сначала снять свои против тех, с кем отношения выше LiftAbove; иначе — против самого враждебного
    /// (ниже SanctionBelow, жёсткие — ниже FullBelow), если сила для правительства важнее потерь торговли.
    /// </summary>
    static void Sanctions(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        if (!turn.Ai.Ready("sanction", world.Date, b.SanctionMonths))
            return;
        foreach (var s in DiplomacyModel.SanctionsBy(world, turn.Id).ToList())
        {
            var relations = Relations(turn, s.Target);
            if (relations <= b.LiftAbove)
                continue;
            turn.Why = $"вражда утихла: отношения {Fmt.Signed(relations, 0)}";
            if (!DiplomacyModel.Lift(world, turn.Id, s.Target))
                continue;
            turn.Done("sanction");
            NotifyPlayer(turn, s.Target, $"Страна «{turn.Country.Name}» сняла санкции против нас.");
            return;
        }
        turn.Ai.Last["sanction"] = world.Date;
        var mine = DiplomacyModel.SanctionsBy(world, turn.Id).Select(s => s.Target).ToHashSet(StringComparer.Ordinal);
        var allies = AlliesOf(turn);
        var enemy = Others(turn).Where(c => !mine.Contains(c.Id) && !allies.Contains(c.Id))
            .Select(c => (c.Id, Relations: Relations(turn, c.Id))).Where(x => x.Relations < b.SanctionBelow)
            .OrderBy(x => x.Relations).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (enemy.Id == null)
            return;
        var hostility = (b.SanctionBelow - enemy.Relations) / 40 + 0.5;
        var cost = DiplomacyModel.TradeShare(world, turn.Id, enemy.Id) * 10;
        var utility = turn.Ai.Power * hostility + turn.Ai.Stability * 0.2 - turn.Ai.Growth * cost;
        if (utility <= 0.1)
            return;
        var level = enemy.Relations < b.FullBelow ? "full" : "partial";
        turn.Why = $"заклятый соперник: отношения {Fmt.Signed(enemy.Relations, 0)}; торговли с ним {Fmt.Value("percent", cost * 10)} (приоритеты: {AiModel.Describe(turn.Ai)})";
        if (!DiplomacyModel.Impose(world, turn.Id, enemy.Id, level))
            return;
        turn.Done("sanction");
        NotifyPlayer(turn, enemy.Id, $"Страна «{turn.Country.Name}» ввела {DiplomacyModel.SanctionName(level)} санкции против нас.");
    }

    /// <summary>Визит главы: к важному партнёру (торговля, союз, мощь), с кем отношения не враждебные; к той же стране — раз в VisitAgainMonths.</summary>
    static void Visit(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        if (!turn.Ai.Ready("visit", world.Date, b.VisitMonths))
            return;
        var visits = world.Diplomacy!.Visits;
        var allies = AlliesOf(turn);
        // Сначала ценность (дёшево), потом для лучших — недавние визиты и враждебность.
        var best = Others(turn)
            .Select(c => (c.Id, Value: DiplomacyModel.TradeShare(world, turn.Id, c.Id) * 100 * (0.5 + turn.Ai.Growth)
                                       + (allies.Contains(c.Id) ? 10 * (0.5 + turn.Ai.Power) : 0)
                                       + (world.Diplomacy.Power.GetValueOrDefault(c.Id)?.Index ?? 0) * 0.1))
            .OrderByDescending(x => x.Value).ThenBy(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault(x => Relations(turn, x.Id) >= world.Data.DiplomacyBalance.VisitCriticismBelow
                                 && (!visits.TryGetValue(WorldDiplomacy.Key(turn.Id, x.Id), out var last) || last.AddMonths(b.VisitAgainMonths) <= world.Date));
        turn.Ai.Last["visit"] = world.Date;
        if (best.Id == null || best.Value < 1)
            return;
        turn.Why = $"важный партнёр: доля в торговле {Fmt.Value("percent", DiplomacyModel.TradeShare(world, turn.Id, best.Id) * 100)}" +
                   (allies.Contains(best.Id) ? ", союзник" : "");
        if (DiplomacyModel.Visit(world, turn.Id, best.Id))
            NotifyPlayer(turn, best.Id, $"К нам с визитом приехал глава страны «{turn.Country.Name}»: отношения улучшились.");
    }

    static readonly string[] TreatyTypes = ["free_trade", "investment", "science", "visa_free", "energy"];

    /// <summary>Польза договора для страны a: рост от торговли и инвестиций против конкуренции импорта и миграции.</summary>
    public static double TreatyUtility(World world, string a, string partner, string type)
    {
        var ai = world.Ai!.Countries[a];
        var share = DiplomacyModel.TradeShare(world, a, partner);
        var weight = DiplomacyModel.Weight(world, a, partner);
        // Защита инвестиций и безвизовый режим насыщаются (сумма весов партнёров не больше 1): польза — только прибавка.
        double Marginal(string kind)
        {
            var current = world.Diplomacy!.Treaties.Where(t => t.Type == kind && t.Involves(a)).Sum(t => DiplomacyModel.Weight(world, a, t.Other(a)));
            return Math.Min(1, current + weight) - Math.Min(1, current);
        }
        var economic = Politics.PoliticsModel.HeadParty(world, a)?.Position[0] ?? 0;
        var protectionist = economic < world.Data.DiplomacyBalance.Protectionist ? 0.3 : 0;
        return type switch
        {
            "free_trade" => ai.Growth * share * 20 * world.Data.DiplomacyBalance.FreeTrade - ai.Stability * share - protectionist,
            "investment" => ai.Growth * Marginal("investment") * 1.5 - protectionist,
            "science" => ai.Growth * weight * (world[partner].Stat("technology_level_pct") > world[a].Stat("technology_level_pct") ? 1 : 0.3),
            "visa_free" => ai.Growth * Marginal("visa_free") * 0.8 - ai.Stability * weight * 0.6,
            "energy" => DiplomacyModel.EnergyExporter(world, a) != DiplomacyModel.EnergyExporter(world, partner) ? ai.Growth * weight * 1.2 : -1,
            _ => -1,
        };
    }

    /// <summary>
    /// Договор: с одним из главных партнёров по торговле (не с игроком), выгодный нам (больше TreatyThreshold) и
    /// заметно выгодный партнёру (больше трети порога), одобренный им и не сверх его лимита договоров.
    /// </summary>
    static void Treaty(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        var d = world.Diplomacy!;
        if (!turn.Ai.Ready("treaty", world.Date, b.TreatyMonths))
            return;
        turn.Ai.Last["treaty"] = world.Date;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var existing = new HashSet<(string, string, string)>();
        foreach (var t in d.Treaties)
        {
            counts[t.A] = counts.GetValueOrDefault(t.A) + 1;
            counts[t.B] = counts.GetValueOrDefault(t.B) + 1;
            existing.Add((t.Type, t.A, t.B));
            existing.Add((t.Type, t.B, t.A));
        }
        if (counts.GetValueOrDefault(turn.Id) >= b.MaxTreaties)
            return;
        var partners = Others(turn).Where(c => c.Id != world.PlayerCountry && world.Ai!.Countries.ContainsKey(c.Id)
                                               && counts.GetValueOrDefault(c.Id) < b.MaxTreaties)
            .OrderByDescending(c => DiplomacyModel.TradeShare(world, turn.Id, c.Id)).Take(b.TreatyPartners);
        var best = (Partner: (string?)null, Type: "", Utility: 0.0, Theirs: 0.0);
        foreach (var p in partners)
            foreach (var type in TreatyTypes)
            {
                if (existing.Contains((type, turn.Id, p.Id)))
                    continue;
                var ours = TreatyUtility(world, turn.Id, p.Id, type);
                if (ours <= b.TreatyThreshold || ours <= best.Utility)
                    continue;
                var theirs = TreatyUtility(world, p.Id, turn.Id, type);
                if (theirs <= b.TreatyThreshold / 3 || !DiplomacyModel.Acceptance(world, turn.Id, p.Id, type).Accepts)
                    continue;
                best = (p.Id, type, ours, theirs);
            }
        // Партнёру за этот месяц могли уже предложить другие — его лимит проверяется по нынешнему списку.
        if (best.Partner is not { } partner || d.Treaties.Count(t => t.Involves(partner)) >= b.MaxTreaties)
            return;
        var name = world.Data.Diplomacy.Treaties.First(t => t.Id == best.Type).Name;
        turn.Why = $"выгодно обеим: нам {Fmt.Number(best.Utility, 2)}, им {Fmt.Number(best.Theirs, 2)}; доля в торговле {Fmt.Value("percent", DiplomacyModel.TradeShare(world, turn.Id, partner) * 100)}";
        if (!DiplomacyModel.Propose(world, turn.Id, partner, best.Type))
            return;
        turn.Done("treaty");
        var other = world.Ai!.Countries[partner];
        other.Decisions.Add(new AiDecision(world.Date, "diplomacy", $"Принят договор «{name}», предложенный страной «{turn.Country.Name}».",
            $"выгодно: польза {Fmt.Number(best.Theirs, 2)}"));
        if (other.Decisions.Count > b.Journal)
            other.Decisions.RemoveAt(0);
    }

    /// <summary>
    /// Блоки: выйти, если отношения с членами в среднем ниже LeaveBelow; вступить, если примут и польза больше
    /// BlocThreshold (торговый — рост × торговля с членами × глубина, военный — сила × угроза от соперничающего блока).
    /// </summary>
    static void Blocs(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        if (!turn.Ai.Ready("bloc", world.Date, b.BlocMonths))
            return;
        turn.Ai.Last["bloc"] = world.Date;
        foreach (var bloc in DiplomacyModel.BlocsOf(world, turn.Id).ToList())
        {
            var members = DiplomacyModel.Members(world, bloc.Id).Where(m => m != turn.Id).ToList();
            if (members.Count == 0)
                continue;
            var average = members.Average(m => Relations(turn, m));
            if (average >= b.LeaveBelow)
                continue;
            turn.Why = $"с членами блока отношения в среднем {Fmt.Signed(average, 0)}";
            if (DiplomacyModel.Leave(world, turn.Id, bloc.Id))
            {
                turn.Done("bloc");
                NotifyPlayer(turn, members, $"Страна «{turn.Country.Name}» вышла из «{bloc.Name}».");
                return;
            }
        }
        var best = (Bloc: (Data.BlocRecord?)null, Utility: 0.0, Why: "");
        foreach (var bloc in world.Data.Diplomacy.Blocs)
        {
            if (DiplomacyModel.InBloc(world, bloc.Id, turn.Id) || !DiplomacyModel.JoinAcceptance(world, turn.Id, bloc.Id).Accepts)
                continue;
            var members = DiplomacyModel.Members(world, bloc.Id);
            double utility;
            string why;
            if (bloc.Military)
            {
                var threat = bloc.Rival is { } rival
                    ? Math.Max(0, -DiplomacyModel.Members(world, rival).Select(r => Relations(turn, r)).DefaultIfEmpty(0).Min()) / 50
                    : 0;
                utility = turn.Ai.Power * threat + turn.Ai.Stability * 0.2;
                why = $"угроза от соперников блока {Fmt.Number(threat, 2)}";
            }
            else
            {
                var trade = members.Sum(m => DiplomacyModel.TradeShare(world, turn.Id, m));
                utility = turn.Ai.Growth * trade * bloc.Trade * 10;
                why = $"торговля с членами — {Fmt.Value("percent", trade * 100)} нашей";
            }
            if (utility > best.Utility)
                best = (bloc, utility, why);
        }
        if (best.Bloc is not { } chosen || best.Utility <= b.BlocThreshold)
            return;
        turn.Why = $"польза {Fmt.Number(best.Utility, 2)}: {best.Why} (приоритеты: {AiModel.Describe(turn.Ai)})";
        var before = DiplomacyModel.Members(world, chosen.Id).ToList();
        if (DiplomacyModel.Join(world, turn.Id, chosen.Id))
        {
            turn.Done("bloc");
            NotifyPlayer(turn, before, $"Страна «{turn.Country.Name}» вступила в «{chosen.Name}».");
        }
    }

    /// <summary>
    /// Совет Наций (постоянные члены Совета безопасности и первые CouncilRank по мощи): санкции против заклятого
    /// соперника, если пройдут; иначе осуждение; иначе гуманитарная помощь бедной стране в кризисе. Только если по
    /// прогнозу резолюция пройдёт — проигранное голосование ИИ не нужно.
    /// </summary>
    static void Council(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        if (!turn.Ai.Ready("council", world.Date, b.CouncilMonths))
            return;
        var rank = world.Diplomacy!.Power.GetValueOrDefault(turn.Id)?.Rank ?? int.MaxValue;
        if (rank > b.CouncilRank && !DiplomacyModel.Permanent(world).Contains(turn.Id))
            return;
        turn.Ai.Last["council"] = world.Date;
        var hostile = Others(turn).Where(c => c.Info.UnMember).Select(c => (c.Id, Relations: Relations(turn, c.Id)))
            .Where(x => x.Relations < b.SanctionBelow).OrderBy(x => x.Relations).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var options = new List<(string Type, string Target, string Why)>();
        foreach (var (id, relations) in hostile.Take(3))
        {
            if (turn.Ai.Power >= 0.3)
                options.Add(("sanctions", id, $"заклятый соперник: отношения {Fmt.Signed(relations, 0)}"));
            options.Add(("condemn", id, $"заклятый соперник: отношения {Fmt.Signed(relations, 0)}"));
        }
        if (turn.Country.Income == IncomeGroup.High && turn.Ai.Stability + turn.Ai.Reelection >= 0.45)
        {
            var poor = Others(turn).Where(c => c.Income == IncomeGroup.Low && c.Info.UnMember && Relations(turn, c.Id) > 0)
                .Select(c => (c, s: AiModel.Assess(world, c))).Where(x => x.s.Any(b))
                .OrderByDescending(x => x.s.Inflation + x.s.Unemployment).ThenBy(x => x.c.Id, StringComparer.Ordinal).FirstOrDefault();
            if (poor.c != null)
                options.Add(("aid", poor.c.Id, "бедная страна в кризисе: " + string.Join(", ", AiModel.Crises(poor.s, b))));
        }
        foreach (var (type, target, why) in options)
        {
            if (!DiplomacyModel.CanPropose(world, turn.Id, type, target).Allowed || !DiplomacyModel.Forecast(world, type, turn.Id, target).Passed)
                continue;
            turn.Why = why + " — по прогнозу резолюция пройдёт";
            if (!DiplomacyModel.ProposeResolution(world, turn.Id, type, target))
                continue;
            turn.Done("council");
            var record = world.Data.Diplomacy.Council.Resolution(type)!;
            NotifyPlayer(turn, target, $"{world.Data.Diplomacy.Council.Name}: по предложению страны «{turn.Country.Name}» принята резолюция «{record.Name}» в отношении нас.");
            return;
        }
    }

    static void NotifyPlayer(AiTurn turn, string country, string text) => NotifyPlayer(turn, [country], text);

    /// <summary>Сообщить игроку, если действие ИИ касается его страны.</summary>
    static void NotifyPlayer(AiTurn turn, IEnumerable<string> countries, string text)
    {
        if (turn.World.PlayerCountry is { } player && countries.Contains(player))
            turn.World.Log(player, "diplomacy", text);
    }
}
