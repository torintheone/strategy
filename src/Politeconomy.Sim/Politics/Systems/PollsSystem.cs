using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>
/// Ежемесячный опрос: доли партий догоняют то, что дают нынешние взгляды и настроение классов, публикуется опрос
/// с небольшой погрешностью. Перед опросом у каждой партии сдвигается случайный дрейф привлекательности. Показатель ruling_poll_pct — сумма правящих партий. MODEL.md, «Опросы».
/// </summary>
public sealed class PollsSystem : ISimSystem
{
    public string Name => "Опросы";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        foreach (var country in world.Countries)
        {
            var p = politics[country.Id];
            if (p.Parties.Count > 0)
                Update(world, country, p, world.Society![country.Id], world.Government![country.Id], months: 1);
        }
    }

    /// <summary>Один месяц опросов; при months = 0 (старт) доли сразу равны расчётным.</summary>
    public static void Update(World world, Country country, CountryPolitics p, CountrySociety s, CountryGovernment g, int months)
    {
        var b = world.Data.PoliticsBalance;
        if (months > 0)
            foreach (var party in p.Parties)
                party.Momentum += -b.MomentumReversion * party.Momentum + PoliticsModel.Rng(world).NextGaussian() * b.MomentumSpread;
        var result = Polls.Compute(b, country, p, s, g, PoliticsModel.Majoritarian(world, country), world.Date,
            Polls.EconomicVote(b, world.Economy![country.Id]));
        var shares = new Dictionary<string, double>(StringComparer.Ordinal);
        var published = new double[p.Parties.Count];
        for (var i = 0; i < p.Parties.Count; i++)
        {
            var party = p.Parties[i];
            party.Poll = months == 0 ? result.Shares[i] : party.Poll + (result.Shares[i] - party.Poll) / b.PollMonths;
            party.Voters = result.Voters[i];
            published[i] = Math.Max(0.001, party.Poll + PoliticsModel.Rng(world).NextGaussian() * b.PollNoise / 100);
        }
        var total = published.Sum();
        for (var i = 0; i < p.Parties.Count; i++)
            shares[p.Parties[i].Id] = Math.Round(published[i] / total, 4);
        p.Polls.Add(new PollRecord(world.Date, shares));
        if (p.Polls.Count > b.PollHistory)
            p.Polls.RemoveRange(0, p.Polls.Count - b.PollHistory);
        p.Version++;

        var ruling = p.Parties.Where(x => x.Ruling).Sum(x => shares[x.Id]) * 100;
        country.Stats["ruling_poll_pct"] = new StatValue(ruling, world.Date.Year, StatSources.Model, null, null,
            p.Parties.Where(x => x.Ruling).Select(x => new StatFactor(x.Name, shares[x.Id] * 100, "percent")).ToArray());
    }
}
