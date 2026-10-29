using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Выборы: календарь, явка классов, голосование (как опрос, но с явкой и погрешностью дня выборов), места
/// по избирательной системе из закона, президентские выборы с возможным вторым туром, итоги по регионам.
/// Кто после выборов правит — <see cref="Cabinet"/> и <see cref="CoalitionTalksModel"/>. MODEL.md, «Выборы».
/// </summary>
public static class Elections
{
    /// <summary>Ближайшие выборы на старте: из данных («год-месяц») или случайный месяц в пределах срока.</summary>
    public static void Schedule(World world, Country country, CountryPolitics p)
    {
        var record = world.Data.PoliticsOf(country.Id);
        if (p.Parties.Count == 0)
            return;
        p.NextElection = Parse(record.NextElection) ?? FirstOfMonth(world.Date.AddMonths(PoliticsModel.Rng(world).Next(1, record.Term * 12 + 1)));
        if (HasPresident(world, country))
            p.NextPresident = Parse(record.NextPresident)
                              ?? FirstOfMonth(world.Date.AddMonths(PoliticsModel.Rng(world).Next(1, (record.PresidentTerm ?? 5) * 12 + 1)));
    }

    static DateOnly? Parse(string? yearMonth) =>
        yearMonth is { Length: 7 } && int.TryParse(yearMonth[..4], out var y) && int.TryParse(yearMonth[5..], out var m) ? new DateOnly(y, m, 1) : null;

    static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Главу (президента) избирают напрямую: президентская или смешанная республика с партиями.</summary>
    public static bool HasPresident(World world, Country country) =>
        country.System is PoliticalSystem.PresidentialRepublic or PoliticalSystem.SemiPresidential && world.Data.PoliticsOf(country.Id).PresidentTerm != null;

    /// <summary>Избирательная система: по действующему закону «Избирательная система», до законов — из данных.</summary>
    public static string Electoral(World world, Country country) =>
        world.Laws?.Countries.GetValueOrDefault(country.Id)?.Current.GetValueOrDefault("electoral_system") ?? world.Data.PoliticsOf(country.Id).Electoral;

    /// <summary>
    /// Явка класса: обычная (classes.json → turnout) × (1 + TurnoutRadicalism × прирост радикализма / 100
    /// − TurnoutApathy × недовольство), в пределах TurnoutMin…TurnoutMax.
    /// </summary>
    public static double[] Turnout(PoliticsBalance b, IReadOnlyList<ClassRecord> classes, CountrySociety s, string country)
    {
        // У каждой страны своя привычка ходить на выборы (обязательное голосование, традиции) — постоянная поправка.
        var habit = 1 + b.TurnoutSpread * Normal(country, 7);
        return classes.Select((c, k) =>
        {
            var radical = Math.Max(0, s.Radicalism[k] - c.Radicalism) / 100;
            var apathy = Math.Max(0, 45 - s.Satisfaction[k]) / 45;
            return Math.Clamp(c.Turnout * habit * (1 + b.TurnoutRadicalism * radical - b.TurnoutApathy * apathy), b.TurnoutMin, b.TurnoutMax);
        }).ToArray();
    }

    /// <summary>Голосование: доли партий из list (с учётом явки) и погрешность дня выборов; общая явка.</summary>
    static (double[] Shares, double Turnout) Vote(World world, Country country, IReadOnlyList<Party> list, Func<Party, double>? extra = null)
    {
        var b = world.Data.PoliticsBalance;
        var p = world.Politics![country.Id];
        var s = world.Society![country.Id];
        var g = world.Government![country.Id];
        var turnout = Turnout(b, world.Data.Classes.Classes, s, country.Id);
        var weights = s.Heads.Select((h, k) => h * turnout[k]).ToArray();
        var result = Polls.Compute(b, country, p, s, g, PoliticsModel.Majoritarian(world, country), world.Date,
            Polls.EconomicVote(b, world.Economy![country.Id]), list, extra, weights);
        var shares = result.Shares.Select(x => x * Math.Exp(PoliticsModel.Rng(world).NextGaussian() * b.ElectionNoise)).ToArray();
        // Фальсификации в пользу партии игрока: прибавка к её доле до пересчёта долей.
        var (fraudParty, fraudShare) = CampaignModel.FraudBoost(world, country);
        var before = shares.Sum();
        for (var i = 0; i < list.Count; i++)
            if (list[i].Id == fraudParty)
                shares[i] += fraudShare * before;
        var total = shares.Sum();
        var heads = s.Heads.Sum();
        return (shares.Select(x => total > 0 ? x / total : 0).ToArray(), heads > 0 ? weights.Sum() / heads : 0);
    }

