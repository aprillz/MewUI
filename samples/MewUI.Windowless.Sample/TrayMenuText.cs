using System.Text;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Windowless.Sample;

/// <summary>Turns a menu item's text, with MewUI's "_" access key markers, into native menu titles.</summary>
internal static class TrayMenuText
{
    /// <summary>A Win32 menu string: the access key marker becomes "&amp;", and a literal "&amp;" is doubled.</summary>
    public static string ForWin32(MenuItem item) => Convert(item, accessKeyMarker: "&");

    /// <summary>A macOS menu title: macOS menus have no access keys, so the markers are dropped.</summary>
    public static string ForMacOS(MenuItem item) => Convert(item, accessKeyMarker: string.Empty);

    private static string Convert(MenuItem item, string accessKeyMarker)
    {
        // An item without its own text shows its command's, whose access text keeps the markers.
        string text = item.Text.Length > 0 ? item.Text : item.Command?.Presentation.AccessText ?? string.Empty;

        var builder = new StringBuilder(text.Length + 1);
        bool markerUsed = false;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current == '_' && index + 1 < text.Length)
            {
                // "__" is a literal underscore; the first other "_" marks the access key.
                index++;
                if (text[index] == '_')
                {
                    builder.Append('_');
                    continue;
                }

                if (!markerUsed)
                {
                    builder.Append(accessKeyMarker);
                    markerUsed = true;
                }
                current = text[index];
            }

            if (current == '&' && accessKeyMarker.Length > 0)
            {
                builder.Append('&');
            }
            builder.Append(current);
        }

        return builder.ToString();
    }
}
