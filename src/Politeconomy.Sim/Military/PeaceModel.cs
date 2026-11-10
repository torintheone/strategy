using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Military;

/// <summary>
/// Мирные договоры. Счёт войны (<see cref="Score"/>) — сколько победитель может потребовать: занятые земли, усталость,
/// капитуляция. Условия (<see cref="PeaceTerms"/>) имеют цену для уступающего (<see cref="Cost"/>): регионы — по доле
/// населения, репарации, торговые уступки, нейтралитет. Уступающий соглашается на цену не больше счёта, если устал или
/// капитулировал; победитель — на цену не меньше счёта за вычетом своей усталости. Договор передаёт регионы вместе
/// с населением и хозяйством (<see cref="Annex"/>), остальное занятое возвращается, ставит мир на PeaceYears лет.
/// ИИ договаривается после войны и потом раз в PeaceMonths. MODEL.md, «Мирные договоры».
/// </summary>
public static class PeaceModel
{
    /// <summary>Действующий мирный договор между странами (мир ещё не истёк и не нарушен).</summary>
    public static PeaceTreaty? TreatyBetween(World world, string a, string b) =>
        world.War?.Treaties.LastOrDefault(t => t.Between(a, b) && t.Until > world.Date);

    /// <summary>До какой даты страна нейтральна по мирному договору (не вступает в военные блоки и союзы).</summary>
    public static DateOnly? NeutralUntil(World world, string country) =>
        world.War?.Treaties.Where(t => t.Loser == country && t.Terms.Neutrality && t.Until > world.Date).Select(t => (DateOnly?)t.Until).Max();

    /// <summary>Война, где страны — главные противники.</summary>
    public static War? MainWar(World world, string a, string b) =>
        world.War?.Wars.FirstOrDefault(w => w.IsMain(a) && w.IsMain(b) && w.Side(a) != w.Side(b));

    /// <summary>Можно ли договариваться о мире: страны — главные противники в идущей войне или между ними перемирие.</summary>
    public static bool CanTalk(World world, string a, string b) =>
        a != b && (MainWar(world, a, b) != null || WarModel.CeasefireBetween(world, a, b) != null);

    /// <summary>Стороны переговоров: в идущей войне — с союзниками, иначе — сами страны.</summary>
    static (IReadOnlyList<string> A, IReadOnlyList<string> B) Sides(World world, string a, string b)
    {
        if (MainWar(world, a, b) is not { } war)
            return ([a], [b]);
        return war.Side(a) == 1 ? (war.Attackers, war.Defenders) : (war.Defenders, war.Attackers);
    }

    /// <summary>Доля населения страны (по её регионам), живущая на землях, которые держат holders.</summary>
    static double Held(World world, IReadOnlyList<string> holders, string owner)
    {
        var total = world.War!.Map.CountryPopulation.GetValueOrDefault(owner);
        if (total <= 0)
            return 0;
        return world.Regions.Where(r => r.Owner == owner && r.Controller != owner && holders.Contains(r.Controller)).Sum(r => r.Info.Population) / total;
    }

    /// <summary>
    /// Счёт войны a против b в пунктах: 100 × доля населения b на землях, которые держит сторона a, − наоборот,
    /// + ScoreExhaustion × (усталость b − усталость a), ± ScoreCapitulation, если кто-то капитулировал.
    /// </summary>
    public static double Score(World world, string a, string b)
    {
        var p = world.Data.PeaceBalance;
        var w = world.War!;
        var (sa, sb) = Sides(world, a, b);
        var score = 100 * Held(world, sa, b) - 100 * Held(world, sb, a) + p.ScoreExhaustion * (w.Armies[b].Exhaustion - w.Armies[a].Exhaustion);
        var truce = WarModel.CeasefireBetween(world, a, b);
        if (truce?.Capitulated == b)
            score += p.ScoreCapitulation;
        else if (truce?.Capitulated == a)
            score -= p.ScoreCapitulation;
        return score;
    }

