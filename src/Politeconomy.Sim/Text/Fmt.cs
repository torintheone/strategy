using System.Globalization;

namespace Politeconomy.Sim.Text;

/// <summary>Вывод чисел по-русски: запятая в дробях, неразрывный пробел между разрядами, «млн», «млрд».</summary>
public static class Fmt
{
    public const char Nbsp = ' ';
    public const char Minus = '−';

    static readonly NumberFormatInfo Ru = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = Nbsp.ToString(),
        NegativeSign = Minus.ToString(),
    };

    /// <summary>Значение показателя в его формате из data/indicators.json.</summary>
    public static string Value(string format, double v, string? unit = null)
    {
        if (double.IsNaN(v))
            return "—";
        var text = format switch
        {
            "people" => People(v),
            "money_big" => MoneyBig(v),
            "money" => Number(v, 0) + Nbsp + "$",
            "percent" => Number(v, 1) + "%",
            "percent_signed" => Signed(v, 1) + "%",
            "percent_gdp" => Number(v, 1) + "%" + Nbsp + "ВВП",
            "percent_gdp_signed" => Signed(v, 1) + "%" + Nbsp + "ВВП",
            "years" => Number(v, 1) + Nbsp + "года",
            "number0" => Number(v, 0),
            "number1" => Number(v, 1),
            "number2" => Number(v, 2),
            "number1_signed" => Signed(v, 1),
            "rating" => Rating(v),
            _ => Number(v, 1),
        };
        return unit is null ? text : text + Nbsp + unit;
    }

    /// <summary>Короткая запись для легенды карты: без лишних знаков после запятой.</summary>
    public static string Compact(string format, double v) => format switch
    {
        "people" => People(v, compact: true),
        "money_big" => MoneyBig(v),
        "money" => Number(v, 0) + Nbsp + "$",
        "percent" or "percent_gdp" => Number(v, v == Math.Round(v) ? 0 : 1) + "%",
        "percent_signed" or "percent_gdp_signed" => Signed(v, v == Math.Round(v) ? 0 : 1) + "%",
        "years" => Number(v, 0),
        "rating" => Rating(v),
        _ => Number(v, v == Math.Round(v) ? 0 : 1),
    };

    public static string Number(double v, int decimals) =>
        v.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Ru);

    public static string Signed(double v, int decimals)
    {
        var rounded = Math.Round(v, decimals);
        var body = Number(Math.Abs(rounded), decimals);
        return rounded > 0 ? "+" + body : rounded < 0 ? Minus + body : body;
    }

    public static string People(double v, bool compact = false)
    {
        var a = Math.Abs(v);
        if (a >= 1e9) return Scaled(v / 1e9, compact ? 1 : 2) + Nbsp + "млрд";
        if (a >= 1e6) return Scaled(v / 1e6, 1) + Nbsp + "млн";
        if (a >= 1e3) return Scaled(v / 1e3, 1) + Nbsp + "тыс.";
        return Number(v, 0);
    }

    public static string MoneyBig(double v)
    {
        var a = Math.Abs(v);
        if (a >= 1e12) return Scaled(v / 1e12, 2) + Nbsp + "трлн" + Nbsp + "$";
        if (a >= 1e9) return Scaled(v / 1e9, 1) + Nbsp + "млрд" + Nbsp + "$";
        if (a >= 1e6) return Scaled(v / 1e6, 0) + Nbsp + "млн" + Nbsp + "$";
        return Number(v, 0) + Nbsp + "$";
    }

    /// <summary>Убирает «,0» у круглых чисел: «5 млн», а не «5,0 млн».</summary>
    static string Scaled(double v, int decimals)
    {
        var rounded = Math.Round(v, decimals);
        return Number(rounded, rounded == Math.Round(rounded) ? 0 : decimals);
    }

    static readonly string[] Months =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    ];

    /// <summary>Кредитный рейтинг по шкале 0–21: 21 — AAA, 0 — D.</summary>
    public static string Rating(double score) => Economy.Systems.RatingSystem.Letter(score);

    /// <summary>«1 января 2026».</summary>
    /// <summary>
    /// Название в кавычках-«ёлочках»; кавычки внутри становятся „лапками“: «Партия „Вера и семья“».
    /// </summary>
    public static string Quote(string name) => "«" + name.Replace('«', '„').Replace('»', '“') + "»";

    static readonly string[] ShortMonths = ["янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    /// <summary>Месяц и год коротко: «мар 2027» — для подписей на графиках.</summary>
    public static string MonthYear(DateOnly d) => $"{ShortMonths[d.Month - 1]}{Nbsp}{d.Year}";

    public static string Date(DateOnly d) => $"{d.Day}{Nbsp}{Months[d.Month - 1]} {d.Year}";

    /// <summary>Число с существительным в нужной форме: Plural(5, "год", "года", "лет") → «5 лет».</summary>
    public static string Plural(long n, string one, string few, string many)
    {
        var tens = Math.Abs(n) % 100;
        var units = tens % 10;
        var word = tens is >= 11 and <= 14 ? many : units == 1 ? one : units is >= 2 and <= 4 ? few : many;
        return $"{n}{Nbsp}{word}";
    }

    /// <summary>Возраст: «41 год», «43 года», «45 лет».</summary>
    public static string Age(int years) => Plural(years, "год", "года", "лет");
}
