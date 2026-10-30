namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>infrastructure.json: виды с постами и статьями бюджета, стройки с видом или типом станций, кредиторы.</summary>
    static void ValidateInfrastructure(GameData data, Action<string, string> add)
    {
        const string file = "infrastructure.json";
        var types = new HashSet<string>(StringComparer.Ordinal);
        var posts = data.Government.Posts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var items = data.Budget.Spending.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var industries = data.Industries.Industries.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var t in data.Infrastructure.Types)
        {
            if (!types.Add(t.Id))
                add(file, $"вид {t.Id} встречается дважды");
            if (!posts.Contains(t.Post))
                add(file, $"{t.Id}: нет поста «{t.Post}» в government.json");
            if (t.Budget != null && !items.Contains(t.Budget))
                add(file, $"{t.Id}: нет статьи расходов «{t.Budget}» в budget.json");
            if (t.Stock <= 0 || t.Wear < 0 || t.BuildYears <= 0 || t.Households is < 0 or > 1)
                add(file, $"{t.Id}: стоимость и срок стройки — больше нуля, доля населения — от 0 до 1");
            if (t.Driver is not (null or "trade" or "power"))
                add(file, $"{t.Id}: driver — trade, power или нет");
            foreach (var industry in t.Weights.Keys.Where(k => !industries.Contains(k)))
                add(file, $"{t.Id}: нет отрасли «{industry}» в industries.json");
        }
        var plants = data.Energy.Plants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in data.Infrastructure.Projects)
        {
            if (!ids.Add(p.Id))
                add(file, $"стройка {p.Id} встречается дважды");
            if ((p.Type == null) == (p.Plant == null))
                add(file, $"{p.Id}: нужен либо type (вид инфраструктуры), либо plant (тип станций)");
            if (p.Type != null && !types.Contains(p.Type))
                add(file, $"{p.Id}: нет вида «{p.Type}»");
            if (p.Plant != null && !plants.Contains(p.Plant))
                add(file, $"{p.Id}: нет станций «{p.Plant}» в energy.json");
            if (p.Kind is not ("capacity" or "repair") || p.Plant != null && p.Kind == "repair")
                add(file, $"{p.Id}: kind — capacity или repair (у станций — только capacity)");
            if (p.Gain is <= 0 or > 1 || p.Type != null && p.Months <= 0)
                add(file, $"{p.Id}: прибавка — от 0 до 1, срок — больше нуля");
        }
        var countries = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var lender in data.Infrastructure.Lenders.Where(l => !countries.Contains(l)))
            add(file, $"кредитор «{lender}»: нет такой страны");
    }
}