    /// <summary>Счёт словами: кто сколько держит, усталость, капитуляция.</summary>
    public static string ScoreWhy(World world, string a, string b)
    {
        var w = world.War!;
        var (sa, sb) = Sides(world, a, b);
        var truce = WarModel.CeasefireBetween(world, a, b);
        return $"счёт войны {Fmt.Signed(Score(world, a, b), 0)}: {Name(world, a)} держит {Fmt.Number(Held(world, sa, b) * 100, 1)}% населения страны «{Name(world, b)}», " +
               $"та — {Fmt.Number(Held(world, sb, a) * 100, 1)}%; усталость {Fmt.Number(w.Armies[a].Exhaustion, 0)} и {Fmt.Number(w.Armies[b].Exhaustion, 0)}" +
               (truce?.Capitulated is { } c ? $"; капитулировала {Name(world, c)}" : "");
    }

    /// <summary>Регионы уступающего, которые может получить победитель: держит он сам, не столица.</summary>
    public static IEnumerable<Region> Annexable(World world, string winner, string loser)
    {
        var capital = world.War!.Map.Capitals.GetValueOrDefault(loser);
        return world.Regions.Where(r => r.Owner == loser && r.Controller == winner && r.Id != capital);
    }

    public static bool Claimed(World world, string claimant, string region) =>
        world.Data.Military.Claims.Any(c => c.Claimant == claimant && c.Regions.Contains(region));

    /// <summary>Цена региона для уступающего: 100 × доля его населения, спорный (на него претендует победитель) — × ClaimDiscount.</summary>
    public static double RegionCost(World world, Region r, string winner)
    {
        var total = world.War!.Map.CountryPopulation.GetValueOrDefault(r.Owner);
        return total > 0 ? 100 * r.Info.Population / total * (Claimed(world, winner, r.Id) ? world.Data.PeaceBalance.ClaimDiscount : 1) : 0;
    }

    /// <summary>Цена репараций: процент ВВП × лет × ReparationCost.</summary>
    public static double ReparationCost(World world, double percent)
    {
        var p = world.Data.PeaceBalance;
        return percent * p.ReparationYears * p.ReparationCost;
    }

    /// <summary>Цена условий для уступающего.</summary>
    public static double Cost(World world, string winner, PeaceTerms t)
    {
        var p = world.Data.PeaceBalance;
        return t.Annex.Sum(id => world.RegionById(id) is { } r ? RegionCost(world, r, winner) : 0) + ReparationCost(world, t.Reparations)
               + (t.Trade ? p.TradeCost : 0) + (t.Neutrality ? p.NeutralityCost : 0);
    }

    /// <summary>Что не так с условиями (null — всё в порядке): регионы — только те, что победитель может получить; репарации — из списка.</summary>
    public static string? Invalid(World world, string winner, string loser, PeaceTerms t)
    {
        var allowed = Annexable(world, winner, loser).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        if (t.Annex.FirstOrDefault(id => !allowed.Contains(id)) is { } bad)
            return $"Регион «{WarModel.RegionName(world, bad)}» передать нельзя: передаются только земли уступающей стороны, которые держит получающая, кроме столицы.";
        if (t.Reparations > 0 && !world.Data.PeaceBalance.ReparationLevels.Contains(t.Reparations))
            return "Такого размера репараций нет.";
        return null;
    }

