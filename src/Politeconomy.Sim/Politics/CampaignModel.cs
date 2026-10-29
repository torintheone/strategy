using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Предвыборная кампания: за CampaignMonths месяцев до выборов у партии игрока появляется штаб с очками — на поездки
/// к классам, агитацию и обещания; за месяц до выборов — дебаты лидеров. Каждая партия, и партия игрока тоже, ведёт
/// кампанию сама (прибавка по организованности), штаб игрока добавляет сверх неё. Фальсификации прибавляют голоса, но могут раскрыться — тогда скандал. Обещания победившей
/// партии надо выполнить за PromiseYears лет. MODEL.md, «Кампания».
/// </summary>
public static class CampaignModel
{
    /// <summary>Месяц: затухание прибавок и скандала, кампании партий, старт кампании игрока, дебаты, сроки обещаний.</summary>
    public static void Update(World world, Country country, CountryPolitics p)
    {
        var b = world.Data.PoliticsBalance;
        foreach (var party in p.Parties)
            for (var k = 0; k < party.Campaign.Length; k++)
                party.Campaign[k] *= b.CampaignDecay;
        p.Scandal = p.Scandal * b.ScandalDecay < 0.01 ? 0 : p.Scandal * b.ScandalDecay;

        var player = world.PlayerCountry == country.Id;
        var head = PoliticsModel.HeadParty(world, country.Id);
        if (Next(p) is { } election && election <= world.Date.AddMonths(b.CampaignMonths) && election > world.Date)
        {
            // Каждая партия ведёт кампанию сама (у игрока поверх этого — штаб).
            foreach (var party in p.Parties)
                for (var k = 0; k < party.Campaign.Length; k++)
                    party.Campaign[k] = Math.Max(party.Campaign[k], b.AiCampaign * party.Organization);
            if (player && head != null)
                PlayerCampaign(world, country, p, head, election);
        }
        CheckPledges(world, country, p);
    }

    /// <summary>Ближайшие выборы — парламента или президента.</summary>
    public static DateOnly? Next(CountryPolitics p) =>
        new[] { p.NextElection, p.NextPresident }.Where(d => d != null).Min();

    /// <summary>Очков штаба в месяц: основа + за организованность партии (целое число).</summary>
    public static double PointsPerMonth(Data.PoliticsBalance b, Party party) => Math.Round(b.CampaignPoints + b.CampaignOrganization * party.Organization);

    static void PlayerCampaign(World world, Country country, CountryPolitics p, Party head, DateOnly election)
    {
        var b = world.Data.PoliticsBalance;
        var perMonth = PointsPerMonth(b, head);
        if (p.Campaign == null || p.Campaign.Election != election)
        {
            p.Campaign = new Campaign { Party = head.Id, Election = election, Points = perMonth };
            world.Log(country.Id, "campaign", $"Началась предвыборная кампания: выборы {Text.Fmt.Date(election)}. У штаба {Text.Fmt.Number(perMonth, 0)} очков " +
                                              "в месяц — на поездки, агитацию и обещания (экран «Политика»).");
        }
        else
            p.Campaign.Points = Math.Min(3 * perMonth, p.Campaign.Points + perMonth);
        if (p.Campaign.Debate == null && election <= world.Date.AddMonths(1) && p.Parties.Count >= 2)
        {
            p.Campaign.Debate = new Debate { Date = world.Date, Axis = Topic(p, world.Society![country.Id]) };
            var axis = world.Data.Parties.Axes[p.Campaign.Debate.Axis];
            world.Log(country.Id, "campaign", $"Дебаты лидеров перед выборами. Тема — {axis.Name.ToLowerInvariant()}: {axis.Debate}");
        }
        p.Version++;
    }

    /// <summary>Тема дебатов — ось, по которой классы страны расходятся сильнее всего (взвешенный разброс взглядов).</summary>
    public static int Topic(CountryPolitics p, CountrySociety s)
    {
        var best = 0;
        var bestSpread = -1.0;
        var total = s.Heads.Sum();
        for (var a = 0; a < PoliticalViews.Axes; a++)
        {
            var mean = 0.0;
            for (var k = 0; k < p.Views.Length; k++)
                mean += p.Views[k][a] * s.Heads[k] / Math.Max(1, total);
            var spread = 0.0;
            for (var k = 0; k < p.Views.Length; k++)
                spread += Math.Pow(p.Views[k][a] - mean, 2) * s.Heads[k] / Math.Max(1, total);
            if (spread > bestSpread)
                (best, bestSpread) = (a, spread);
        }
        return best;
    }

