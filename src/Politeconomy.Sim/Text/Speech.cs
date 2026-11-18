using System.Text;
using System.Text.RegularExpressions;

namespace Politeconomy.Sim.Text;

/// <summary>
/// Текст для синтезатора речи: тот же текст, что на экране, но без знаков, которые голос прочтёт неверно или
/// не прочтёт вовсе. «8,4%» → «8,4 процента», «−2,1%» → «минус 2,1 процента», «1,2 млрд $» → «1,2 миллиарда
/// долларов», «BBB−» → «BBB минус»; неразрывные пробелы внутри чисел убираются («12 500» → «12500»). Кавычки
/// остаются: голос их не читает. Цифры остаются цифрами: синтезатор сам читает их по-русски. MODEL.md, «Телевыпуск».
/// </summary>
public static partial class Speech
{
    public static string Say(string text)
    {
        var s = text;
        // Разряды числа: «12 500» → «12500», иначе голос прочтёт «двенадцать пятьсот».
        s = DigitGroups().Replace(s, "$1$2");
        s = s.Replace(Fmt.Nbsp, ' ').Replace(' ', ' ');
        // Рейтинг: «BBB−», «AA+».
        s = RatingSign().Replace(s, m => m.Groups[1].Value + (m.Groups[2].Value == "+" ? " плюс" : " минус"));
        // Знак перед числом.
        s = SignedNumber().Replace(s, m => (m.Groups[1].Value == "+" ? "плюс " : "минус ") + m.Groups[2].Value);
        // Число и единица: проценты, миллионы, доллары — в нужной форме по числу.
        s = NumberUnit().Replace(s, m => m.Groups["pre"].Value + m.Groups["n"].Value + " "
            + Unit(m.Groups["n"].Value, m.Groups["u"].Value, Genitive.Contains(m.Groups["pre"].Value.Trim().ToLowerInvariant())));
        // Доллары после слова-множителя: «миллиарда $» → «миллиарда долларов».
        s = s.Replace(" $", " долларов").Replace("$", " долларов");
        // Диапазон лет: «2027–2028» → «2027 — 2028».
        s = Range().Replace(s, "$1 — $2");
        s = s.Replace("№", "номер ");
        s = Spaces().Replace(s, " ").Trim();
        if (s.Length > 0 && !".!?…".Contains(s[^1]))
            s += ".";
        return s;
    }

    /// <summary>Предлоги, после которых число и единица — в родительном падеже: «до 5 процентов», «от 21 миллиона».</summary>
    static readonly HashSet<string> Genitive = ["до", "от", "около", "свыше", "более", "менее", "из"];

    /// <summary>
    /// Слово единицы в форме, которая нужна после числа: 1 процент, 2 процента, 5 процентов, 2,5 процента; после
    /// предлога с родительным падежом — 1 процента, 2 процентов.
    /// </summary>
    static string Unit(string number, string unit, bool genitive)
    {
        var (one, few, many) = unit switch
        {
            "%" => ("процент", "процента", "процентов"),
            "тыс." => ("тысяча", "тысячи", "тысяч"),
            "млн" => ("миллион", "миллиона", "миллионов"),
            "млрд" => ("миллиард", "миллиарда", "миллиардов"),
            "трлн" => ("триллион", "триллиона", "триллионов"),
            _ => ("доллар", "доллара", "долларов"),
        };
        if (unit == "тыс.")
            few = genitive ? "тысяч" : few;
        // Дробное число читается «целых … десятых» — после него всегда родительный падеж единственного числа.
        if (number.Contains(','))
            return unit == "тыс." ? "тысячи" : few;
        var digits = new StringBuilder();
        foreach (var c in number)
            if (char.IsDigit(c))
                digits.Append(c);
        if (!long.TryParse(digits.ToString(), out var n))
            return many;
        var tens = n % 100;
        var units = tens % 10;
        var single = tens is < 11 or > 14 && units == 1;
        if (genitive)
            return single ? (unit == "тыс." ? "тысячи" : few) : many;
        return tens is >= 11 and <= 14 ? many : single ? one : units is >= 2 and <= 4 ? few : many;
    }

    [GeneratedRegex(@"(\d)[   ](?=\d{3}(?!\d))(\d)")]
    private static partial Regex DigitGroups();

    [GeneratedRegex(@"\b([A-D]{1,3})([+−])(?![\d])")]
    private static partial Regex RatingSign();

    [GeneratedRegex(@"(?<![\w\d])([+−])(\d[\d,]*)")]
    private static partial Regex SignedNumber();

    [GeneratedRegex(@"(?<pre>(?<![\w])\p{L}+\s)?(?<n>\d[\d,]*)\s?(?<u>%|тыс\.|млн|млрд|трлн|\$)")]
    private static partial Regex NumberUnit();

    [GeneratedRegex(@"(\d{4})–(\d{4})")]
    private static partial Regex Range();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