    /// <summary>
    /// Согласится ли страна who на условия (winner получает их от loser). Уступающий: белый мир — всегда; иначе — если
    /// устал (усталость не ниже PeaceExhaustion) или капитулировал, и цена не больше счёта + Slack. Получающий — если цена
    /// не меньше счёта − Patience × его усталость.
    /// </summary>
    public static (bool Yes, string Why) Accepts(World world, string who, string winner, string loser, PeaceTerms t)
    {
        var p = world.Data.PeaceBalance;
        var w = world.War!;
        var score = Score(world, winner, loser);
        var cost = Cost(world, winner, t);
        if (who == loser)
        {
            if (t.IsWhite)
                return (true, "белый мир: каждый возвращает занятое");
            var capitulated = WarModel.CeasefireBetween(world, winner, loser)?.Capitulated == loser;
            var exhaustion = w.Armies[loser].Exhaustion;
            if (!capitulated && exhaustion < p.PeaceExhaustion)
                return (false, $"страна «{Name(world, loser)}» ещё не устала от войны (усталость {Fmt.Number(exhaustion, 0)}, уступает с {Fmt.Number(p.PeaceExhaustion, 0)})");
            return cost <= score + p.Slack
                ? (true, $"цена условий {Fmt.Number(cost, 0)} не больше счёта войны {Fmt.Number(score, 0)}")
                : (false, $"цена условий {Fmt.Number(cost, 0)} больше счёта войны {Fmt.Number(score, 0)}");
        }
        var need = score - p.Patience * w.Armies[winner].Exhaustion;
        return cost >= need - 1e-9
            ? (true, $"уступки на {Fmt.Number(cost, 0)} пунктов при счёте войны {Fmt.Number(score, 0)}")
            : (false, $"страна «{Name(world, winner)}» хочет уступок не меньше чем на {Fmt.Number(need, 0)} пунктов, а предложено {Fmt.Number(cost, 0)}");
    }

    /// <summary>Нужная победителю цена (для прогноза на экране) и предел, на который согласен уступающий.</summary>
    public static (double Need, double Limit, bool Willing) Bounds(World world, string winner, string loser)
    {
        var p = world.Data.PeaceBalance;
        var w = world.War!;
        var score = Score(world, winner, loser);
        var willing = WarModel.CeasefireBetween(world, winner, loser)?.Capitulated == loser || w.Armies[loser].Exhaustion >= p.PeaceExhaustion;
        return (score - p.Patience * w.Armies[winner].Exhaustion, score + p.Slack, willing);
    }

    /// <summary>Условия словами.</summary>
    public static string Describe(World world, string winner, string loser, PeaceTerms t)
    {
        if (t.IsWhite)
            return $"белый мир между странами «{Name(world, winner)}» и «{Name(world, loser)}»: каждая возвращает занятые земли";
        var p = world.Data.PeaceBalance;
        var items = new List<string>();
        if (t.Annex.Count > 0)
            items.Add($"передаёт регионы: {string.Join(", ", t.Annex.Select(r => WarModel.RegionName(world, r)))}");
        if (t.Reparations > 0)
            items.Add($"платит репарации {Fmt.Number(t.Reparations, 1)}% ВВП в год {p.ReparationYears} лет");
        if (t.Trade)
            items.Add("снимает санкции и открывает свободную торговлю");
        if (t.Neutrality)
            items.Add($"остаётся нейтральной {p.PeaceYears} лет — выходит из военных блоков и союзов");
        return $"{Name(world, loser)} {string.Join("; ", items)} — в пользу страны «{Name(world, winner)}». Остальные занятые земли возвращаются";
    }

