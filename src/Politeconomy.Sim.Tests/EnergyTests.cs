using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Tests;

public class EnergyTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static int Plant(string id) => TestData.Game.Energy.Plants.ToList().FindIndex(p => p.Id == id);

    static void Law(Simulation sim, string country, string law, string variant)
    {
        var laws = sim.World.Laws![country];
        laws.Current[law] = variant;
        Laws.LawModel.Recompute(sim.World.Data, laws, sim.World.Date);
    }

    [Fact]
    public void StartMatchesDataWithoutBlackouts()
    {
        var world = NewGame().World;
        var fra = world.Energy!["FRA"];
        Assert.InRange(fra.Generation[Plant("nuclear")] / fra.Generation.Sum(), 0.6, 0.75);
        Assert.True(world.Energy!["ZAF"].Generation[Plant("coal")] / world.Energy!["ZAF"].Generation.Sum() > 0.7);
        Assert.All(world.Countries, c =>
        {
            var e = world.Energy![c.Id];
            Assert.Equal(0, e.Shortage, 9);
            Assert.True(c.Stat("power_reserve_pct") > 0, c.Id);
            Assert.Equal(100, c.Stat("electricity_price_index"), 6);
        });
    }

    [Fact]
    public void BlackoutsCutOutputAndAngerPeopleUntilNewPlantsAreBuilt()
    {
        var hit = NewGame();
        var same = NewGame();
        const string id = "ZAF";
        // Половина станций выходит из строя.
        var e = hit.World.Energy![id];
        for (var t = 0; t < e.Capacity.Length; t++)
            e.Capacity[t] *= 0.5;
        hit.RunUntil(hit.World.Date.AddMonths(3));
        same.RunUntil(same.World.Date.AddMonths(3));
        Assert.True(e.Shortage > 0.2, $"нехватка {e.Shortage:P1}");
        var (a, b) = (hit.World.Economy![id], same.World.Economy![id]);
        Assert.True(a.RealGdp < b.RealGdp * 0.9, $"ВВП {a.RealGdp / b.RealGdp:P1} от обычного");
        var metallurgy = TestData.Game.Industries.Industries.ToList().FindIndex(i => i.Id == "metallurgy");
        var finance = TestData.Game.Industries.Industries.ToList().FindIndex(i => i.Id == "finance");
        Assert.True(e.Cut[metallurgy] > e.Cut[finance], "металлургии отключения стоят дороже, чем банкам");
        Assert.True(e.Mood.All(m => m < -5));
        Assert.True(hit.World.Society![id].Average(hit.World.Society![id].Satisfaction) < same.World.Society![id].Average(same.World.Society![id].Satisfaction));
        // Энергетики строят новые станции: через несколько лет отключений меньше.
        var worst = e.Shortage;
        hit.RunUntil(hit.World.Date.AddYears(6));
        Assert.True(e.Shortage < worst / 2, $"нехватка через 6 лет {e.Shortage:P1}");
    }

    [Fact]
    public void DroughtEmptiesHydroCountries()
    {
        var sim = NewGame();
        var e = sim.World.Energy!["ZMB"];
        Assert.Equal(0, e.Shortage, 9);
        e.Hydro = 0.7;
        sim.RunUntil(sim.World.Date.AddMonths(2));
        Assert.True(e.Shortage > 0.03, $"нехватка {e.Shortage:P1}");
    }

    [Fact]
    public void UnderfundedPowerSectorLosesItsReserve()
    {
        var poor = NewGame();
        var same = NewGame();
        const string id = "IND";
        Law(poor, id, "electricity_tariffs", "subsidized");
        var item = TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == "energy");
        poor.World.Economy![id].SpendingPlan[item] *= 0.3;
        poor.RunUntil(poor.World.Date.AddYears(10));
        same.RunUntil(same.World.Date.AddYears(10));
        var (a, b) = (poor.World.Energy![id], same.World.Energy![id]);
        Assert.True(a.Funding < 0.75, $"деньги {a.Funding:F2}");
        Assert.True(poor.World[id].Stat("power_reserve_pct") < same.World[id].Stat("power_reserve_pct") - 8,
            $"запас {poor.World[id].Stat("power_reserve_pct"):F1}% против {same.World[id].Stat("power_reserve_pct"):F1}%, нехватка {a.Shortage:P1} против {b.Shortage:P1}");
        // Запаса нет — и первый же засушливый год оборачивается отключениями.
        poor.RunUntil(poor.World.Date.AddMonths(1));
        same.RunUntil(same.World.Date.AddMonths(1));
        (a.Hydro, b.Hydro) = (0.7, 0.7);
        (a.HydroYear, b.HydroYear) = (poor.World.Date.Year, same.World.Date.Year);
        poor.RunUntil(poor.World.Date.AddMonths(1));
        same.RunUntil(same.World.Date.AddMonths(1));
        Assert.Equal(0, b.Shortage, 9);
        Assert.True(a.Shortage > 0.002,
            $"запас {poor.World[id].Stat("power_reserve_pct"):F1}% против {same.World[id].Stat("power_reserve_pct"):F1}%, нехватка {a.Shortage:P1} против {b.Shortage:P1}, деньги {a.Funding:F2}");
    }

    [Fact]
    public void NuclearStrategyBuildsReactors()
    {
        var sim = NewGame();
        var same = NewGame();
        const string id = "POL";
        Assert.Equal(0, sim.World.Energy![id].Capacity[Plant("nuclear")], 9);
        Law(sim, id, "energy_strategy", "nuclear");
        sim.RunUntil(sim.World.Date.AddYears(15));
        same.RunUntil(same.World.Date.AddYears(15));
        var (a, b) = (sim.World.Energy![id], same.World.Energy![id]);
        Assert.True(a.Capacity[Plant("nuclear")] > 2 * b.Capacity[Plant("nuclear")] + 0.5,
            $"АЭС {a.Capacity[Plant("nuclear")]:F1} ГВт против {b.Capacity[Plant("nuclear")]:F1}");
        Assert.True(sim.World[id].Stat("low_carbon_power_pct") > same.World[id].Stat("low_carbon_power_pct"));
    }

    [Fact]
    public void CarbonTaxPushesCoalOutAndCleansTheAir()
    {
        var sim = NewGame();
        var same = NewGame();
        const string id = "IND";
        Law(sim, id, "carbon_tax", "high");
        sim.RunUntil(sim.World.Date.AddYears(15));
        same.RunUntil(same.World.Date.AddYears(15));
        Assert.True(sim.World[id].Stat("power_coal_pct") < same.World[id].Stat("power_coal_pct") - 5);
        Assert.True(sim.World.Energy![id].Effects["pollution"] < same.World.Energy![id].Effects["pollution"]);
        Assert.True(sim.World.Living![id].Pollution < same.World.Living![id].Pollution);
        // Налог дорожает электричество — это бьёт по тем, кто платит по тарифам.
        Assert.True(sim.World.Energy![id].Mood[ClassIds.Unskilled] < same.World.Energy![id].Mood[ClassIds.Unskilled]);
    }
}
