using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Смена власти без выборов: вотум недоверия и импичмент (в демократиях), дворцовый переворот (когда элиты недовольны
/// и легитимность низка), военный переворот (когда армия нелояльна), хунта и передача власти гражданским, революция
/// (восстание, которое армия не подавила). Если власть теряет игрок — тот же выбор, что после проигранных выборов.
/// MODEL.md, «Смена власти без выборов».
/// </summary>
public static class PowerModel
{
    /// <summary>Вероятности за месяц для страны (для интерфейса и для броска).</summary>
    public sealed record Threats(double NoConfidence, double Impeachment, double PalaceCoup, double MilitaryCoup, double Revolution)
    {
        public double Total => NoConfidence + Impeachment + PalaceCoup + MilitaryCoup + Revolution;
    }

    /// <summary>Месяц: передача власти хунтой, затем по очереди броски на революцию, военный и дворцовый переворот, импичмент, вотум.</summary>
    public static void Update(World world, Country country, CountryPolitics p)
    {
        if (p.Junta?.HandOver is { } handOver && world.Date >= handOver)
        {
            HandOver(world, country, p);
            return;
        }
        var b = world.Data.PowerBalance;
        var coup = p.Changes.Count > 0 && p.Changes[^1] is { Kind: "palace_coup" or "military_coup" } last && last.Date == p.Formed;
        if (p.Formed is { } formed && world.Date < formed.AddMonths(coup ? b.CoupGrace : b.GraceMonths))
            return;
        var t = Estimate(world, country, p);
        var rng = PoliticsModel.Rng(world);
        if (t.Revolution > 0 && rng.Chance(t.Revolution) && p.Protests.Where(x => x.Stage >= 4).MaxBy(x => x.Months) is { } uprising)
            Revolution(world, country, p, uprising.Class);
        else if (t.MilitaryCoup > 0 && rng.Chance(t.MilitaryCoup))
            MilitaryCoup(world, country, p);
        else if (t.PalaceCoup > 0 && rng.Chance(t.PalaceCoup))
            PalaceCoup(world, country, p);
        else if (t.Impeachment > 0 && rng.Chance(t.Impeachment))
            Impeach(world, country, p);
        else if (t.NoConfidence > 0 && rng.Chance(t.NoConfidence))
            NoConfidence(world, country, p);
    }

    /// <summary>Вероятности смены власти в этом месяце (формулы — в PowerBalance и MODEL.md).</summary>
    public static Threats Estimate(World world, Country country, CountryPolitics p)
    {
        var b = world.Data.PowerBalance;
        var g = world.Government![country.Id];
        var approval = Stat(country, "approval_pct", 50);
        var legitimacy = Stat(country, "legitimacy", 50);
        var anger = Math.Max(0, (b.ApprovalLow - approval) / b.ApprovalScale);

        double noConfidence = 0, impeachment = 0;
        var parliament = g.Parliament;
        if (parliament is { Kind: "real", Seats: > 0 } && country.Regime != Regime.Authoritarian && p.Parties.Count >= 2 && p.Talks == null)
        {
            if (country.System is PoliticalSystem.ParliamentaryRepublic or PoliticalSystem.ConstitutionalMonarchy or PoliticalSystem.SemiPresidential)
            {
                var minority = parliament.RulingSeats * 2 <= parliament.Seats ? 1 : 0;
                noConfidence = b.NoConfidence * (minority + anger + p.Unrest / b.UnrestScale) * (minority > 0 || anger > 0 ? 1 : 0);
            }
            if (Elections.HasPresident(world, country))
                impeachment = b.Impeachment * (b.ImpeachScandal * p.Scandal + Math.Max(0, (b.ImpeachApproval - approval) / b.ImpeachScale));
        }

        var legitimacyAnger = Math.Max(0, (b.LegitimacyLow - legitimacy) / b.LegitimacyScale);
        var prone = b.CoupProne[Math.Clamp((int)country.Income, 0, b.CoupProne.Count - 1)];
        double palace = 0;
        if (country.Regime != Regime.Democracy)
        {
            var elite = Elite(world, p);
            palace = b.PalaceCoup * prone * (Math.Max(0, (b.EliteLow - elite) / b.EliteScale) + legitimacyAnger) * (country.Regime == Regime.Hybrid ? b.HybridCoup : 1);
        }

        double military = 0;
        if (country.System != PoliticalSystem.MilitaryRegime)
        {
            var army = Math.Max(0, (b.ArmyLow - p.Security.ArmyLoyalty) / b.ArmyScale);
            military = b.MilitaryCoup * prone * army * (1 + legitimacyAnger + p.Unrest / b.CoupUnrest);
            if (country.Regime == Regime.Democracy)
                military *= Stat(country, "democracy_index", 50) >= b.StableDemocracy ? b.StableCoup : b.DemocracyCoup;
        }

        var revolution = p.Protests.Any(x => x.Stage >= 4) ? b.Revolution * (1 - p.Security.ArmyLoyalty / 100) : 0;
        return new Threats(Clamp(noConfidence), Clamp(impeachment), Clamp(palace), Clamp(military), Clamp(revolution));
    }

    static double Clamp(double x) => Math.Clamp(x, 0, 0.5);

    static double Stat(Country country, string id, double fallback) => country.Stat(id) is var v && double.IsFinite(v) ? v : fallback;

    /// <summary>Одобрение элит: группы из Elites (силовики, капитал), взвешенные по влиянию.</summary>
    public static double Elite(World world, CountryPolitics p)
    {
        var elites = p.Groups.Where(g => world.Data.PowerBalance.Elites.Contains(g.Id)).ToList();
        var weight = elites.Sum(g => g.Influence);
        return weight > 0 ? elites.Sum(g => g.Influence * g.Approval) / weight : 50;
    }

    /// <summary>
    /// Вотум недоверия: против — оппозиция и часть партнёров по коалиции (тем больше, чем ниже одобрение). Если против
    /// больше половины, правительство собирает крупнейшая оппозиционная партия.
    /// </summary>
    public static bool NoConfidence(World world, Country country, CountryPolitics p)
    {
        var b = world.Data.PowerBalance;
        var g = world.Government![country.Id];
        var parliament = g.Parliament;
        var head = PoliticsModel.HeadParty(world, country.Id);
        var approval = Stat(country, "approval_pct", 50);
        var defect = Math.Clamp((b.Defect - approval) / b.DefectScale, 0, b.DefectMax);
        var rng = PoliticsModel.Rng(world);
        var against = parliament.OppositionSeats;
        foreach (var f in parliament.Factions.Where(f => f.Ruling && f.Party != head?.Id))
            if (rng.Chance(defect))
                against += f.Seats;
        if (against * 2 <= parliament.Seats)
        {
            world.Log(country.Id, "power", $"{parliament.Name} проголосовал по вотуму недоверия: против правительства {against} из {parliament.Seats} — оно устояло.");
            return false;
        }
        var parties = p.Parties;
        var seats = parties.Select(x => parliament.Factions.FirstOrDefault(f => f.Party == x.Id)?.Seats ?? 0).ToArray();
        var candidates = Enumerable.Range(0, parties.Count).Where(i => parties[i] != head).ToList();
        if (candidates.Count == 0)
            return false;
        var anchor = candidates.OrderByDescending(i => seats[i]).ThenByDescending(i => parties[i].Poll).First();
        var excluded = head == null ? null : new[] { parties.IndexOf(head) };
        var ruling = Coalition.Form(parties, seats, world.Data.PoliticsBalance, anchor, excluded).Select(i => parties[i].Id).ToList();
        var old = g.Leader.Name;
        Cabinet.Form(world, country, ruling, parties[anchor], parties[anchor].Leader);
        Record(world, country, p, "no_confidence", old,
            $"{parliament.Name} выразил недоверие правительству ({against} из {parliament.Seats}). {old} уходит в отставку, новое правительство возглавил " +
            $"{g.Leader.Name} ({Text.Fmt.Quote(parties[anchor].Name)}).");
        return true;
    }

    /// <summary>Импичмент: нужны две трети парламента (оппозиция и перебежчики). Главу сменяет преемник из той же партии.</summary>
    public static bool Impeach(World world, Country country, CountryPolitics p)
    {
        var b = world.Data.PowerBalance;
        var g = world.Government![country.Id];
        var parliament = g.Parliament;
        var approval = Stat(country, "approval_pct", 50);
        var defect = Math.Clamp((b.Defect - approval) / b.DefectScale, 0, b.DefectMax);
        var rng = PoliticsModel.Rng(world);
        var against = parliament.OppositionSeats;
        foreach (var f in parliament.Factions.Where(f => f.Ruling))
            against += (int)Math.Round(f.Seats * (rng.Chance(defect) ? defect : 0));
        if (against * 3 < parliament.Seats * 2)
        {
            world.Log(country.Id, "power", $"Импичмент не прошёл: за отстранение {against} из {parliament.Seats}, нужно две трети.");
            return false;
        }
        var old = g.Leader.Name;
        var head = PoliticsModel.HeadParty(world, country.Id);
        var successor = head != null ? PartyFactory.PartyLeader(world, country, head) : CharacterFactory.Create(world, country, null, leader: true, rng);
        Replace(world, country, p, successor);
        Record(world, country, p, "impeachment", old, $"Импичмент: {parliament.Name.ToLowerInvariant()} отстранил {old} ({against} из {parliament.Seats}). " +
                                                     $"Страну возглавил {successor.Name}" + (head != null ? $" из той же партии." : "."));
        return true;
    }

    /// <summary>Дворцовый переворот: элиты меняют главу на своего человека; партия и режим остаются.</summary>
    public static void PalaceCoup(World world, Country country, CountryPolitics p)
    {
        var g = world.Government![country.Id];
        var old = g.Leader.Name;
        var head = PoliticsModel.HeadParty(world, country.Id);
        var rng = PoliticsModel.Rng(world);
        var insider = head != null ? PartyFactory.PartyLeader(world, country, head) : CharacterFactory.Create(world, country, null, leader: true, rng);
        Replace(world, country, p, insider);
        Appease(world, p, world.Data.PowerBalance.Elites);
        Record(world, country, p, "palace_coup", old, $"Дворцовый переворот: элиты отстранили {old}. Власть взял {insider.Name}.");
    }

    /// <summary>
    /// Военный переворот: армия свергает власть. Военный режим, парламент распущен, выборов нет, собрания запрещены,
    /// правят указами; хунта может пообещать вернуть власть гражданским через несколько лет.
    /// </summary>
    public static void MilitaryCoup(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        var b = data.PowerBalance;
        var g = world.Government![country.Id];
        var rng = PoliticsModel.Rng(world);
        var old = g.Leader;
        p.Junta = new Junta
        {
            System = p.Junta?.System ?? country.System,
            Regime = p.Junta?.Regime ?? country.Regime,
            Parliament = g.Parliament,
            HandOver = rng.Chance(b.HandOverChance) ? FirstOfMonth(world.Date.AddYears(rng.Next(b.HandOverYears[0], b.HandOverYears[1] + 1))) : null,
        };
        country.System = PoliticalSystem.MilitaryRegime;
        country.Regime = Regime.Authoritarian;
        g.Parliament = Parliament.Blocks("none", GovernmentModel.SystemOf(data, country).ParliamentName, 0, 0, 0);
        var general = CharacterFactory.Create(world, country, null, leader: true, rng);
        if (!general.Has("silovik"))
            general.Traits.Add("silovik");
        general.Party = null;
        general.Since = world.Date;
        if (p.Party(old.Party) is { } oldParty)
            oldParty.Leader = old;
        g.Leader = general;
        foreach (var party in p.Parties)
            (party.Ruling, party.RulingSince, party.Power, party.Loyal) = (false, null, false, false);
        foreach (var minister in g.Ministers.Values)
            minister.Party = null;
        (p.NextElection, p.NextPresident, p.Campaign, p.Talks) = (null, null, null, null);
        p.Promises.Clear();
        p.Pledges.Clear();
        SetLaws(world, country, ("emergency_powers", "decree"), ("assembly", "banned"));
        p.Security.ArmyStrain = 0;
        p.Security.Repression = Math.Min(100, p.Security.Repression + b.CoupRepression);
        Appease(world, p, ["security"]);
        g.Version++;
        Record(world, country, p, "military_coup", old.Name,
            $"Военный переворот: армия свергла {old.Name}. Власть у военного совета во главе с {general.Name}. Парламент распущен, собрания запрещены" +
            (p.Junta.HandOver is { } d ? $"; военные обещают вернуть власть гражданским к {Text.Fmt.Date(d)}." : "; сроков передачи власти не называют."));
    }

    /// <summary>Хунта передаёт власть: прежний строй (гибридный режим), парламент восстановлен, выборы через HandOverElection месяцев.</summary>
    public static void HandOver(World world, Country country, CountryPolitics p)
    {
        var junta = p.Junta!;
        var g = world.Government![country.Id];
        country.System = junta.System == PoliticalSystem.MilitaryRegime ? PoliticalSystem.PresidentialRepublic : junta.System;
        country.Regime = Regime.Hybrid;
        Restore(world, country, p, junta.Parliament, ruling: null);
        p.Junta = null;
        Schedule(world, country, p, world.Data.PowerBalance.HandOverElection);
        SetLaws(world, country, ("emergency_powers", "parliament"));
        g.Version++;
        Record(world, country, p, "handover", g.Leader.Name,
            $"Военные возвращают власть гражданским: парламент восстановлен, выборы — {Text.Fmt.Date(p.NextElection ?? world.Date)}.", lost: false);
    }

    /// <summary>
    /// Революция: восстание класса свергает власть. Временное правительство — партия, ближайшая к взглядам восставших
    /// (если партий не было, они появляются); режим становится свободнее, монархия и однопартийность уходят, собрания и
    /// пресса свободны, выборы — через ElectionMonths.
    /// </summary>
    public static void Revolution(World world, Country country, CountryPolitics p, int rebels)
    {
        var data = world.Data;
        var b = data.PowerBalance;
        var g = world.Government![country.Id];
        var s = world.Society![country.Id];
        var old = g.Leader.Name;
        var record = data.PoliticsOf(country.Id);
        var presidential = record.PresidentTerm != null;
        country.System = country.System switch
        {
            PoliticalSystem.MilitaryRegime => p.Junta?.System is { } civil && civil != PoliticalSystem.MilitaryRegime ? civil
                : presidential ? PoliticalSystem.PresidentialRepublic : PoliticalSystem.ParliamentaryRepublic,
            PoliticalSystem.AbsoluteMonarchy => PoliticalSystem.ParliamentaryRepublic,
            PoliticalSystem.OnePartyState => presidential ? PoliticalSystem.PresidentialRepublic : PoliticalSystem.ParliamentaryRepublic,
            var same => same,
        };
        country.Regime = country.Regime == Regime.Authoritarian ? Regime.Hybrid : Regime.Democracy;
        if (p.Parties.Count == 0)
        {
            p.Parties = PartyFactory.Create(world, country, p, PoliticalViews.Mean(p.Views, s), force: true, regime: Regime.Democracy);
            var poll = Polls.Compute(data.PoliticsBalance, country, p, s, g, false, world.Date, 0);
            for (var i = 0; i < p.Parties.Count && i < poll.Shares.Length; i++)
                (p.Parties[i].Poll, p.Parties[i].Voters) = (poll.Shares[i], poll.Voters[i]);
        }
        foreach (var party in p.Parties)
            (party.Power, party.Loyal) = (false, false);
        var view = p.Views[rebels];
        var leaderParty = p.Parties
            .OrderBy(x => x.Ruling ? 1 : 0)
            .ThenBy(x => PoliticalViews.Distance(view, x.Position, data.PoliticsBalance.Salience))
            .First();
        Restore(world, country, p, p.Junta?.Parliament ?? g.Parliament, leaderParty);
        p.Junta = null;
        Cabinet.Form(world, country, [leaderParty.Id], leaderParty, leaderParty.Leader ?? PartyFactory.PartyLeader(world, country, leaderParty));
        Schedule(world, country, p, b.ElectionMonths);
        SetLaws(world, country, ("assembly", "free"), ("press_freedom", "free"), ("emergency_powers", "parliament"));
        p.Protests.Clear();
        for (var k = 0; k < s.Radicalism.Length; k++)
            s.Radicalism[k] = Math.Max(0, s.Radicalism[k] - b.RevolutionCalm);
        (p.Security.PoliceStrain, p.Security.ArmyStrain, p.Security.Repression) = (0, 0, 0);
        (p.Campaign, p.Talks) = (null, null);
        p.Promises.Clear();
        p.Pledges.Clear();
        g.Version++;
        Record(world, country, p, "revolution", old,
            $"Революция: восстание ({data.Classes.Classes[rebels].Name.ToLowerInvariant()}) свергло {old}. Временное правительство возглавил {g.Leader.Name} " +
            $"({Text.Fmt.Quote(leaderParty.Name)}). Выборы — {Text.Fmt.Date(p.NextElection ?? world.Date)}.");
    }

    /// <summary>
    /// Настоящий парламент вместо формального или распущенного: места — как были (или по данным), фракции — по опросам,
    /// правит партия ruling (null — пока никто).
    /// </summary>
    static void Restore(World world, Country country, CountryPolitics p, Parliament previous, Party? ruling)
    {
        var data = world.Data;
        var b = data.GovernmentBalance.Parliament;
        var g = world.Government![country.Id];
        var seats = previous.Seats > 0 ? previous.Seats
            : data.Government.ParliamentSeats.TryGetValue(country.Id, out var known) ? known
            : Math.Clamp((int)Math.Round(Math.Cbrt(Stat(country, "population", 1e6))), b.SeatsMin, b.SeatsMax);
        var shares = p.Parties.Select(x => Math.Max(0.001, x.Poll)).ToArray();
        var allocation = p.Parties.Count > 0 ? Seats.LargestRemainder(shares, seats) : [];
        g.Parliament = new Parliament
        {
            Kind = "real",
            Name = GovernmentModel.SystemOf(data, country).ParliamentName,
            Seats = seats,
            Discipline = country.Regime == Regime.Democracy ? b.DemocracyDiscipline : b.HybridDiscipline,
            Factions = Enumerable.Range(0, p.Parties.Count).Where(i => allocation[i] > 0)
                .OrderByDescending(i => allocation[i])
                .Select(i => new Faction { Party = p.Parties[i].Id, Seats = allocation[i], Ruling = p.Parties[i] == ruling })
                .ToList(),
        };
        foreach (var party in p.Parties)
            (party.Ruling, party.RulingSince) = (party == ruling, party == ruling ? world.Date : null);
    }

    /// <summary>Выборы через months месяцев (президентские — если они в этой стране есть).</summary>
    static void Schedule(World world, Country country, CountryPolitics p, int months)
    {
        var date = FirstOfMonth(world.Date.AddMonths(months));
        p.NextElection = date;
        p.NextPresident = Elections.HasPresident(world, country) ? date : null;
    }

    /// <summary>Новая власть задабривает тех, кто привёл её к власти: прибавка к одобрению групп (как за выполненное требование).</summary>
    static void Appease(World world, CountryPolitics p, IReadOnlyCollection<string> groups)
    {
        foreach (var g in p.Groups.Where(g => groups.Contains(g.Id)))
            g.Memory += world.Data.PowerBalance.CoupAppeasement;
    }

    /// <summary>Сменить главу на new (партия та же), министры остаются.</summary>
    static void Replace(World world, Country country, CountryPolitics p, Character newHead)
    {
        var g = world.Government![country.Id];
        newHead.Party = g.Leader.Party;
        newHead.Since = world.Date;
        g.Leader = newHead;
        g.Version++;
    }

    /// <summary>Ввести варианты законов сразу (без парламента и переходного периода), если такие есть.</summary>
    static void SetLaws(World world, Country country, params (string Law, string Variant)[] changes)
    {
        if (world.Laws?.Countries.GetValueOrDefault(country.Id) is not { } laws)
            return;
        foreach (var (law, variant) in changes)
            if (laws.Current.ContainsKey(law) && world.Data.Law(law).Variants.Any(v => v.Id == variant))
            {
                laws.Current[law] = variant;
                laws.Transitions.RemoveAll(t => t.Law == law);
                laws.Bills.RemoveAll(x => x.Law == law);
            }
        Laws.LawModel.Recompute(world.Data, laws, world.Date);
        laws.Version++;
    }

    /// <summary>Запомнить смену власти, записать в журнал; если власть потерял игрок — спросить, что дальше.</summary>
    static void Record(World world, Country country, CountryPolitics p, string kind, string oldLeader, string text, bool lost = true)
    {
        var g = world.Government![country.Id];
        var party = p.Party(g.Leader.Party);
        p.Changes.Add(new PowerChange(world.Date, kind, oldLeader, g.Leader.Name, party?.Name));
        p.Formed = world.Date;
        p.Version++;
        world.Log(country.Id, "power", text);
        // Cabinet.Form уже мог записать поражение «на выборах» — здесь указывается настоящая причина.
        if (lost && world.PlayerCountry == country.Id)
            world.Defeat = new Defeat(world.Date, country.Id, oldLeader, g.Leader.Name, party?.Name ?? "", kind);
    }

    static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
}