    /// <summary>Поездка к классу: прибавка к привлекательности партии у этого класса.</summary>
    public static bool Trip(World world, string countryId, int classIndex)
    {
        if (Active(world, countryId) is not { } x || classIndex < 0 || classIndex >= x.Party.Campaign.Length || !Spend(x.Campaign, world.Data.PoliticsBalance.TripCost))
            return false;
        var b = world.Data.PoliticsBalance;
        x.Party.Campaign[classIndex] = Math.Min(b.CampaignCap, x.Party.Campaign[classIndex] + b.TripBonus);
        var name = world.Data.Classes.Classes[classIndex].Name.ToLowerInvariant();
        world.Log(countryId, "campaign", $"Поездка и встречи: вы обратились к классу «{name}».");
        x.P.Version++;
        return true;
    }

    /// <summary>Агитация в СМИ: небольшая прибавка у всех классов.</summary>
    public static bool Media(World world, string countryId)
    {
        if (Active(world, countryId) is not { } x || !Spend(x.Campaign, world.Data.PoliticsBalance.MediaCost))
            return false;
        var b = world.Data.PoliticsBalance;
        for (var k = 0; k < x.Party.Campaign.Length; k++)
            x.Party.Campaign[k] = Math.Min(b.CampaignCap, x.Party.Campaign[k] + b.MediaBonus);
        world.Log(countryId, "campaign", "Агитация в СМИ: ролики, интервью, выступления.");
        x.P.Version++;
        return true;
    }

    /// <summary>Цена обещания: каждое следующее в этой кампании дороже (PromiseCost × (1 + уже данных обещаний)).</summary>
    public static double PromiseCost(Data.PoliticsBalance b, CountryPolitics p, Campaign c) =>
        b.PromiseCost * (1 + p.Pledges.Count(x => x.Party == c.Party && x.Deadline == null));

    /// <summary>
    /// Обещание закона: классы, которым он нравится, тянутся к партии, остальные — отворачиваются
    /// (PromiseWeight × изменение отношения класса).
    /// </summary>
    public static bool Promise(World world, string countryId, string lawId, string variant)
    {
        if (Active(world, countryId) is not { } x || world.Laws?.Countries.GetValueOrDefault(countryId) is not { } laws)
            return false;
        var data = world.Data;
        var law = data.Laws.Laws.FirstOrDefault(l => l.Id == lawId);
        if (law == null || law.Variants.All(v => v.Id != variant) || laws.Current[lawId] == variant
            || x.P.Pledges.Any(pl => pl.Law == lawId) || !Spend(x.Campaign, PromiseCost(data.PoliticsBalance, x.P, x.Campaign)))
            return false;
        var b = data.PoliticsBalance;
        var shifts = Legislation.Shifts(data, law, laws.Current[lawId], variant);
        for (var k = 0; k < x.Party.Campaign.Length && k < shifts.Length; k++)
            x.Party.Campaign[k] = Math.Clamp(x.Party.Campaign[k] + b.PromiseWeight * shifts[k], -b.CampaignCap, b.CampaignCap);
        x.P.Pledges.Add(new CampaignPromise { Party = x.Party.Id, Law = lawId, Variant = variant });
        world.Log(countryId, "campaign", $"Вы пообещали избирателям: «{law.Name}: {law.Variant(variant).Name.ToLowerInvariant()}».");
        x.P.Version++;
        return true;
    }

    /// <summary>
    /// Ответ на дебатах: −1, 0 или +1 по оси темы. Классы, чьи взгляды на стороне ответа, тянутся к партии
    /// (DebateWeight × ответ × взгляд класса / 100), к этому — обаяние лидера и удача выступления, у всех классов.
    /// </summary>
    public static bool AnswerDebate(World world, string countryId, int answer)
    {
        if (Active(world, countryId) is not { Campaign.Debate: { Answer: null } debate } x || answer is < -1 or > 1)
            return false;
        var b = world.Data.PoliticsBalance;
        var leader = PoliticsModel.LeaderOf(world, countryId, x.Party);
        var performance = b.DebateCharisma * (leader.Popularity - 50) / 50 + PoliticsModel.Rng(world).NextGaussian() * b.DebateNoise;
        for (var k = 0; k < x.Party.Campaign.Length && k < x.P.Views.Length; k++)
        {
            var stance = b.DebateWeight * answer * x.P.Views[k][debate.Axis] / 100;
            x.Party.Campaign[k] = Math.Clamp(x.Party.Campaign[k] + stance + performance, -b.CampaignCap, b.CampaignCap);
        }
        debate.Answer = answer;
        var verdict = performance switch
        {
            > 0.12 => "Вы выступили блестяще: зрители запомнили вас.",
            > 0 => "Вы выступили уверенно.",
            > -0.12 => "Выступление вышло средним.",
            _ => "Выступление не задалось: соперники выглядели убедительнее.",
        };
        world.Log(countryId, "campaign", "Дебаты лидеров прошли. " + verdict);
        x.P.Version++;
        return true;
    }

