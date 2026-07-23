using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Computes a change-detection hash of a method body that ignores EnC metadata-token
/// reassignment, so an unedited method keeps a stable hash across unrelated Hot Reload
/// deltas while a real body edit changes it.
/// </summary>
internal static class IlNormalizer
{
    private static readonly Dictionary<short, OperandType> _operandTypes = BuildOperandTable();

    private const short TWO_BYTE_PREFIX = 0xFE;

    /// <summary>
    /// Returns a token-normalized hash of the method body, or <see langword="null"/> when the
    /// IL is unavailable. Compiler-generated member names have numeric ordinals stripped so
    /// references to renumbered lambdas stay stable.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    internal static byte[]? Hash(MethodBase? method)
    {
        if (method == null)
        {
            return null;
        }

        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            return null;
        }

        if (il == null)
        {
            return null;
        }

        var module = method.Module;
        Type[]? typeArgs = method.DeclaringType?.GetGenericArguments();
        Type[]? methodArgs = method is MethodInfo ? method.GetGenericArguments() : null;
        var builder = new StringBuilder(il.Length * 2);

        int offset = 0;
        while (offset < il.Length)
        {
            short opcode = il[offset++];
            if (opcode == TWO_BYTE_PREFIX && offset < il.Length)
            {
                opcode = (short)(0xFE00 | il[offset++]);
            }

            builder.Append(opcode.ToString("X")).Append(';');
            if (!_operandTypes.TryGetValue(opcode, out var operandType))
            {
                break; // Unknown opcode: stop conservatively (treats the rest as changed).
            }

            switch (operandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    builder.Append(il[offset]).Append(';');
                    offset += 1;
                    break;
                case OperandType.InlineVar:
                    builder.Append(BitConverter.ToUInt16(il, offset)).Append(';');
                    offset += 2;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                    builder.Append(BitConverter.ToInt32(il, offset)).Append(';');
                    offset += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    builder.Append(BitConverter.ToInt64(il, offset)).Append(';');
                    offset += 8;
                    break;
                case OperandType.InlineString:
                    builder.Append('s').Append(Resolve(() => module.ResolveString(BitConverter.ToInt32(il, offset)))).Append(';');
                    offset += 4;
                    break;
                case OperandType.InlineMethod:
                    builder.Append('m').Append(Resolve(() => Describe(module.ResolveMethod(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)))).Append(';');
                    offset += 4;
                    break;
                case OperandType.InlineField:
                    builder.Append('f').Append(Resolve(() => Describe(module.ResolveField(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)))).Append(';');
                    offset += 4;
                    break;
                case OperandType.InlineType:
                    builder.Append('t').Append(Resolve(() => module.ResolveType(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)?.FullName)).Append(';');
                    offset += 4;
                    break;
                case OperandType.InlineTok:
                    builder.Append('k').Append(Resolve(() => Describe(module.ResolveMember(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)))).Append(';');
                    offset += 4;
                    break;
                case OperandType.InlineSig:
                    builder.Append("sig;");
                    offset += 4;
                    break;
                case OperandType.InlineSwitch:
                    int caseCount = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    builder.Append("sw").Append(caseCount).Append(';');
                    for (int caseIndex = 0; caseIndex < caseCount; caseIndex++)
                    {
                        builder.Append(BitConverter.ToInt32(il, offset)).Append(',');
                        offset += 4;
                    }
                    break;
                default:
                    offset += 4;
                    break;
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string Resolve(Func<string?> resolver)
    {
        try
        {
            return resolver() ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    private static string? Describe(MemberInfo? member)
    {
        if (member == null)
        {
            return null;
        }

        string declaringName = member.DeclaringType?.FullName ?? string.Empty;
        string memberName = member.Name;

        // EnC can renumber compiler-generated lambdas/display classes; strip numeric ordinals
        // so a reference to a renumbered generated member does not read as a change.
        if (declaringName.Contains('<') || declaringName.Contains("DisplayClass") || memberName.Contains('<'))
        {
            declaringName = StripDigits(declaringName);
            memberName = StripDigits(memberName);
        }

        return string.Concat(declaringName, "::", memberName);
    }

    private static string StripDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (!char.IsDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static Dictionary<short, OperandType> BuildOperandTable()
    {
        var table = new Dictionary<short, OperandType>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opcode)
            {
                table[opcode.Value] = opcode.OperandType;
            }
        }

        return table;
    }
}
