using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy;

// Блоки: торговые и военные союзы с реальным составом на 1 января 2026 года. MODEL.md, «Блоки».
public static partial class DiplomacyModel
{
    /// <summary>Члены блока (коды стран по алфавиту).</summary>
    public static IReadOnlyList<string> Members(World world, string bloc) =>
        world.Diplomacy?.Blocs.GetValueOrDefault(bloc) ?? (IReadOnlyList<string>)[];

    public static bool InBloc(World world, string bloc, string country) =>
        world.Diplomacy?.Blocs.GetValueOrDefault(bloc) is { } members && members.BinarySearch(country, StringComparer.Ordinal) >= 0;

    /// <summary>Блоки, в которых состоит страна (в порядке данных).</summary>
    public static IEnumerable<BlocRecord> BlocsOf(World world, string country) =>
        world.Data.Diplomacy.Blocs.Where(b => InBloc(world, b.Id, country));

    /// <summary>Союзники: вместе в военном блоке или с договором о военном союзе.</summary>
    public static bool Allies(World world, string a, string b) =>
        a != b && (world.Data.Diplomacy.Blocs.Any(x => x.Military && InBloc(world, x.Id, a) && InBloc(world, x.Id, b))
                   || HasTreaty(world, a, b, "alliance"));

    /// <summary>Примут ли страну в блок и почему: регион, строй, соперничающий блок, отношения с членами.</summary>
    public static (bool Accepts, string Reason) JoinAcceptance(World world, string country, string blocId)
    {
        var data = world.Data;
        var b = data.DiplomacyBalance;
        if (data.Diplomacy.Bloc(blocId) is not { } bloc)
            return (false, "Такого блока нет.");
        if (InBloc(world, blocId, country))
            return (false, "Страна уже в этом блоке.");
        var c = world[country];
        if (!bloc.Regions.Contains(c.Info.Region))
            return (false, $"Принимают только страны {string.Join(" и ", bloc.Regions.Select(RegionGenitive))}.");
        if (bloc.Democracy && c.Regime != Model.Regime.Democracy)
            return (false, "Принимают только демократии.");
        if (bloc.Military && BlocsOf(world, country).FirstOrDefault(x => x.Military) is { } other)
            return (false, $"Страна уже в другом военном блоке — «{other.Name}».");
        var members = Members(world, blocId);
        if (members.Count == 0)
            return (true, "В блоке никого нет: страна станет первым членом.");
        var need = bloc.Military ? b.JoinMilitaryRelations : b.JoinRelations;
        var against = members.Where(m => Relations(world, m, country) < need || SanctionLevel(world, m, country) != null).ToList();
        var yes = members.Count - against.Count;
        string Names(IEnumerable<string> ids) => string.Join(", ", ids.Take(3).Select(id => world[id].Name)) + (against.Count > 3 ? $" и ещё {against.Count - 3}" : "");
        if (bloc.Unanimous)
            return against.Count == 0
                ? (true, $"Все {members.Count} членов «за».")
                : (false, $"Нужно согласие каждого члена (отношения не ниже {Text.Fmt.Signed(need, 0)}, без санкций). Против: {Names(against)}.");
        return yes * 2 > members.Count
            ? (true, $"«За» {yes} из {members.Count} членов.")
            : (false, $"«За» только {yes} из {members.Count}: нужно больше половины (отношения не ниже {Text.Fmt.Signed(need, 0)}, без санкций).");
    }

    static string RegionGenitive(string region) => region switch
    {
        "Европа" => "Европы",
        "Азия" => "Азии",
        "Африка" => "Африки",
        "Америка" => "Америки",
        "Океания" => "Океании",
        _ => region,
    };

    /// <summary>Вступить в блок (игрок): члены голосуют по отношениям.</summary>
    public static bool Join(World world, string country, string blocId)
    {
        if (world.PlayerCountry != country || world.Data.Diplomacy.Bloc(blocId) is not { } bloc)
            return false;
        var (accepts, reason) = JoinAcceptance(world, country, blocId);
        if (!accepts)
        {
            world.Log(country, "diplomacy", $"Заявка на вступление в «{bloc.Name}» отклонена. {reason}");
            return false;
        }
        var d = world.Diplomacy!;
        var members = d.Blocs.TryGetValue(blocId, out var list) ? list : d.Blocs[blocId] = [];
        members.Add(country);
        members.Sort(StringComparer.Ordinal);
        world.Log(country, "diplomacy", $"Страна вступила в «{bloc.Name}»." + (bloc.Military
            ? " Теперь мы защищаем союзников, а они — нас."
            : " Торговля с членами блока станет свободнее."));
        Refresh(world);
        return true;
    }

    /// <summary>Выйти из блока (игрок): торговля с членами падает, отношения с ними портятся.</summary>
    public static bool Leave(World world, string country, string blocId)
    {
        if (world.PlayerCountry != country || world.Data.Diplomacy.Bloc(blocId) is not { } bloc || !InBloc(world, blocId, country))
            return false;
        var members = world.Diplomacy!.Blocs[blocId];
        members.Remove(country);
        foreach (var m in members)
            Remember(world, country, m, -world.Data.DiplomacyBalance.LeaveRelations);
        world.Log(country, "diplomacy", $"Страна вышла из «{bloc.Name}»: отношения с его членами ухудшились.");
        Refresh(world);
        return true;
    }
}