    /// <summary>Выборы парламента: голоса, места, кто правит дальше.</summary>
    public static ElectionResult HoldParliament(World world, Country country)
    {
        var data = world.Data;
        var b = data.PoliticsBalance;
        var record = data.PoliticsOf(country.Id);
        var p = world.Politics![country.Id];
        var g = world.Government![country.Id];
        var parliament = g.Parliament;
        var parties = p.Parties;
        var (votes, turnout) = Vote(world, country, parties);
        int[] seats;
        if (country.Regime == Regime.Authoritarian)
        {
            // Формальный парламент: партия власти получает долю мест правящего блока, остальные делят остаток.
            var range = data.GovernmentBalance.Parliament.AuthoritarianRuling;
            var powerIndex = Math.Max(0, parties.FindIndex(x => x.Power));
            var powerSeats = parties.Count == 1 ? parliament.Seats
                : (int)Math.Round(parliament.Seats * (range[0] + PoliticsModel.Rng(world).NextDouble() * (range[1] - range[0])));
            var others = Enumerable.Range(0, parties.Count).Where(i => i != powerIndex).ToList();
            var rest = Seats.LargestRemainder(others.Select(i => votes[i]).ToArray(), parliament.Seats - powerSeats);
            seats = new int[parties.Count];
            seats[powerIndex] = powerSeats;
            for (var j = 0; j < others.Count; j++)
                seats[others[j]] = rest[j];
        }
        else
            seats = Seats.Allocate(Electoral(world, country), votes, parliament.Seats, record.Threshold, b.MajoritarianPower);

        var result = new ElectionResult { Date = world.Date, Kind = "parliament", Turnout = Math.Round(turnout, 4) };
        for (var i = 0; i < parties.Count; i++)
        {
            result.Votes[parties[i].Id] = Math.Round(votes[i], 4);
            if (seats[i] > 0)
                result.Seats[parties[i].Id] = seats[i];
        }
        var largest = Enumerable.Range(0, parties.Count).OrderByDescending(i => seats[i]).ThenByDescending(i => votes[i]).First();
        result.Winner = parties[largest].Id;
        AssignRegions(world, country, result, parties, votes);
        parliament.Factions = Enumerable.Range(0, parties.Count)
            .Where(i => seats[i] > 0)
            .OrderByDescending(i => seats[i])
            .Select(i => new Faction { Party = parties[i].Id, Seats = seats[i], Ruling = parties[i].Ruling })
            .ToList();

        var head = PoliticsModel.HeadParty(world, country.Id);
        var before = head?.Id;
        if (HasPresident(world, country) || country.Regime != Regime.Democracy || head == null)
        {
            // Президент (или партия власти) остаётся; парламентское большинство собирается вокруг его партии.
            var anchor = head == null ? largest : parties.IndexOf(head);
            Cabinet.Form(world, country, Coalition.Form(parties, seats, b, anchor, Coalition.Rival(seats, anchor)).Select(i => parties[i].Id).ToList(),
                parties[anchor], null);
        }
        else if (Enumerable.Range(0, parties.Count).Where(i => parties[i].Ruling).Sum(i => seats[i]) * 2 > parliament.Seats)
            // Правящая коалиция сохранила большинство — правит дальше.
            Cabinet.Form(world, country, parties.Where(x => x.Ruling).Select(x => x.Id).ToList(), head, null);
        else if (world.PlayerCountry == country.Id && parties[largest] == head && seats[largest] * 2 <= parliament.Seats)
            CoalitionTalksModel.Start(world, country, head, seats);
        else
        {
            var ruling = Coalition.Form(parties, seats, b);
            var headParty = parties[ruling[0]];
            Cabinet.Form(world, country, ruling.Select(i => parties[i].Id).ToList(), headParty,
                headParty == head ? null : headParty.Leader);
        }
        result.PowerChanged = PoliticsModel.HeadParty(world, country.Id)?.Id != before;
        Finish(world, country, p, result);
        p.NextElection = FirstOfMonth(world.Date.AddYears(record.Term));
        return result;
    }

