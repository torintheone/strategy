using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Military;

/// <summary>
/// Месяц войны. Фронт — регионы одной стороны, соседние (по суше или через пролив) с регионами другой. Каждая сторона
/// делит силу поровну между регионами противника, на которые наступает, и между своими регионами, которые обороняет.
/// Перевес = сила наступления / (сила обороны × оборона региона: горы, плотность населения, столица, своя земля);
/// через пролив наступать труднее. Прогресс к взятию растёт на Advance × (перевес − 1) с поправкой на площадь, без
/// перевеса — откатывается. Взятый регион переходит под контроль наступающих (свой — возвращается владельцу). Потери
/// солдат и оснащения, мирные жители и разрушения в регионах фронта, усталость. Потом — капитуляция, тупик или
/// перемирие. MODEL.md, «Армии и войны».
/// </summary>
public static class WarFront
{
    public static void Month(World world, War war)
    {
        var w = world.War!;
        var b = world.Data.WarBalance;
        var map = w.Map;
        var strength = new[] { 0.0, WarModel.SideStrength(world, war, 1), WarModel.SideStrength(world, war, 2) };

        // Кто на что может наступать: регион стороны s, у которого есть сосед стороны 3 − s.
        var targets = new[] { new Dictionary<string, bool>(), new Dictionary<string, bool>(), new Dictionary<string, bool>() };
        foreach (var r in world.Regions)
        {
            var s = war.Side(r.Controller);
            if (s == 0)
                continue;
            foreach (var (n, land) in map.Neighbors(r.Id))
            {
                if (war.Side(world.RegionById(n)?.Controller) != 3 - s)
                    continue;
                var attackers = targets[3 - s];
                attackers[r.Id] = attackers.GetValueOrDefault(r.Id) || land;
            }
        }
        var front = new HashSet<string>(targets[1].Keys.Concat(targets[2].Keys), StringComparer.Ordinal);
        foreach (var gone in war.Front.Keys.Where(k => !front.Contains(k)).ToList())
            war.Front.Remove(gone);

        // Бои: регион, на который наступает сторона t (его держит сторона 3 − t).
        var captured = new List<(string Region, int By)>();
        var advancing = new bool[3];
        for (var t = 1; t <= 2; t++)
        {
            var defenders = 3 - t;
            if (targets[t].Count == 0 || strength[t] <= 0)
                continue;
            // Наступающие сосредоточивают силы на цели войны (нападающие — на спорных регионах), обороняющиеся — поровну.
            var goal = t == 1 ? war.Goal.ToHashSet(StringComparer.Ordinal) : [];
            double Weight(string region) => goal.Contains(region) ? b.Focus : 1;
            var weights = targets[t].Keys.Sum(Weight);
            var defense = strength[defenders] / targets[t].Count;
            foreach (var (region, land) in targets[t].OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var r = world.RegionById(region)!;
                var attack = strength[t] * Weight(region) / weights;
                var ratio = Math.Min(4, attack * (land ? 1 : b.SeaPenalty) / Math.Max(1e-9, defense * Defense(world, r, defenders, war)));
                if (ratio > 1)
                    advancing[t] = true;
                var progress = war.Front.GetValueOrDefault(region);
                if (ratio > 1)
                    progress += b.Advance * (ratio - 1) * Math.Pow(b.AreaRef / Math.Max(100, r.Info.AreaKm2), b.AreaExponent);
                else
                    progress = Math.Max(0, progress - b.Retreat);
                if (progress >= 100)
                {
                    captured.Add((region, t));
                    progress = 0;
                }
                if (progress > 0)
                    war.Front[region] = progress;
                else
                    war.Front.Remove(region);
            }
        }
        foreach (var (region, by) in captured)
            Capture(world, war, region, by);

        Losses(world, war, strength, front, advancing);
        Finish(world, war);
    }

    /// <summary>Оборона региона: окопы + горы + плотность населения + столица + своя земля.</summary>
    static double Defense(World world, Region r, int side, War war)
    {
        var b = world.Data.WarBalance;
        var map = world.War!.Map;
        var density = r.Info.AreaKm2 > 0 ? r.Info.Population / r.Info.AreaKm2 : 0;
        var capital = map.Capitals.GetValueOrDefault(r.Owner) == r.Id;
        var home = war.Side(r.Owner) == side;
        return b.Entrenchment + b.Mountains * map.Mountains.GetValueOrDefault(r.Id) + b.City * Math.Log10(1 + density)
               + (capital ? b.Capital : 0) + (home ? b.Home : 0);
    }

    /// <summary>Регион взят: своя земля возвращается владельцу, чужая — самой сильной стране стороны из соседних.</summary>
    static void Capture(World world, War war, string region, int by)
    {
        var r = world.RegionById(region)!;
        var before = r.Controller;
        if (war.Side(before) == by)
            return;
        string controller;
        if (war.Side(r.Owner) == by)
            controller = r.Owner;
        else
        {
            var map = world.War!.Map;
            var neighbors = map.Neighbors(region).Select(n => world.RegionById(n.Id)?.Controller).Where(c => war.Side(c) == by).Distinct().ToList();
            controller = neighbors.Count > 0
                ? neighbors.OrderByDescending(c => war.IsMain(c!) ? 1 : 0).ThenByDescending(c => WarModel.Strength(world, c!)).ThenBy(c => c, StringComparer.Ordinal).First()!
                : (by == 1 ? war.Attacker : war.Defender);
        }
        world.TransferControl(region, controller);
        var name = WarModel.RegionName(world, region);
        WarModel.News(world, war, controller == r.Owner
            ? $"Освобождён регион «{name}»: он снова под контролем страны «{world[controller].Name}»."
            : $"Взят регион «{name}» ({world[r.Owner].Name}): теперь его контролирует страна «{world[controller].Name}».");
    }

