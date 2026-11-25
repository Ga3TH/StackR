using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StackR.Core.Sites;

// Nom de site valide (étiquette DNS) dérivé d'un nom de dossier.
public readonly partial record struct SiteName(string Value)
{
    public string Host => Value + ".test";

    public static bool TryFromFolder(string folderName, out SiteName name)
    {
        name = default;
        if (string.IsNullOrEmpty(folderName) || folderName.StartsWith('.'))
        {
            return false;
        }

        var sb = new StringBuilder(folderName.Length);
        foreach (var c in folderName.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            sb.Append(lower is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? lower : '-');
        }

        var slug = MultiDashRegex().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length is < 1 or > 63 || !ValidRegex().IsMatch(slug))
        {
            return false;
        }

        name = new SiteName(slug);
        return true;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDashRegex();

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex ValidRegex();
}