    /// <summary>Фальсификации на ближайших выборах: 0 — честные, 1 — вбросы, 2 — массовые.</summary>
    public static bool SetFraud(World world, string countryId, int level)
    {
        if (Active(world, countryId) is not { } x || level < 0 || level >= world.Data.PoliticsBalance.FraudShare.Count)
            return false;
        x.Campaign.Fraud = level;
        x.P.Version++;
        return true;
    }

    /// <summary>
    /// Риск разоблачения фальсификаций уровня level: базовый × (0,3 + 0,7 × свобода прессы / 100) × (0,5 + доля
    /// оппозиции в опросах), не больше 95%.
    /// </summary>
    public static double FraudRisk(World world, Country country, int level)
    {
        var b = world.Data.PoliticsBalance;
        if (level <= 0 || level >= b.FraudRisk.Count)
            return 0;
        var press = country.Stat("press_freedom_index");
        var p = world.Politics![country.Id];
        var opposition = p.Parties.Where(x => !x.Ruling).Sum(x => x.Poll);
        return Math.Min(0.95, b.FraudRisk[level] * (0.3 + 0.7 * (double.IsFinite(press) ? press : 50) / 100) * (0.5 + opposition));
    }

    /// <summary>Сколько фальсификации прибавляют к доле партии игрока на этих выборах (и какой партии).</summary>
    public static (string? Party, double Share) FraudBoost(World world, Country country)
    {
        var p = world.Politics![country.Id];
        return world.PlayerCountry == country.Id && p.Campaign is { Fraud: > 0 } c && c.Election <= world.Date
            ? (c.Party, world.Data.PoliticsBalance.FraudShare[c.Fraud])
            : (null, 0);
    }

    /// <summary>
    /// Кампания закончилась выборами: при фальсификациях — бросок на разоблачение; если партия победила (правит),
    /// обещания получают срок, иначе забываются.
    /// </summary>
    public static void End(World world, Country country, CountryPolitics p)
    {
        if (p.Campaign is not { } c)
            return;
        var b = world.Data.PoliticsBalance;
        if (c.Fraud > 0 && world.PlayerCountry == country.Id)
        {
            if (PoliticsModel.Rng(world).Chance(FraudRisk(world, country, c.Fraud)))
            {
                p.Scandal += b.Scandal * c.Fraud;
                var leader = world.Government![country.Id].Leader;
                leader.Popularity = Math.Max(1, leader.Popularity - b.ScandalPopularity * c.Fraud);
                world.Log(country.Id, "fraud", "Фальсификации раскрыты: журналисты и наблюдатели доказали вбросы. Доверие к власти рухнуло, " +
                                               "недовольные выходят на улицы.");
            }
            else
                world.Log(country.Id, "fraud", "Подсчёт голосов прошёл «как надо»: разоблачений не было.");
        }
        var won = p.Party(c.Party) is { Ruling: true };
        foreach (var pledge in p.Pledges.Where(x => x.Party == c.Party && x.Deadline == null).ToList())
        {
            if (won)
                pledge.Deadline = world.Date.AddYears(b.PromiseYears);
            else
                p.Pledges.Remove(pledge);
        }
        p.Campaign = null;
        p.Version++;
    }

    /// <summary>Сроки обещаний кампании: выполненное — партии плюс, просроченное — минус партии и популярности лидера.</summary>
    static void CheckPledges(World world, Country country, CountryPolitics p)
    {
        var b = world.Data.PoliticsBalance;
        var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
        foreach (var pledge in p.Pledges.Where(x => x.Deadline != null).ToList())
        {
            var party = p.Party(pledge.Party);
            var law = world.Data.Law(pledge.Law);
            var title = $"«{law.Name}: {law.Variant(pledge.Variant).Name.ToLowerInvariant()}»";
            if (party is not { Ruling: true })
                p.Pledges.Remove(pledge);
            else if (laws?.Current[pledge.Law] == pledge.Variant)
            {
                p.Pledges.Remove(pledge);
                party.Momentum += b.KeptPromise;
                world.Log(country.Id, "campaign", $"Обещание выполнено: {title}. Избиратели это запомнили.");
            }
            else if (world.Date > pledge.Deadline)
            {
                p.Pledges.Remove(pledge);
                party.Momentum -= b.BrokenPromise;
                var leader = PoliticsModel.LeaderOf(world, country.Id, party);
                leader.Popularity = Math.Max(1, leader.Popularity - b.BrokenPopularity);
                world.Log(country.Id, "campaign", $"Обещание не выполнено: {title}. Доверие к партии и её лидеру упало.");
            }
        }
    }