    /// <summary>Потери: солдаты и оснащение воюющих, мирные жители и разрушения в регионах фронта, усталость.</summary>
    static void Losses(World world, War war, double[] strength, HashSet<string> front, bool[] advancing)
    {
        var w = world.War!;
        var b = world.Data.WarBalance;
        for (var s = 1; s <= 2; s++)
        {
            var members = s == 1 ? war.Attackers : war.Defenders;
            var intensity = Math.Clamp(Math.Sqrt(strength[3 - s] / Math.Max(1e-9, strength[s])), 0.5, 2) * (advancing[s] ? b.Assault : 1);
            if (front.Count == 0)
                intensity = 0;
            foreach (var c in members)
            {
                var a = w.Armies[c];
                var commit = (war.IsMain(c) ? b.CommitMain : b.CommitAlly) / Math.Max(1, w.Wars.Count(x => x.Involves(c)));
                var dead = a.Soldiers * commit * b.SoldierLoss * intensity;
                a.Soldiers = Math.Max(0, a.Soldiers - dead);
                a.Equipment *= 1 - commit * b.EquipmentLoss * intensity;
                a.Losses += dead;
                if (s == 1)
                    war.AttackerLosses += dead;
                else
                    war.DefenderLosses += dead;
                WarModel.Kill(world, c, dead, 0);
                var population = Math.Max(1, world.Economy![c].Population);
                var occupied = OccupiedShare(world, c);
                a.Exhaustion = Math.Clamp(a.Exhaustion + (war.IsMain(c) ? b.Exhaustion : b.Exhaustion * b.AllyBurden)
                                          + b.ExhaustionCasualties * dead / population * 1e4 + b.ExhaustionOccupied * occupied / 12, 0, 100);
            }
        }
        foreach (var region in front)
        {
            var r = world.RegionById(region)!;
            WarModel.Kill(world, r.Owner, 0, r.Info.Population * b.CivilianLoss);
            if (w.Armies.TryGetValue(r.Owner, out var owner))
                owner.Damage = Math.Min(0.5, owner.Damage + b.Damage * w.Map.Share(region, r.Owner));
        }
    }

    /// <summary>Доля населения страны (по данным), чьи регионы контролирует кто-то другой.</summary>
    public static double OccupiedShare(World world, string country)
    {
        var map = world.War!.Map;
        var total = map.CountryPopulation.GetValueOrDefault(country);
        if (total <= 0)
            return 0;
        return world.Regions.Where(r => r.Owner == country && r.Controller != country).Sum(r => r.Info.Population) / total;
    }

    /// <summary>
    /// Конец войны: главная сторона капитулирует, потеряв столицу или больше CapitulateShare населения; обе устали
    /// сильнее StalemateExhaustion — перемирие само; цель взята или сторона устала — ИИ предлагает перемирие, другая
    /// сторона (ИИ) соглашается по <see cref="WarModel.Accepts"/>, игрок решает сам (предложение ждёт три месяца).
    /// </summary>
    static void Finish(World world, War war)
    {
        var w = world.War!;
        var b = world.Data.WarBalance;
        foreach (var main in new[] { war.Attacker, war.Defender })
        {
            var capital = w.Map.Capitals.GetValueOrDefault(main);
            var lostCapital = capital != null && war.Side(world.RegionById(capital)?.Controller) == 3 - war.Side(main);
            if (lostCapital || OccupiedShare(world, main) > b.CapitulateShare)
            {
                WarModel.End(world, war, $"Капитуляция: {world[main].Name} больше не может воевать" + (lostCapital ? ": столица взята" : ": занята большая часть страны") +
                                         ". Огонь прекращён по линии фронта.", capitulated: main);
                return;
            }
        }
        var (ea, ed) = (w.Armies[war.Attacker].Exhaustion, w.Armies[war.Defender].Exhaustion);
        if (ea > b.StalemateExhaustion && ed > b.StalemateExhaustion)
        {
            WarModel.End(world, war, "Война выдохлась: обе стороны истощены, огонь прекращён по линии фронта.");
            return;
        }
        if (war.OfferDate is { } date && date.AddMonths(3) <= world.Date)
            (war.Offer, war.OfferDate) = (null, null);
        foreach (var (asker, other) in new[] { (war.Attacker, war.Defender), (war.Defender, war.Attacker) })
        {
            if (asker == world.PlayerCountry)
                continue;
            var wants = w.Armies[asker].Exhaustion > b.CeasefireExhaustion || (asker == war.Attacker && WarModel.GoalTaken(world, war));
            if (!wants || war.Offer == asker)
                continue;
            if (other == world.PlayerCountry)
            {
                (war.Offer, war.OfferDate) = (asker, world.Date);
                WarModel.News(world, war, $"{world[asker].Name} предлагает перемирие по линии фронта.");
                continue;
            }
            if (WarModel.Accepts(world, war, other))
            {
                WarModel.End(world, war, $"Перемирие: {world[war.Attacker].Name} и {world[war.Defender].Name} прекратили огонь по линии фронта" +
                                         (WarModel.GoalTaken(world, war) ? $"; спорные регионы теперь под контролем страны «{world[war.Attacker].Name}»." : "."));
                return;
            }
        }
    }

    public static string Progress(double value) => Fmt.Number(value, 0) + "%";
}
