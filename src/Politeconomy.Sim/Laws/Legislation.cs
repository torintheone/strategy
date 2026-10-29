using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Laws;

/// <summary>Подготовка, голосование и вступление закона в силу. MODEL.md, «Законы».</summary>
public static class Legislation
{
    /// <summary>Сколько месяцев министерство готовит закон: базовый срок с поправкой на компетентность министра.</summary>
    public static int DraftingMonths(World world, string countryId, LawRecord law)
    {
        var minister = world.Government?[countryId].Minister(law.Post);
        return DraftingMonths(world.Data.LawsBalance, law, minister?.Competence ?? 50, minister?.Has("reformer") == true);
    }

    public static int DraftingMonths(LawsBalance b, LawRecord law, double competence, bool reformer)
    {
        var factor = Math.Clamp(b.DraftingBase - competence / 100, b.DraftingMin, b.DraftingMax);
        if (reformer)
            factor *= b.ReformerSpeed;
        return Math.Max(1, (int)Math.Round(law.DraftingMonths * factor));
    }

    /// <summary>
    /// Одобрение закона в обществе от 0 до 1: сдвиг отношения каждого класса (новый вариант минус нынешний),
    /// взвешенный по влиянию классов. 0,5 — общество равнодушно.
    /// </summary>
    public static double Support(GameData data, LawRecord law, string from, string to, IReadOnlyList<double> influence)
    {
        var shifts = Shifts(data, law, from, to);
        var shift = 0.0;
        for (var k = 0; k < shifts.Length && k < influence.Count; k++)
            shift += influence[k] / 100 * shifts[k];
        return 0.5 + 0.5 * Math.Tanh(shift / data.LawsBalance.SupportScale);
    }

    /// <summary>
    /// Одобрение закона избирателями партии от 0 до 1: сдвиг отношения каждого класса, взвешенный по его доле
    /// среди избирателей партии (voters). По нему голосует фракция партии.
    /// </summary>
    public static double PartySupport(GameData data, LawRecord law, string from, string to, IReadOnlyList<double> voters) =>
        Politics.PoliticsModel.Support(voters, Shifts(data, law, from, to), data.LawsBalance.SupportScale);

    /// <summary>Как изменится отношение каждого класса к закону при переходе с варианта from на to.</summary>
    public static double[] Shifts(GameData data, LawRecord law, string from, string to)
    {
        var classes = data.Classes.Classes;
        var old = law.Variant(from);
        var proposed = law.Variant(to);
        return classes.Select(c => proposed.Attitudes.GetValueOrDefault(c.Id) - old.Attitudes.GetValueOrDefault(c.Id)).ToArray();
    }

    /// <summary>Голосование по готовому законопроекту; при успехе закон вступает в силу с переходным периодом.</summary>
    public static void Decide(World world, string countryId, CountryLaws laws, Bill bill)
    {
        var data = world.Data;
        var law = data.Law(bill.Law);
        var influence = world.Society?[countryId].Influence ?? new double[data.Classes.Classes.Count];
        bill.Support = Support(data, law, laws.Current[bill.Law], bill.Variant, influence);
        var parliament = world.Government![countryId].Parliament;
        var shifts = Shifts(data, law, laws.Current[bill.Law], bill.Variant);
        var general = bill.Support;
        // Группы интересов лоббируют: одобрение у всех фракций сдвигается на одну и ту же величину.
        var lobby = Politics.InterestGroupsModel.Lobby(world, countryId, law, laws.Current[bill.Law], bill.Variant);
        bill.Vote = ParliamentVote.Cast(world, parliament,
            party => Math.Clamp((Politics.PoliticsModel.Voters(world, countryId, party) is { } voters
                ? Politics.PoliticsModel.Support(voters, shifts, data.LawsBalance.SupportScale)
                : general) + lobby, 0, 1),
            law.Constitutional);
        bill.Decided = world.Date;
        bill.Stage = bill.Vote.Passed ? "passed" : "rejected";
        laws.Bills.Remove(bill);
        Remember(world, laws, bill);
        laws.Version++;
        var title = $"«{law.Name}: {law.Variant(bill.Variant).Name.ToLowerInvariant()}»";
        var v = bill.Vote;
        if (v.Decree)
            world.Log(countryId, "law", $"Закон {title} введён указом.");
        else if (v.Passed)
            world.Log(countryId, "law", $"{parliament.Name} принял закон {title}: за {v.Yes}, против {v.No}.");
        else
            world.Log(countryId, "law", $"{parliament.Name} отклонил закон {title}: за {v.Yes}, против {v.No}, нужно {v.Required}.");
        if (!v.Passed)
            return;
        if (world.Reign is { } reign && reign.Country == countryId)
            reign.LawsPassed++;
        laws.Transitions.RemoveAll(t => t.Law == bill.Law);
        var previous = laws.Current[bill.Law];
        laws.Transitions.Add(new LawTransition
        {
            Law = bill.Law,
            From = laws.Current[bill.Law],
            To = bill.Variant,
            Begin = world.Date,
            Months = law.TransitionMonths,
        });
        laws.Current[bill.Law] = bill.Variant;
        LawModel.Recompute(data, laws, world.Date);
        // Кому закон сильно не нравится, выходят на улицы и требуют вернуть прежний.
        Politics.ProtestModel.LawPassed(world, countryId, law, previous, bill.Variant);
    }

    public static void Remember(World world, CountryLaws laws, Bill bill)
    {
        laws.Finished.Add(bill);
        var limit = world.Data.LawsBalance.History;
        if (laws.Finished.Count > limit)
            laws.Finished.RemoveRange(0, laws.Finished.Count - limit);
    }
}