    /// <summary>Выборы президента: кандидаты партий, первый тур, при необходимости второй; победитель возглавляет власть.</summary>
    public static ElectionResult HoldPresident(World world, Country country)
    {
        var data = world.Data;
        var b = data.PoliticsBalance;
        var record = data.PoliticsOf(country.Id);
        var p = world.Politics![country.Id];
        var g = world.Government![country.Id];
        var parties = p.Parties;
        var head = PoliticsModel.HeadParty(world, country.Id);

        // Кандидаты: лидеры партий; если главе нельзя избираться ещё раз, его партия выдвигает преемника.
        var candidates = parties.ToDictionary(x => x, x => PoliticsModel.LeaderOf(world, country.Id, x));
        Character? successor = null;
        if (head != null && TermLimited(world, country, g.Leader))
        {
            successor = PartyFactory.PartyLeader(world, country, head);
            candidates[head] = successor;
        }
        double Extra(Party x) => b.CandidateWeight * (candidates[x].Popularity - 50) / 50;
        var (first, turnout) = Vote(world, country, parties, Extra);
        var result = new ElectionResult { Date = world.Date, Kind = "president", Turnout = Math.Round(turnout, 4) };
        for (var i = 0; i < parties.Count; i++)
        {
            result.Votes[parties[i].Id] = Math.Round(first[i], 4);
            result.Candidates[parties[i].Id] = candidates[parties[i]].Name;
        }
        var order = Enumerable.Range(0, parties.Count).OrderByDescending(i => first[i]).ToList();
        var winner = parties[order[0]];
        var final = first;
        var finalists = parties;
        if (record.TwoRounds && parties.Count > 1 && first[order[0]] <= 0.5)
        {
            finalists = [parties[order[0]], parties[order[1]]];
            var (second, _) = Vote(world, country, finalists, Extra);
            result.Runoff = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [finalists[0].Id] = Math.Round(second[0], 4),
                [finalists[1].Id] = Math.Round(second[1], 4),
            };
            winner = second[0] >= second[1] ? finalists[0] : finalists[1];
            final = second;
        }
        result.Winner = winner.Id;
        AssignRegions(world, country, result, finalists, final);

