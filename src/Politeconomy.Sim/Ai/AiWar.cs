using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Military;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Ai;

/// <summary>
/// Война в решениях ИИ: раз в WarMonths страна, которая ни с кем не воюет и не устала, смотрит на тех, с кем у неё спор
/// за регионы (ещё не взятые) или заклятая вражда (отношения ниже Hostile), и с кем её земли соприкасаются. Нападает, если
/// цель не ядерная держава, перемирие длится дольше CeasefireMonths, перевес сил — с учётом союзников цели и её сильных
/// друзей (Deterrence их силы) — не меньше WarRatio (демократия — DemocracyRatio и только за спорные регионы), польза
/// (сила × перевес − потери торговли − риск) положительная, — и то с вероятностью WarChance. MODEL.md, «ИИ стран».
/// </summary>
public static class AiWar
{
    public static void Consider(AiTurn turn)
    {
        var world = turn.World;
        var b = world.Data.WarBalance;
        var id = turn.Id;
        if (world.War is null || WarModel.AtWar(world, id) || !turn.Ai.Ready("war", world.Date, b.WarMonths))
            return;
        turn.Ai.Last["war"] = world.Date;
        if (world.War.Armies[id].Exhaustion > 20)
            return;
        var nuclear = world.Data.Diplomacy.Nuclear.ToHashSet(StringComparer.Ordinal);
        var claimed = world.Data.Military.Claims.Where(c => c.Claimant == id).SelectMany(c => c.Regions)
            .Select(r => world.RegionById(r)?.Controller).Where(c => c != null && c != id).Cast<string>();
        var hostile = world.Countries.Where(c => c.Id != id && DiplomacyModel.Relations(world, id, c.Id) < b.Hostile).Select(c => c.Id);
        var best = (Target: (string?)null, Utility: 0.0, Why: "");
        foreach (var target in claimed.Concat(hostile).Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            if (nuclear.Contains(target) || world.ById(target) is null || !WarModel.CanDeclare(world, id, target).Allowed)
                continue;
            if (WarModel.CeasefireBetween(world, id, target) is { } truce && truce.Since.AddMonths(b.CeasefireMonths) > world.Date)
                continue;
            var claim = WarModel.Claims(world, id, target).Any();
            if (turn.Country.Regime == Regime.Democracy && !claim)
                continue;
            var ours = WarModel.Strength(world, id) * b.CommitMain;
            var defenders = WarModel.Defenders(world, id, target);
            var theirs = defenders.Sum(d => WarModel.Strength(world, d) * (d == target ? b.CommitMain : b.CommitAlly));
            var friends = world.Countries.Where(f => f.Id != id && !defenders.Contains(f.Id) && !DiplomacyModel.Allies(world, f.Id, id)
                                                     && DiplomacyModel.Relations(world, target, f.Id) > 40)
                .Sum(f => WarModel.Strength(world, f.Id));
            theirs += b.Deterrence * friends;
            var need = turn.Country.Regime == Regime.Democracy ? b.DemocracyRatio : b.WarRatio;
            var ratio = theirs > 0 ? ours / theirs : 99;
            if (ratio < need)
                continue;
            var trade = DiplomacyModel.TradeShare(world, id, target) * 10;
            var utility = turn.Ai.Power * (claim ? 1 : 0.5) * Math.Min(2, ratio / need) - turn.Ai.Growth * trade - turn.Ai.Stability * 0.3;
            if (utility <= best.Utility || utility <= 0.1)
                continue;
            var regions = WarModel.Claims(world, id, target).Select(r => WarModel.RegionName(world, r)).ToList();
            best = (target, utility, $"перевес сил {Fmt.Number(ratio, 1)} : 1 с учётом союзников и друзей цели" +
                                     (claim ? $"; спорные регионы: {string.Join(", ", regions)}" : $"; заклятый враг: отношения {Fmt.Signed(DiplomacyModel.Relations(world, id, target), 0)}") +
                                     $"; польза {Fmt.Number(utility, 2)} (приоритеты: {AiModel.Describe(turn.Ai)})");
        }
        if (best.Target is not { } chosen || !Politics.PoliticsModel.Rng(world).Chance(b.WarChance))
            return;
        if (WarModel.Declare(world, id, chosen) is not { } war)
            return;
        turn.Done("war");
        turn.Record("war", $"Начинает войну против страны «{world[chosen].Name}»" +
                           (war.Defenders.Count > 1 ? $"; за неё вступились: {string.Join(", ", war.Defenders.Skip(1).Select(d => world[d].Name))}." : "."), best.Why);
    }
}
