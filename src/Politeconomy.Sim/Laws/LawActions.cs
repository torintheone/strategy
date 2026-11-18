using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Laws;

/// <summary>Решения игрока о законах. Вызываются в потоке симуляции (через SimulationHost.Post).</summary>
public static class LawActions
{
    /// <summary>
    /// Поручить ведомству подготовить закон в новом варианте. Срок зависит от компетентности министра.
    /// Возвращает false, если такой вариант уже действует или законопроект по этому закону уже в работе.
    /// </summary>
    public static bool Propose(World world, string countryId, string lawId, string variantId)
    {
        var laws = world.Laws?[countryId];
        if (laws == null)
            return false;
        var law = world.Data.Law(lawId);
        if (law.Variants.All(v => v.Id != variantId) || laws.Current[lawId] == variantId || laws.BillFor(lawId) != null)
            return false;
        var months = Legislation.DraftingMonths(world, countryId, law);
        laws.Bills.Add(new Bill
        {
            Law = lawId,
            Variant = variantId,
            Stage = "drafting",
            Started = world.Date,
            Ready = world.Date.AddMonths(months),
        });
        laws.Version++;
        var ministry = world.Data.Government.Posts.First(p => p.Id == law.Post).Ministry;
        world.Log(countryId, "bill", $"{ministry}: начата подготовка закона «{law.Name}: {law.Variant(variantId).Name.ToLowerInvariant()}». " +
                                     $"Проект будет готов примерно через {Text.Fmt.Plural(months, "месяц", "месяца", "месяцев")}.");
        return true;
    }

    /// <summary>Отозвать законопроект, пока он не проголосован.</summary>
    public static bool Withdraw(World world, string countryId, string lawId)
    {
        var laws = world.Laws?[countryId];
        var bill = laws?.BillFor(lawId);
        if (laws == null || bill == null)
            return false;
        laws.Bills.Remove(bill);
        bill.Stage = "withdrawn";
        bill.Decided = world.Date;
        Legislation.Remember(world, laws, bill);
        laws.Version++;
        world.Log(countryId, "bill", $"Законопроект «{world.Data.Law(lawId).Name}» отозван.");
        return true;
    }
}
