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
            "years" => Number(v, 1) + Nbsp + "года",
            "number1" => Number(v, 1),
            "number2" => Number(v, 2),
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
        "percent_signed" => Signed(v, v == Math.Round(v) ? 0 : 1) + "%",
        "years" => Number(v, 0),
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

    /// <summary>«1 января 2026».</summary>
    public static string Date(DateOnly d) => $"{d.Day}{Nbsp}{Months[d.Month - 1]} {d.Year}";
}
