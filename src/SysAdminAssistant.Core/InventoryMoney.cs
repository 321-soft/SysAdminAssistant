using System.Globalization;
namespace SysAdminAssistant.Core;

public static class InventoryMoney
{
    public const long MaximumKopecks = 99999999999999;
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    public static long? Parse(string text)
    {
        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Replace('.', ',');
        if (text.Length == 0) return null;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, Ru, out var value) || value < 0 || value > MaximumKopecks / 100m || decimal.Round(value, 2) != value)
            throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Стоимость: неотрицательное число в рублях, до двух знаков после запятой; пусто — не указана"));
        return decimal.ToInt64(value * 100m);
    }
    public static string Edit(long? kopecks) => kopecks == null ? "" : (kopecks.Value / 100m).ToString("0.00", Ru);
    public static string Print(long? kopecks) => kopecks == null ? "—" : (kopecks.Value / 100m).ToString("N2", Ru);
}
