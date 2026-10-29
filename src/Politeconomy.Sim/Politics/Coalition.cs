using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Правящая коалиция: крупнейшая партия зовёт партнёров, пока у коалиции не будет большинства мест (не больше
/// CoalitionPartners партнёров). Партнёр выбирается по «цене»: расстояние по взглядам плюс его доля мест —
/// небольшой близкий партнёр лучше крупного соперника (он потребует меньше постов). Если большинства не набрать,
/// правит меньшинство — та же коалиция. MODEL.md, «Партии».
/// </summary>
public static class Coalition
{
    public static List<int> Form(IReadOnlyList<Party> parties, IReadOnlyList<int> seats, PoliticsBalance b)
    {
        var total = seats.Sum();
        if (parties.Count == 0 || total <= 0)
            return [];
        var leader = Enumerable.Range(0, parties.Count).OrderByDescending(i => seats[i]).ThenByDescending(i => parties[i].Poll).First();
        var members = new List<int> { leader };
        double Cost(int i) => PoliticalViews.Distance(parties[i].Position, parties[leader].Position, b.Salience) + (double)seats[i] / total;
        var partners = Enumerable.Range(0, parties.Count).Where(i => i != leader && seats[i] > 0).ToList();
        while (members.Sum(m => seats[m]) * 2 <= total && members.Count - 1 < b.CoalitionPartners && partners.Count > 0)
        {
            // Если кто-то один даёт большинство — берём самого «дешёвого» из таких, иначе самого дешёвого вообще.
            var have = members.Sum(m => seats[m]);
            var enough = partners.Where(i => (have + seats[i]) * 2 > total).ToList();
            var next = (enough.Count > 0 ? enough : partners).OrderBy(Cost).First();
            members.Add(next);
            partners.Remove(next);
        }
        return members;
    }
}
