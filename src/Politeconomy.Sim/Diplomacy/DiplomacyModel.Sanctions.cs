using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy;

// Санкции: одна страна против другой и санкции Совета Наций. MODEL.md, «Санкции».
public static partial class DiplomacyModel
{
    public static readonly IReadOnlyList<string> SanctionLevels = ["partial", "full"];

    /// <summary>Уровень санкций страны by против target (full, partial) или null.</summary>
    public static string? SanctionLevel(World world, string by, string target)
    {
        string? level = null;
        foreach (var s in world.Diplomacy?.Sanctions ?? [])
            if (s.By == by && s.Target == target && (level == null || s.Level == "full"))
                level = s.Level;
        return level;
    }

    /// <summary>Санкции против страны (кто и какие).</summary>
    public static IEnumerable<Sanction> SanctionsAgainst(World world, string target) =>
        world.Diplomacy?.Sanctions.Where(s => s.Target == target) ?? [];

    public static bool UnderCouncilSanctions(World world, string country) =>
        world.Diplomacy?.CouncilSanctions.Contains(country) == true;

    public static string SanctionName(string level) => level == "full" ? "жёсткие" : "частичные";

    /// <summary>
    /// Ввести санкции (игрок): торговля с целью падает (у нас тоже), отношения хуже; цель теряет доступ к деньгам.
    /// Цель отвечает частичными санкциями, если ещё не ввела своих.
    /// </summary>
    public static bool Impose(World world, string by, string target, string level)
    {
        if (world.PlayerCountry != by || by == target || world.ById(target) is null || !SanctionLevels.Contains(level)
            || SanctionLevel(world, by, target) == level)
            return false;
        var d = world.Diplomacy!;
        d.Sanctions.RemoveAll(s => s.By == by && s.Target == target);
        d.Sanctions.Add(new Sanction(by, target, level, world.Date));
        var text = $"Введены {SanctionName(level)} санкции против страны «{world[target].Name}»: торговля с ней сократится, ей труднее занимать.";
        if (SanctionLevel(world, target, by) == null)
        {
            d.Sanctions.Add(new Sanction(target, by, "partial", world.Date) { Response = true });
            text += " В ответ она ввела частичные санкции против нас.";
        }
        world.Log(by, "diplomacy", text);
        Refresh(world);
        return true;
    }

    /// <summary>Снять санкции (игрок); цель снимает и свои ответные.</summary>
    public static bool Lift(World world, string by, string target)
    {
        if (world.PlayerCountry != by || SanctionLevel(world, by, target) == null)
            return false;
        var d = world.Diplomacy!;
        d.Sanctions.RemoveAll(s => s.By == by && s.Target == target);
        var answered = d.Sanctions.RemoveAll(s => s.By == target && s.Target == by && s.Response) > 0;
        world.Log(by, "diplomacy", $"Сняты санкции против страны «{world[target].Name}»." + (answered ? " Она отменила ответные." : ""));
        Refresh(world);
        return true;
    }
}
