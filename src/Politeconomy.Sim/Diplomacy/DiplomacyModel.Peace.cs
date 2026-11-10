using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy;

// Дипломатия мирного договора: уступки и отношения. MODEL.md, «Мирные договоры».
public static partial class DiplomacyModel
{
    /// <summary>
    /// Мир подписан: отношения сторон + relations. Торговые уступки — уступающий снимает санкции против победителя
    /// (и победитель — свои ответные), стороны торгуют свободно. Нейтралитет — уступающий выходит из военных блоков,
    /// где нет победителя, и из военных союзов.
    /// </summary>
    public static void Peace(World world, string winner, string loser, bool trade, bool neutrality, double relations)
    {
        if (world.Diplomacy is not { } d)
            return;
        if (trade)
        {
            d.Sanctions.RemoveAll(s => (s.By == loser && s.Target == winner) || (s.By == winner && s.Target == loser && s.Response));
            var common = BlocsOf(world, winner).Any(x => x.Trade > 0 && InBloc(world, x.Id, loser));
            if (!common && !HasTreaty(world, winner, loser, "free_trade"))
                d.Treaties.Add(new Treaty(winner, loser, "free_trade", world.Date));
        }
        if (neutrality)
        {
            foreach (var bloc in BlocsOf(world, loser).Where(x => x.Military && !InBloc(world, x.Id, winner)).ToList())
                d.Blocs[bloc.Id].Remove(loser);
            d.Treaties.RemoveAll(t => t.Type == "alliance" && t.Involves(loser));
        }
        Remember(world, winner, loser, relations);
        Refresh(world);
    }

    /// <summary>Нейтралитет по мирному договору: почему стране нельзя в военный блок или союз (null — можно).</summary>
    static string? NeutralReason(World world, string country) =>
        Military.PeaceModel.NeutralUntil(world, country) is { } until
            ? $"По мирному договору {world[country].Name} нейтральна до {Text.Fmt.Date(until)}: военные блоки и союзы ей закрыты."
            : null;
}
