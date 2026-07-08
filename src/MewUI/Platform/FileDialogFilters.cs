using System.Text;

namespace Aprillz.MewUI.Platform;

/// <summary>
/// Translates structured <see cref="FileFilter"/> lists into the legacy "Name|*.a;*.b|Name2|*.c" pipe
/// string that the platform dialog services parse into their native filter formats. Keeps filter
/// translation in one place so each backend only needs the string form it already understands.
/// </summary>
internal static class FileDialogFilters
{
    public static string? ToLegacyFilterString(IReadOnlyList<FileFilter>? filters)
    {
        if (filters is not { Count: > 0 })
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var filter in filters)
        {
            if (string.IsNullOrEmpty(filter.Name) || filter.Patterns.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('|');
            }
            builder.Append(filter.Name);
            builder.Append('|');
            builder.Append(string.Join(';', filter.Patterns));
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