    /// <summary>
    /// Подписать мир: война (если идёт) кончается, регионы из условий переходят победителю, остальное занятое между
    /// сторонами возвращается, перемирие сменяется миром на PeaceYears лет, уступки вступают в силу.
    /// </summary>
    public static PeaceTreaty Sign(World world, string winner, string loser, PeaceTerms terms)
    {
        var w = world.War!;
        var p = world.Data.PeaceBalance;
        var (sw, sl) = Sides(world, winner, loser);
        var text = Describe(world, winner, loser, terms);
        if (MainWar(world, winner, loser) is { } war)
            WarModel.End(world, war, $"Мирный договор: {text}.", peace: true);
        foreach (var id in terms.Annex)
            Annex(world, id, winner);
        foreach (var r in world.Regions.Where(r => r.IsOccupied && ((sw.Contains(r.Owner) && sl.Contains(r.Controller)) || (sl.Contains(r.Owner) && sw.Contains(r.Controller)))).ToList())
            world.TransferControl(r.Id, r.Owner);
        if (WarModel.CeasefireBetween(world, winner, loser) is { } truce)
            w.Ceasefires.Remove(truce);
        w.Offers.RemoveAll(o => (o.From == winner && o.To == loser) || (o.From == loser && o.To == winner));
        var treaty = new PeaceTreaty
        {
            Id = w.NextId++, Winner = winner, Loser = loser, Date = world.Date, Terms = terms,
            Until = world.Date.AddYears(p.PeaceYears),
            ReparationsUntil = terms.Reparations > 0 ? world.Date.AddYears(p.ReparationYears) : null,
            Text = text,
        };
        w.Treaties.Add(treaty);
        DiplomacyModel.Peace(world, winner, loser, terms.Trade, terms.Neutrality, p.PeaceRelations);
        foreach (var c in new[] { winner, loser }.Where(c => c == world.PlayerCountry))
            world.Log(c, "peace", $"Подписан мирный договор: {text}.");
        return treaty;
    }

    /// <summary>
    /// Передать регион победителю насовсем. Если регион ещё не учтён у него в экономике (не держал его на старте),
    /// вместе с регионом переходят его доля населения (по возрастам), капитала, электростанций, инфраструктуры, жилья;
    /// армия прежнего владельца уменьшается в той же доле.
    /// </summary>
    public static void Annex(World world, string regionId, string winner)
    {
        var r = world.RegionById(regionId)!;
        var loser = r.Owner;
        if (loser == winner)
            return;
        if (WarModel.Baseline(world, r) != winner)
            Move(world, r, loser, winner);
        world.War!.Baseline[regionId] = winner;
        world.TransferOwnership(regionId, winner);
        world.War.Map.Recount(world);
    }

    static void Move(World world, Region r, string from, string to)
    {
        var total = world.War!.Map.CountryPopulation.GetValueOrDefault(from);
        if (total <= 0)
            return;
        var share = Math.Min(0.9, r.Info.Population / total);
        var moved = 0.0;
        if (world.Demography?.Countries.GetValueOrDefault(from) is { } df && world.Demography.Countries.GetValueOrDefault(to) is { } dt)
            for (var g = 0; g < df.Female.Length; g++)
            {
                var (f, m) = (df.Female[g] * share, df.Male[g] * share);
                df.Female[g] -= f;
                df.Male[g] -= m;
                dt.Female[g] += f;
                dt.Male[g] += m;
                moved += f + m;
            }
        var growth = 0.0;
        if (world.Economy is { } economy && economy.Countries.GetValueOrDefault(from) is { } ef && economy.Countries.GetValueOrDefault(to) is { } et)
        {
            // Выпуск региона (доля прежнего владельца) прибавляется к выпуску нового: капитал — столько, чтобы с пришедшими
            // работниками выпуск вырос на эту долю.
            growth = et.GdpUsd > 0 ? share * ef.GdpUsd / et.GdpUsd : 0;
            var labor = et.Population > 0 ? moved / et.Population : 0;
            var industries = world.Data.Industries.Industries;
            for (var i = 0; i < ef.Capital.Length && i < et.Capital.Length; i++)
            {
                var alpha = industries[i].Alpha;
                ef.Capital[i] *= 1 - share;
                et.Capital[i] *= Math.Clamp(Math.Pow((1 + growth) / Math.Pow(1 + labor, 1 - alpha), 1 / alpha), 0.5, 2);
            }
        }
        if (world.Energy?.Countries.GetValueOrDefault(from) is { } nf && world.Energy.Countries.GetValueOrDefault(to) is { } nt)
            for (var s = 0; s < nf.Capacity.Length && s < nt.Capacity.Length; s++)
            {
                var part = nf.Capacity[s] * share;
                nf.Capacity[s] -= part;
                nt.Capacity[s] += part;
            }
        if (world.Infrastructure?.Countries.GetValueOrDefault(from) is { } inf && world.Infrastructure.Countries.GetValueOrDefault(to) is { } int_)
        {
            var population = world.Demography?.Countries.GetValueOrDefault(to)?.Total ?? 0;
            var people = population > moved ? moved / (population - moved) : 0;
            for (var t = 0; t < inf.Capacity.Length && t < int_.Capacity.Length; t++)
            {
                inf.Capacity[t] *= 1 - share;
                int_.Capacity[t] *= 1 + (growth + people) / 2;
            }
        }
        if (world.Living?.Countries.GetValueOrDefault(from) is { } lf && world.Living.Countries.GetValueOrDefault(to) is { } lt)
        {
            var stock = lf.Stock * share;
            lf.Stock -= stock;
            lt.Stock += stock;
        }
        if (world.War.Armies.GetValueOrDefault(from) is { } army)
        {
            army.Base *= 1 - share;
            army.Soldiers *= 1 - share;
        }
    }

