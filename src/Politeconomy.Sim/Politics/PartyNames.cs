using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Названия партий из частей data/parties.json: «Социалистическая партия», «Народная партия труда»,
/// «Союз обновления», «Движение «Новый путь»». Короткое название — инициалы или слова в кавычках.
/// </summary>
public static class PartyNames
{
    public static (string Name, string Short) Generate(PartiesFile parties, PartyFamilyRecord family, string region, DeterministicRandom r,
        ICollection<string> used)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var (name, quoted) = One(parties, family, region, r);
            var @short = Short(name, quoted);
            // Ни полное, ни короткое название не должны совпадать с уже занятыми в стране.
            if (used.Contains(name) || used.Contains("#" + @short) || Redundant(name))
                continue;
            used.Add(name);
            used.Add("#" + @short);
            return (name, @short);
        }
        // Запасной вариант: семейство и номер — так бывает, только если частей названий слишком мало.
        var fallback = $"{family.Name}-{used.Count + 1}";
        used.Add(fallback);
        return (fallback, fallback);
    }

    static (string Name, string? Quoted) One(PartiesFile parties, PartyFamilyRecord family, string region, DeterministicRandom r)
    {
        var noun = Noun(parties.Nouns, region, r);
        var gender = noun.Gender switch { "f" => 1, "n" => 2, _ => 0 };
        var hasAdjective = family.Adjectives.Count > 0;
        var hasTail = family.Tails.Count > 0;
        var hasQuoted = family.Quoted.Count > 0;
        // Шаблоны: прилагательное + слово; прилагательное + слово + хвост; слово + хвост; слово «название».
        var weights = new[] { hasAdjective ? 0.35 : 0, hasAdjective && hasTail ? 0.15 : 0, hasTail ? 0.3 : 0, hasQuoted ? 0.2 : 0 };
        var pattern = Pick(weights, r);
        string Adjective() => family.Adjectives[r.Next(0, family.Adjectives.Count)][gender];
        string Tail() => family.Tails[r.Next(0, family.Tails.Count)];
        return pattern switch
        {
            0 => ($"{Adjective()} {noun.Word}", null),
            1 => ($"{Adjective()} {noun.Word} {Tail()}", null),
            2 => ($"{Capital(noun.Word)} {Tail()}", null),
            _ => Quoted(noun, family, r),
        };
    }

    static (string, string?) Quoted(PartyNounRecord noun, PartyFamilyRecord family, DeterministicRandom r)
    {
        var title = family.Quoted[r.Next(0, family.Quoted.Count)];
        return ($"{Capital(noun.Word)} «{title}»", title);
    }

    static PartyNounRecord Noun(IReadOnlyList<PartyNounRecord> nouns, string region, DeterministicRandom r)
    {
        var weights = nouns.Select(n => n.Weight * (n.Regions?.GetValueOrDefault(region, 1) ?? 1)).ToArray();
        return nouns[Pick(weights, r)];
    }

    static int Pick(IReadOnlyList<double> weights, DeterministicRandom r)
    {
        var total = weights.Sum();
        var x = r.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            x -= weights[i];
            if (x < 0 && weights[i] > 0)
                return i;
        }
        for (var i = weights.Count - 1; i >= 0; i--)
            if (weights[i] > 0)
                return i;
        return 0;
    }

    /// <summary>Тавтология вроде «Народная партия народной воли»: корень первого слова повторяется дальше.</summary>
    static bool Redundant(string name)
    {
        var words = name.ToLowerInvariant().Split(' ');
        if (words.Length < 3 || words[0].Length < 5)
            return false;
        var root = words[0][..4];
        return words.Skip(1).Any(w => w.StartsWith(root, StringComparison.Ordinal));
    }

    static string Capital(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>Короткое название: слова в кавычках, если они короткие, иначе заглавные буквы слов («СДП», «НПТ»).</summary>
    public static string Short(string name, string? quoted)
    {
        if (quoted != null && quoted.Length <= 16)
            return quoted;
        var words = (quoted ?? name).Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 || w.Length == 2 && char.IsUpper(w[0]));
        return string.Concat(words.Select(w => char.ToUpperInvariant(w.TrimStart('«')[0])));
    }
}
