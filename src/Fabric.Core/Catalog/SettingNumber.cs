using System.Globalization;

namespace Fabric.Core.Catalog;

/// <summary>How a setting's number is read and written, in the catalog, in <c>fabric.yaml</c> and on the command line.</summary>
public static class SettingNumber
{
    public static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    /// <summary><c>4</c>, <c>3.5</c>: invariant, without trailing zeros, so the same value always reads the same.</summary>
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