    /// <summary>
    /// Месяц: репарации; старые предложения игроку снимаются; переговоры по перемириям — сразу после войны, потом раз в
    /// PeaceMonths.
    /// </summary>
    public static void Month(World world)
    {
        var w = world.War!;
        var p = world.Data.PeaceBalance;
        PayReparations(world);
        foreach (var offer in w.Offers.Where(o => o.Date.AddMonths(p.OfferMonths) <= world.Date).ToList())
        {
            w.Offers.Remove(offer);
            world.Log(offer.To, "peace", $"Предложение мира от страны «{Name(world, offer.From)}» больше не действует.");
        }
        foreach (var truce in w.Ceasefires.ToList())
        {
            if (truce.Talks is { } last && last.AddMonths(p.PeaceMonths) > world.Date)
                continue;
            var i = w.Ceasefires.IndexOf(truce);
            w.Ceasefires[i] = truce with { Talks = world.Date };
            Talk(world, truce.A, truce.B);
        }
    }

    /// <summary>Репарации за месяц: долг плательщика растёт, получателя — уменьшается.</summary>
    static void PayReparations(World world)
    {
        if (world.Economy is not { } economy)
            return;
        foreach (var t in world.War!.Treaties.Where(t => t.Terms.Reparations > 0 && t.ReparationsUntil > world.Date))
        {
            if (economy.Countries.GetValueOrDefault(t.Loser) is not { } payer || economy.Countries.GetValueOrDefault(t.Winner) is not { } receiver)
                continue;
            var usd = t.Terms.Reparations / 100 * payer.GdpUsd / 12;
            payer.Debt += usd * payer.ExchangeRate;
            receiver.Debt = Math.Max(0, receiver.Debt - usd * receiver.ExchangeRate);
            t.Paid += usd;
        }
    }