        var winnerPerson = candidates[winner];
        var seats = parties.Select(x => g.Parliament.Factions.FirstOrDefault(f => f.Party == x.Id)?.Seats ?? 0).ToArray();
        var ruling = Coalition.Form(parties, seats, b, parties.IndexOf(winner), Coalition.Rival(seats, parties.IndexOf(winner)))
            .Select(i => parties[i].Id).ToList();
        if (winner == head && successor == null)
            Cabinet.Form(world, country, ruling, winner, null);
        else
            Cabinet.Form(world, country, ruling, winner, winnerPerson, successor);
        if (winner != head && successor != null)
            head!.Leader = successor;
        g.Leader.Terms++;
        result.PowerChanged = winner != head;
        Finish(world, country, p, result);
        p.NextPresident = FirstOfMonth(world.Date.AddYears(record.PresidentTerm ?? 5));
        return result;
    }

    /// <summary>Главе нельзя избираться снова: закон «Сроки главы государства» и сколько сроков он уже отслужил.</summary>
    public static bool TermLimited(World world, Country country, Character head)
    {
        var limit = world.Laws?.Countries.GetValueOrDefault(country.Id)?.Current.GetValueOrDefault("term_limits") switch
        {
            "one" => 1,
            "two" => 2,
            _ => int.MaxValue,
        };
        return head.Terms >= limit;
    }

    /// <summary>Записать итог, журнал и счёт правления игрока.</summary>
    static void Finish(World world, Country country, CountryPolitics p, ElectionResult result)
    {
        p.Elections.Add(result);
        var keep = world.Data.PoliticsBalance.ElectionsKept;
        if (p.Elections.Count > keep)
            p.Elections.RemoveRange(0, p.Elections.Count - keep);
        p.Version++;
        if (world.Reign is { } reign && reign.Country == country.Id)
        {
            if (result.PowerChanged)
                reign.ElectionsLost++;
            else
                reign.ElectionsWon++;
        }
        world.Log(country.Id, "election", Describe(world, country, p, result));
    }

    static string Describe(World world, Country country, CountryPolitics p, ElectionResult r)
    {
        var winner = p.Party(r.Winner);
        var turnout = Text.Fmt.Value("percent", r.Turnout * 100);
        if (r.Kind == "president")
        {
            var name = r.Candidates.GetValueOrDefault(r.Winner ?? "", "");
            var share = (r.Runoff ?? r.Votes).GetValueOrDefault(r.Winner ?? "") * 100;
            var round = r.Runoff != null ? " во втором туре" : "";
            return $"Президентские выборы: победил{(world.Government![country.Id].Leader.Female ? "а" : "")} {name} ({winner?.Short}){round} " +
                   $"с {Text.Fmt.Value("percent", share)}, явка {turnout}." + (r.PowerChanged ? " Власть сменилась." : "");
        }
        var parliament = world.Government![country.Id].Parliament.Name;
        var first = p.Parties.OrderByDescending(x => r.Votes.GetValueOrDefault(x.Id)).Take(3)
            .Select(x => $"{x.Short} {Text.Fmt.Value("percent", r.Votes.GetValueOrDefault(x.Id) * 100)}");
        return $"Выборы в {parliament.ToLowerInvariant()}: {string.Join(", ", first)}; явка {turnout}." +
               (r.PowerChanged ? $" К власти приходит {Text.Fmt.Quote(winner?.Name ?? "")}." : "");
    }

    /// <summary>
    /// Итоги по регионам страны. У каждого региона свой постоянный сдвиг взглядов (случайный по его id и по плотности
    /// населения: города свободнее и открытее, село традиционнее); доли партий в регионе ∝ общая доля ×
    /// e^(сдвиг · (позиция партии − средняя позиция) / 10 000 / RegionScale).
    /// </summary>
    static void AssignRegions(World world, Country country, ElectionResult result, IReadOnlyList<Party> parties, IReadOnlyList<double> shares)
    {
        var b = world.Data.PoliticsBalance;
        var regions = world.Regions.Where(r => r.Owner == country.Id).ToList();
        if (regions.Count == 0 || parties.Count == 0)
            return;
        static double LogDensity(Region r) => Math.Log10(Math.Max(0.1, r.Info.Population / Math.Max(1, r.Info.AreaKm2)));
        var meanDensity = regions.Average(LogDensity);
        var mean = new double[PoliticalViews.Axes];
        for (var i = 0; i < parties.Count; i++)
            for (var a = 0; a < PoliticalViews.Axes; a++)
                mean[a] += shares[i] * parties[i].Position[a];
        foreach (var region in regions)
        {
            var urban = LogDensity(region) - meanDensity;
            var lean = new double[PoliticalViews.Axes];
            for (var a = 0; a < PoliticalViews.Axes; a++)
                lean[a] = Normal(region.Id, a) * b.RegionSpread;
            lean[1] -= b.RegionDensity * urban;
            lean[2] -= b.RegionDensity * urban;
            var best = 0;
            var bestScore = double.NegativeInfinity;
            for (var i = 0; i < parties.Count; i++)
            {
                var dot = 0.0;
                for (var a = 0; a < PoliticalViews.Axes; a++)
                    dot += lean[a] * (parties[i].Position[a] - mean[a]);
                var score = Math.Log(Math.Max(1e-9, shares[i])) + dot / 10000 / b.RegionScale;
                if (score > bestScore)
                    (best, bestScore) = (i, score);
            }
            result.Regions[region.Id] = parties[best].Id;
        }
    }

    /// <summary>Постоянное «нормальное» случайное число для id и номера: по хешу, не трогает генератор партии.</summary>
    static double Normal(string id, int axis)
    {
        var sum = 0.0;
        for (var j = 0; j < 4; j++)
        {
            var h = 2166136261u;
            foreach (var ch in $"{id}/{axis}/{j}")
                h = (h ^ ch) * 16777619u;
            sum += (h & 0xFFFFFF) / (double)0x1000000;
        }
        return (sum - 2) * Math.Sqrt(3);
    }

    /// <summary>Итог «прошлых выборов» на старте: по стартовым местам и опросу (для карты и сравнения).</summary>
    public static void RecordStart(World world, Country country, CountryPolitics p)
    {
        var record = world.Data.PoliticsOf(country.Id);
        var g = world.Government![country.Id];
        var result = new ElectionResult
        {
            Date = FirstOfMonth((p.NextElection ?? world.Date).AddYears(-record.Term)),
            Kind = "parliament",
            Turnout = Math.Round(Turnout(world.Data.PoliticsBalance, world.Data.Classes.Classes, world.Society![country.Id], country.Id)
                .Zip(world.Society![country.Id].Heads).Sum(x => x.First * x.Second) / Math.Max(1, world.Society![country.Id].Heads.Sum()), 4),
        };
        if (result.Date > world.Date)
            result.Date = FirstOfMonth(world.Date.AddYears(-1));
        foreach (var party in p.Parties)
            result.Votes[party.Id] = Math.Round(party.Poll, 4);
        foreach (var f in g.Parliament.Factions.Where(f => f.Party != null))
            result.Seats[f.Party!] = f.Seats;
        result.Winner = p.Parties.OrderByDescending(x => result.Seats.GetValueOrDefault(x.Id)).ThenByDescending(x => x.Poll).First().Id;
        AssignRegions(world, country, result, p.Parties, p.Parties.Select(x => x.Poll).ToArray());
        p.Elections.Add(result);
    }
}
