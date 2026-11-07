using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Military;

/// <summary>
/// Армии и войны за регионы. Сила армии = солдаты^a × оснащение^b × (1 + военные технологии) × боевой дух. Война идёт
/// помесячно по регионам на линии соприкосновения (<see cref="WarFront"/>); взятый регион переходит под контроль
/// наступающих. Цена: потери, разрушения, расходы, сначала сплочение, потом усталость. Заканчивается перемирием по
/// линии фронта (мирные договоры — этап 6.5). MODEL.md, «Армии и войны».
/// </summary>
public static class WarModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.WarSystem()];

    public static void Setup(World world)
    {
        var w = world.War = new WorldWar();
        Ensure(world);
        foreach (var c in world.Data.Military.Ceasefires.Where(c => world.ById(c.A) is not null && world.ById(c.B) is not null))
            w.Ceasefires.Add(new Ceasefire(c.A, c.B, DateOnly.Parse(c.Since)));
        UpdateArmies(world, monthly: false);
        Publish(world);
    }

    /// <summary>Армии для стран, у которых их нет, и то, что не сохраняется: карта, земли на старте, сила.</summary>
    public static void Ensure(World world)
    {
        var w = world.War ??= new WorldWar();
        var data = world.Data;
        var b = data.WarBalance;
        if (ReferenceEquals(w.Map, WarMap.Empty))
            w.Map = WarMap.Build(data);
        var overrides = data.Military.Armies.ToDictionary(a => a.Country, a => a.Soldiers, StringComparer.Ordinal);
        foreach (var country in world.Countries)
        {
            if (w.Armies.ContainsKey(country.Id))
                continue;
            var soldiers = overrides.TryGetValue(country.Id, out var s) ? s
                : country.Info.Stats.GetValueOrDefault("armed_forces")?.Value ?? (country.Info.Stats.GetValueOrDefault("population")?.Value ?? 1e6) * 0.004;
            w.Armies[country.Id] = new Army
            {
                Soldiers = soldiers,
                Base = soldiers,
                Equipment = DefenseUsd(world, country.Id) * b.EquipmentYears,
                Morale = b.Morale,
                Mood = new double[ClassIds.Count],
            };
        }
        if (w.StartTerritory.Count == 0)
        {
            var start = data.Control.Control.ToDictionary(c => c.Region, c => c.Controller, StringComparer.Ordinal);
            foreach (var country in world.Countries)
                w.StartTerritory[country.Id] = Territory(w.Map, b, country.Id, r => start.GetValueOrDefault(r.Id, r.Country));
        }
        if (w.Strength.Count == 0)
            foreach (var country in world.Countries)
                w.Strength[country.Id] = ComputeStrength(world, country.Id);
    }

    /// <summary>Военные расходы страны за год, долл.</summary>
    static double DefenseUsd(World world, string id)
    {
        var e = world.Economy![id];
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == "defense");
        if (k < 0 || e.ExchangeRate <= 0)
            return 0;
        var nominal = k < e.Spending.Length && e.Spending[k] > 0 ? e.Spending[k] : e.SpendingPlan[k] * e.NominalGdp;
        return Math.Max(0, nominal) / e.ExchangeRate;
    }

    /// <summary>Сила армии (относительная величина: сравнивается только с другими армиями).</summary>
    public static double Strength(World world, string country) =>
        world.War?.Strength.GetValueOrDefault(country) ?? 0;

    static double ComputeStrength(World world, string id)
    {
        var b = world.Data.WarBalance;
        var a = world.War!.Armies[id];
        var tech = Math.Max(0.1, 1 + LawModel.Effect(world, id, "military_power"));
        return Math.Pow(Math.Max(1, a.Soldiers), b.SoldiersExponent) * Math.Pow(Math.Max(1, a.Equipment / 1e6), b.EquipmentExponent)
               * tech * Math.Max(0.1, a.Morale) / 70;
    }

    /// <summary>Доля выпуска от земель: свои под чужим контролем теряются (OccupiedLoss), чужие под нашим — прибавляют (OccupationGain).</summary>
    static double Territory(WarMap map, Data.WarBalance b, string country, Func<Data.RegionRecord, string> controller)
    {
        var total = map.CountryPopulation.GetValueOrDefault(country);
        if (total <= 0)
            return 1;
        double lost = 0, gained = 0;
        foreach (var r in map.Regions.Values)
        {
            var c = controller(r);
            if (r.Country == country && c != country)
                lost += r.Population;
            else if (r.Country != country && c == country)
                gained += r.Population;
        }
        return Math.Max(0.05, 1 - b.OccupiedLoss * lost / total + b.OccupationGain * gained / total);
    }

    public static bool AtWar(World world, string country) => world.War?.Wars.Any(w => w.Involves(country)) == true;

    public static War? WarBetween(World world, string a, string b) =>
        world.War?.Wars.FirstOrDefault(w => w.Side(a) != 0 && w.Side(b) != 0 && w.Side(a) != w.Side(b));

    public static Ceasefire? CeasefireBetween(World world, string a, string b) =>
        world.War?.Ceasefires.FirstOrDefault(c => c.Between(a, b));

    /// <summary>Регионы, на которые страна претендует и которые контролирует другая.</summary>
    public static IEnumerable<string> Claims(World world, string claimant, string holder) =>
        world.Data.Military.Claims.Where(c => c.Claimant == claimant).SelectMany(c => c.Regions)
            .Where(r => world.RegionById(r)?.Controller == holder).Distinct();

    /// <summary>Соприкасаются ли земли стран (по суше или через пролив) — есть ли где воевать.</summary>
    public static bool Touch(World world, string a, string b)
    {
        var map = world.War!.Map;
        foreach (var r in world.Regions)
        {
            if (r.Controller != a)
                continue;
            foreach (var (n, _) in map.Neighbors(r.Id))
                if (world.RegionById(n)?.Controller == b)
                    return true;
        }
        return false;
    }

    /// <summary>Кто вступится за страну: члены её военных блоков и союзники по договору (кроме союзников нападающего).</summary>
    public static List<string> Defenders(World world, string attacker, string defender)
    {
        var allies = new List<string> { defender };
        foreach (var bloc in DiplomacyModel.BlocsOf(world, defender).Where(x => x.Military))
        {
            if (DiplomacyModel.InBloc(world, bloc.Id, attacker))
                continue;
            allies.AddRange(DiplomacyModel.Members(world, bloc.Id).Where(m => m != defender && m != attacker));
        }
        allies.AddRange(world.Diplomacy!.Treaties.Where(t => t.Type == "alliance" && t.Involves(defender)).Select(t => t.Other(defender))
            .Where(x => x != attacker && !DiplomacyModel.Allies(world, x, attacker)));
        return allies.Distinct().ToList();
    }

    /// <summary>Можно ли объявить войну и почему нет.</summary>
    public static (bool Allowed, string Reason) CanDeclare(World world, string attacker, string defender)
    {
        if (attacker == defender || world.ById(defender) is null)
            return (false, "Нужна другая страна.");
        if (WarBetween(world, attacker, defender) != null)
            return (false, "Война уже идёт.");
        if (DiplomacyModel.Allies(world, attacker, defender))
            return (false, "Это наш союзник.");
        if (!Touch(world, attacker, defender))
            return (false, "Наши земли не соприкасаются ни по суше, ни через пролив — войскам не дойти.");
        return (true, "");
    }

    /// <summary>
    /// Объявить войну: за жертву вступаются её союзники; агрессор портит отношения со всеми (со спором за регионы —
    /// вполовину), с жертвой — вдвое; перемирие между ними, если было, разорвано. Цель — спорные регионы, если они есть.
    /// </summary>
    public static War? Declare(World world, string attacker, string defender)
    {
        if (!CanDeclare(world, attacker, defender).Allowed)
            return null;
        var w = world.War!;
        var goal = Claims(world, attacker, defender).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var war = new War
        {
            Id = w.NextId++,
            Attackers = [attacker],
            Defenders = Defenders(world, attacker, defender),
            Goal = goal,
            Start = world.Date,
        };
        w.Wars.Add(war);
        var truce = CeasefireBetween(world, attacker, defender);
        if (truce != null)
            w.Ceasefires.Remove(truce);
        DiplomacyModel.Aggression(world, attacker, war.Defenders, goal.Count > 0 || truce != null);
        var allies = war.Defenders.Skip(1).ToList();
        var text = $"Война: {Name(world, attacker)} нападает на страну «{world[defender].Name}»" +
                   (goal.Count > 0 ? $" из-за спорных регионов ({string.Join(", ", goal.Select(r => RegionName(world, r)))})" : "") +
                   (truce != null ? ", нарушив перемирие" : "") + "." +
                   (allies.Count > 0 ? $" За неё вступились союзники: {string.Join(", ", allies.Select(a => world[a].Name))}." : "");
        News(world, war, text);
        return war;
    }

    /// <summary>Объявить войну (игрок).</summary>
    public static bool DeclareByPlayer(World world, string attacker, string defender) =>
        world.PlayerCountry == attacker && Declare(world, attacker, defender) != null;

    /// <summary>Согласится ли сторона на перемирие: устала, или противник заметно сильнее, или её цель уже взята.</summary>
    public static bool Accepts(World world, War war, string country)
    {
        var b = world.Data.WarBalance;
        var side = war.Side(country);
        var (own, other) = side == 1 ? (SideStrength(world, war, 1), SideStrength(world, war, 2)) : (SideStrength(world, war, 2), SideStrength(world, war, 1));
        return world.War!.Armies[country].Exhaustion > b.AcceptExhaustion || other > own * b.AcceptRatio
               || (side == 1 && GoalTaken(world, war));
    }

    public static bool GoalTaken(World world, War war) =>
        war.Goal.Count > 0 && war.Goal.All(r => war.Side(world.RegionById(r)?.Controller) == 1);

    /// <summary>Сила стороны в войне: главные — CommitMain своей силы, союзники — CommitAlly (делится на все войны страны).</summary>
    public static double SideStrength(World world, War war, int side)
    {
        var b = world.Data.WarBalance;
        var members = side == 1 ? war.Attackers : war.Defenders;
        return members.Sum(c => Strength(world, c) * (war.IsMain(c) ? b.CommitMain : b.CommitAlly) / Math.Max(1, world.War!.Wars.Count(x => x.Involves(c))));
    }

    /// <summary>Предложить перемирие (игрок): другая главная сторона соглашается, если ей это выгодно; на встречное предложение — согласие сразу.</summary>
    public static bool ProposeCeasefire(World world, string country, int warId)
    {
        if (world.PlayerCountry != country || world.War!.Wars.FirstOrDefault(w => w.Id == warId) is not { } war || !war.Involves(country))
            return false;
        var other = war.Side(country) == 1 ? war.Defender : war.Attacker;
        if (war.Offer == other || Accepts(world, war, other))
        {
            End(world, war, $"Перемирие: {world[war.Attacker].Name} и {world[war.Defender].Name} прекратили огонь по линии фронта.");
            return true;
        }
        News(world, war, $"Перемирие отвергнуто: страна «{Name(world, other)}» ещё не устала от войны и не уступает в силе.");
        return false;
    }

    /// <summary>Закончить войну перемирием по линии фронта: занятые регионы остаются у тех, кто их держит.</summary>
    public static void End(World world, War war, string text)
    {
        var w = world.War!;
        w.Wars.Remove(war);
        var old = CeasefireBetween(world, war.Attacker, war.Defender);
        if (old != null)
            w.Ceasefires.Remove(old);
        w.Ceasefires.Add(new Ceasefire(war.Attacker, war.Defender, world.Date));
        var regions = war.News.Count(n => n.Text.StartsWith("Взят", StringComparison.Ordinal) || n.Text.StartsWith("Освобождён", StringComparison.Ordinal));
        w.History.Add(new WarRecord(war.Id, war.Attacker, war.Defender, war.Start, world.Date, text, war.AttackerLosses, war.DefenderLosses, regions));
        News(world, war, text);
    }

    /// <summary>Новость войны: в её ленту и в журнал игрока, если он воюет.</summary>
    public static void News(World world, War war, string text)
    {
        war.News.Add(new WarNews(world.Date, text));
        if (war.News.Count > 30)
            war.News.RemoveAt(0);
        if (world.PlayerCountry is { } player && war.Involves(player))
            world.Log(player, "war", text);
    }

    static string Name(World world, string id) => world[id].Name;

    public static string RegionName(World world, string id) =>
        world.War?.Map.Regions.GetValueOrDefault(id)?.Name ?? world.RegionById(id)?.Info.Name ?? id;

    /// <summary>
    /// Армии за месяц: оснащение растёт на долю военных расходов и изнашивается; на войне армия мобилизуется, в мире
    /// возвращается к мирной численности; усталость в мире спадает; боевой дух — от усталости; разрушения
    /// восстанавливаются. Потом — сила.
    /// </summary>
    public static void UpdateArmies(World world, bool monthly = true)
    {
        var w = world.War!;
        var b = world.Data.WarBalance;
        foreach (var country in world.Countries)
        {
            var a = w.Armies[country.Id];
            if (monthly)
            {
                a.Equipment = Math.Max(0, a.Equipment + DefenseUsd(world, country.Id) * b.Procurement / 12 - a.Equipment * b.Depreciation / 12);
                var atWar = AtWar(world, country.Id);
                var target = a.Base * (atWar ? 1 + b.Mobilization : 1);
                a.Soldiers += (target - a.Soldiers) / (atWar ? b.MobilizationMonths : 2 * b.MobilizationMonths);
                if (!atWar)
                    a.Exhaustion = Math.Max(0, a.Exhaustion - b.ExhaustionPeace);
                a.Damage *= 1 - b.Recovery / 12;
            }
            a.Morale = Math.Max(10, b.Morale - b.MoraleExhaustion * a.Exhaustion);
        }
        foreach (var country in world.Countries)
            w.Strength[country.Id] = ComputeStrength(world, country.Id);
    }

    /// <summary>
    /// Поправки к модели и настроение: на войне — оборона дороже, рост ниже, ставки выше, сначала сплочение вокруг
    /// флага, потом усталость; множитель выпуска — от занятых и взятых земель (в сравнении со стартом) и разрушений.
    /// </summary>
    public static void Apply(World world)
    {
        var w = world.War!;
        var b = world.Data.WarBalance;
        var controllers = world.Regions.ToDictionary(r => r.Id, r => r.Controller, StringComparer.Ordinal);
        foreach (var country in world.Countries)
        {
            var a = w.Armies[country.Id];
            a.Effects.Clear();
            var war = w.Wars.Where(x => x.Involves(country.Id)).OrderBy(x => x.IsMain(country.Id) ? 0 : 1).FirstOrDefault();
            if (war != null)
            {
                var burden = war.IsMain(country.Id) ? 1 : b.AllyBurden;
                a.Effects["spending.defense"] = b.WarSpending * burden;
                a.Effects["potential_growth"] = b.WarGrowth * burden;
                a.Effects["spread"] = b.WarSpread * burden;
                var months = Math.Max(0, (world.Date.Year - war.Start.Year) * 12 + world.Date.Month - war.Start.Month);
                a.Effects["legitimacy"] = b.Rally * burden * Math.Exp(-(double)months / b.RallyMonths);
            }
            if (a.Exhaustion > 0)
                a.Effects["legitimacy"] = a.Effects.GetValueOrDefault("legitimacy") - b.ExhaustionLegitimacy * a.Exhaustion;
            if (a.Mood.Length != ClassIds.Count)
                a.Mood = new double[ClassIds.Count];
            Array.Fill(a.Mood, -b.ExhaustionMood * a.Exhaustion);
            var territory = Territory(w.Map, b, country.Id, r => controllers.GetValueOrDefault(r.Id, r.Country));
            a.Output = territory / Math.Max(0.05, w.StartTerritory.GetValueOrDefault(country.Id, 1)) * (1 - a.Damage);
        }
    }

    /// <summary>Множитель выпуска страны от войны (занятые земли, разрушения) — для производства.</summary>
    public static double Output(World world, string country) => world.War?.Armies.GetValueOrDefault(country)?.Output ?? 1;

    /// <summary>Показатели: сила армии (у сильнейшей 100, с разбором), солдаты, усталость от войны.</summary>
    public static void Publish(World world)
    {
        var w = world.War!;
        var year = world.Date.Year;
        var top = w.Strength.Values.DefaultIfEmpty(1).Max();
        foreach (var country in world.Countries)
        {
            var a = w.Armies[country.Id];
            var strength = w.Strength.GetValueOrDefault(country.Id);
            country.Stats["military_strength"] = new StatValue(top > 0 ? 100 * Math.Sqrt(strength / top) : 0, year, StatSources.Model, null, null,
            [
                new StatFactor("Солдаты", a.Soldiers, "people"),
                new StatFactor("Оснащение, млрд долл.", a.Equipment / 1e9, "number1"),
                new StatFactor("Военные технологии", LawModel.Effect(world, country.Id, "military_power") * 100, "number0"),
                new StatFactor("Боевой дух", a.Morale, "number0"),
            ]);
            country.Stats["soldiers"] = new StatValue(a.Soldiers, year, StatSources.Model, null, null, null);
            country.Stats["war_exhaustion"] = new StatValue(a.Exhaustion, year, StatSources.Model, null, null, null);
        }
    }

    /// <summary>Погибшие: солдаты — мужчины 20–44 лет, мирные жители — все поровну.</summary>
    public static void Kill(World world, string country, double soldiers, double civilians)
    {
        if (world.Demography?.Countries.GetValueOrDefault(country) is not { } d)
            return;
        if (soldiers > 0)
        {
            var total = 0.0;
            for (var g = 4; g <= 8; g++)
                total += d.Male[g];
            var keep = total > 0 ? Math.Max(0, 1 - soldiers / total) : 1;
            for (var g = 4; g <= 8; g++)
                d.Male[g] *= keep;
        }
        if (civilians > 0 && d.Total > 0)
        {
            var keep = Math.Max(0, 1 - civilians / d.Total);
            for (var g = 0; g < d.Male.Length; g++)
            {
                d.Male[g] *= keep;
                d.Female[g] *= keep;
            }
        }
    }

    public static string Plural(double people) => Fmt.People(people);
}