    /// <summary>
    /// Переговоры по перемирию: при почти равном счёте — белый мир; иначе победитель (ИИ) требует то, что даёт счёт, и
    /// уступающий (ИИ) соглашается по <see cref="Accepts"/>. Игроку ИИ предлагает мир, только если игрок устал или
    /// капитулировал (или мир белый); если победитель — игрок, уставший ИИ сам предлагает уступки.
    /// </summary>
    static void Talk(World world, string a, string b)
    {
        var p = world.Data.PeaceBalance;
        var w = world.War!;
        var player = world.PlayerCountry;
        var score = Score(world, a, b);
        var (winner, loser) = score >= 0 ? (a, b) : (b, a);
        var value = Math.Abs(score);
        var why = ScoreWhy(world, winner, loser);
        if (winner != player)
        {
            var terms = value <= p.WhiteScore ? PeaceTerms.White : Demand(world, winner, loser, value + p.Slack);
            if (loser == player)
            {
                var capitulated = WarModel.CeasefireBetween(world, winner, loser)?.Capitulated == loser;
                if (terms.IsWhite || capitulated || w.Armies[loser].Exhaustion >= p.PeaceExhaustion)
                    Offer(world, winner, loser, winner, loser, terms, why);
                return;
            }
            var (yes, reason) = Accepts(world, loser, winner, loser, terms);
            if (!yes)
                return;
            var text = Describe(world, winner, loser, terms);
            Sign(world, winner, loser, terms);
            Record(world, winner, $"Мирный договор: {text}.", why);
            Record(world, loser, $"Мирный договор: {text}.", why + "; " + reason);
            return;
        }
        // Победитель — игрок: уставший ИИ предлагает уступки, которых хватит, чтобы игрок согласился.
        if (w.Armies[loser].Exhaustion < p.PeaceExhaustion && WarModel.CeasefireBetween(world, winner, loser)?.Capitulated != loser)
            return;
        var offer = value <= p.WhiteScore ? PeaceTerms.White : Concede(world, winner, loser, Bounds(world, winner, loser).Need);
        Offer(world, loser, winner, winner, loser, offer, why);
    }

    /// <summary>
    /// Требования ИИ-победителя в пределах budget: спорные регионы, которые он держит (недемократия — и остальные занятые),
    /// потом репарации, нейтралитет (если проигравший в военном блоке или союзе), торговые уступки.
    /// </summary>
    static PeaceTerms Demand(World world, string winner, string loser, double budget)
    {
        var p = world.Data.PeaceBalance;
        var democracy = world[winner].Regime == Regime.Democracy;
        var annex = new List<string>();
        var cost = 0.0;
        foreach (var r in Annexable(world, winner, loser)
                     .Where(r => Claimed(world, winner, r.Id) || !democracy)
                     .OrderByDescending(r => Claimed(world, winner, r.Id)).ThenByDescending(r => r.Info.Population).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var c = RegionCost(world, r, winner);
            if (cost + c > budget)
                continue;
            annex.Add(r.Id);
            cost += c;
        }
        var reparations = p.ReparationLevels.Where(l => cost + ReparationCost(world, l) <= budget).DefaultIfEmpty(0).Max();
        cost += ReparationCost(world, reparations);
        var allied = DiplomacyModel.BlocsOf(world, loser).Any(x => x.Military && !DiplomacyModel.InBloc(world, x.Id, winner))
                     || world.Diplomacy?.Treaties.Any(t => t.Type == "alliance" && t.Involves(loser)) == true;
        var neutrality = allied && cost + p.NeutralityCost <= budget;
        if (neutrality)
            cost += p.NeutralityCost;
        var trade = cost + p.TradeCost <= budget
                    && (DiplomacyModel.SanctionLevel(world, loser, winner) != null || !DiplomacyModel.HasTreaty(world, winner, loser, "free_trade"));
        annex.Sort(StringComparer.Ordinal);
        return new PeaceTerms(annex, reparations, trade, neutrality);
    }

    /// <summary>Уступки уставшего ИИ игроку-победителю: торговля, спорные регионы, нейтралитет, репарации, остальные регионы — пока не хватит need.</summary>
    static PeaceTerms Concede(World world, string winner, string loser, double need)
    {
        var p = world.Data.PeaceBalance;
        var annex = new List<string>();
        var (trade, neutrality, reparations, cost) = (false, false, 0.0, 0.0);
        if (cost < need)
            (trade, cost) = (true, cost + p.TradeCost);
        var regions = Annexable(world, winner, loser).OrderByDescending(r => Claimed(world, winner, r.Id)).ThenBy(r => RegionCost(world, r, winner))
            .ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        foreach (var r in regions.Where(r => Claimed(world, winner, r.Id)))
            if (cost < need)
            {
                annex.Add(r.Id);
                cost += RegionCost(world, r, winner);
            }
        if (cost < need)
            (neutrality, cost) = (true, cost + p.NeutralityCost);
        foreach (var level in p.ReparationLevels)
            if (cost - ReparationCost(world, reparations) < need)
            {
                cost += ReparationCost(world, level) - ReparationCost(world, reparations);
                reparations = level;
            }
        foreach (var r in regions.Where(r => !Claimed(world, winner, r.Id)))
            if (cost < need)
            {
                annex.Add(r.Id);
                cost += RegionCost(world, r, winner);
            }
        annex.Sort(StringComparer.Ordinal);
        return new PeaceTerms(annex, reparations, trade, neutrality);
    }