    /// <summary>Вариант обещания и сколько он, по оценке, прибавит партии (доля от всех голосов, может быть меньше нуля).</summary>
    public sealed record Option(string Law, string Variant, double Gain);

    /// <summary>
    /// Оценки для штаба: доля партии у каждого класса, прибавка к общей доле от поездки к классу и от агитации,
    /// лучшие обещания, прибавка от каждого ответа на дебатах (без учёта удачи). Прибавка к доле от сдвига du
    /// привлекательности у класса k ≈ вес класса × доля × (1 − доля) × du (наклон логит-модели).
    /// </summary>
    public sealed record Outlook(double[] ClassShare, double[] TripGain, double MediaGain, IReadOnlyList<Option> Options, double[] DebateGain);

    public static Outlook? Estimate(World world, Country country)
    {
        if (Active(world, country.Id) is not { } x)
            return null;
        var data = world.Data;
        var b = data.PoliticsBalance;
        var s = world.Society![country.Id];
        var poll = Polls.Compute(b, country, x.P, s, world.Government![country.Id], PoliticsModel.Majoritarian(world, country), world.Date,
            Polls.EconomicVote(b, world.Economy![country.Id]));
        var index = x.P.Parties.IndexOf(x.Party);
        var classes = x.Party.Campaign.Length;
        var total = Math.Max(1, s.Heads.Sum());
        var share = Enumerable.Range(0, classes).Select(k => k < poll.ByClass.Length && poll.ByClass[k].Length > index ? poll.ByClass[k][index] : 0).ToArray();
        var slope = Enumerable.Range(0, classes).Select(k => s.Heads[k] / total * share[k] * (1 - share[k])).ToArray();
        double Gain(Func<int, double> du) => Enumerable.Range(0, classes).Sum(k => slope[k] * du(k));

        var trip = Enumerable.Range(0, classes).Select(k => Gain(j => j == k ? Math.Min(b.TripBonus, b.CampaignCap - x.Party.Campaign[k]) : 0)).ToArray();
        var media = Gain(k => Math.Min(b.MediaBonus, b.CampaignCap - x.Party.Campaign[k]));
        var options = new List<Option>();
        if (world.Laws?.Countries.GetValueOrDefault(country.Id) is { } laws)
            foreach (var law in data.Laws.Laws.Where(l => x.P.Pledges.All(pl => pl.Law != l.Id)))
            {
                var best = law.Variants.Where(v => v.Id != laws.Current[law.Id])
                    .Select(v => (v.Id, Shifts: Legislation.Shifts(data, law, laws.Current[law.Id], v.Id)))
                    .Select(v => new Option(law.Id, v.Id, Gain(k => b.PromiseWeight * (k < v.Shifts.Length ? v.Shifts[k] : 0))))
                    .MaxBy(o => o.Gain);
                if (best is { Gain: > 0 })
                    options.Add(best);
            }
        var debate = x.Campaign.Debate is { Answer: null } d
            ? new[] { -1, 0, 1 }.Select(a => Gain(k => b.DebateWeight * a * x.P.Views[k][d.Axis] / 100)).ToArray()
            : [];
        return new Outlook(share, trip, media, options.OrderByDescending(o => o.Gain).Take(5).ToList(), debate);
    }

    sealed record ActiveCampaign(CountryPolitics P, Campaign Campaign, Party Party);

    static ActiveCampaign? Active(World world, string countryId) =>
        world.PlayerCountry == countryId && world.Politics?.Countries.GetValueOrDefault(countryId) is { Campaign: { } c } p && p.Party(c.Party) is { } party
            ? new ActiveCampaign(p, c, party)
            : null;

    static bool Spend(Campaign c, double cost)
    {
        if (c.Points < cost - 1e-9)
            return false;
        c.Points -= cost;
        return true;
    }
}
