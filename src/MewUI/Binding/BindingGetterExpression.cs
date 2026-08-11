namespace Aprillz.MewUI;

internal static class BindingGetterExpression
{
    /// <summary>
    /// Extracts the observed property name from a caller-supplied getter expression such as
    /// <c>x =&gt; x.Name</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the expression is not a single member access.</exception>
    internal static string InferPropertyName(string? getterExpression)
    {
        if (string.IsNullOrWhiteSpace(getterExpression))
        {
            throw Reject(getterExpression);
        }

        var body = getterExpression.AsSpan();
        int arrow = body.IndexOf("=>".AsSpan(), StringComparison.Ordinal);
        if (arrow < 0)
        {
            throw Reject(getterExpression);
        }

        body = TrimSuppressions(body[(arrow + 2)..]);
        int dot = body.IndexOf('.');
        if (dot < 0)
        {
            throw Reject(getterExpression);
        }

        var owner = TrimSuppressions(body[..dot]);
        var member = body[(dot + 1)..].Trim();
        if (!IsIdentifier(owner) || !IsIdentifier(member))
        {
            throw Reject(getterExpression);
        }

        return member.ToString();
    }

    private static ReadOnlySpan<char> TrimSuppressions(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        while (text.Length > 0 && text[^1] == '!')
        {
            text = text[..^1].TrimEnd();
        }

        return text;
    }

    private static bool IsIdentifier(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        if (text[0] != '_' && text[0] != '@' && !char.IsLetter(text[0]))
        {
            return false;
        }

        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] != '_' && !char.IsLetterOrDigit(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static ArgumentException Reject(string? getterExpression)
        => new(
            $"A notifying binding getter must be a single member access such as 'x => x.Name', " +
            $"but was '{getterExpression}'. Split a multi-step path into separate ThenNotifying " +
            $"segments, move computation into a convert delegate, or build with an SDK new enough " +
            $"to run the MewUI binding path generator.");
}