    /// <summary>Предложить мир игроку: предложение ждёт его решения OfferMonths месяцев.</summary>
    static void Offer(World world, string from, string to, string winner, string loser, PeaceTerms terms, string why)
    {
        var w = world.War!;
        if (w.Offers.Any(o => o.From == from && o.To == to))
            return;
        w.Offers.Add(new PeaceOffer(from, to, winner, loser, terms, world.Date));
        var text = Describe(world, winner, loser, terms);
        world.Log(to, "peace", $"Страна «{Name(world, from)}» предлагает мир: {text}. Решение — в «Дипломатии», вкладка «Войны».");
        Record(world, from, $"Предлагает мир стране «{Name(world, to)}»: {text}.", why);
    }

    /// <summary>Мир, предложенный игроком: уступающий или получающий (ИИ) решает по <see cref="Accepts"/>.</summary>
    public static bool ProposeByPlayer(World world, string me, string partner, PeaceTerms terms)
    {
        if (world.PlayerCountry != me || !CanTalk(world, me, partner))
            return false;
        var (winner, loser) = Score(world, me, partner) >= 0 ? (me, partner) : (partner, me);
        if (Invalid(world, winner, loser, terms) is { } error)
        {
            world.Log(me, "peace", error);
            return false;
        }
        var (yes, why) = Accepts(world, partner, winner, loser, terms);
        if (!yes)
        {
            world.Log(me, "peace", $"Страна «{Name(world, partner)}» отвергает мир: {why}.");
            return false;
        }
        Sign(world, winner, loser, terms);
        Record(world, partner, $"Подписывает мир со страной «{Name(world, me)}»: {Describe(world, winner, loser, terms)}.", why);
        return true;
    }

    /// <summary>Игрок принимает предложение мира (если оно ещё в силе: регионы из условий всё ещё можно передать).</summary>
    public static bool AcceptOffer(World world, string me, string from)
    {
        var w = world.War!;
        if (world.PlayerCountry != me || w.Offers.FirstOrDefault(o => o.To == me && o.From == from) is not { } offer)
            return false;
        w.Offers.Remove(offer);
        if (!CanTalk(world, me, from) || Invalid(world, offer.Winner, offer.Loser, offer.Terms) is not null)
        {
            world.Log(me, "peace", "Предложение мира устарело: положение на фронте изменилось.");
            return false;
        }
        Sign(world, offer.Winner, offer.Loser, offer.Terms);
        return true;
    }

    /// <summary>Игрок отклоняет предложение мира.</summary>
    public static bool RejectOffer(World world, string me, string from)
    {
        var w = world.War!;
        if (world.PlayerCountry != me || w.Offers.FirstOrDefault(o => o.To == me && o.From == from) is not { } offer)
            return false;
        w.Offers.Remove(offer);
        world.Log(me, "peace", $"Предложение мира от страны «{Name(world, from)}» отклонено.");
        return true;
    }

    /// <summary>Решение ИИ в его журнал (у страны игрока журнала ИИ нет).</summary>
    static void Record(World world, string country, string text, string why)
    {
        if (country == world.PlayerCountry || world.Ai?.Countries.GetValueOrDefault(country) is not { } ai)
            return;
        new Ai.AiTurn(world, world[country], ai).Record("peace", text, why);
    }

    static string Name(World world, string id) => world[id].Name;
}
