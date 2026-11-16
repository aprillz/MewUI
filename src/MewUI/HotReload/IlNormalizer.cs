using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Reads method bodies for Hot Reload: a change-detection hash that ignores EnC metadata-token
/// reassignment, and the methods a body runs while it builds UI.
/// </summary>
internal static class IlNormalizer
{
    private static readonly Dictionary<short, OperandType> _operandTypes = BuildOperandTable();

    private const short TWO_BYTE_PREFIX = 0xFE;
    private const short OPCODE_CALL = 0x28;
    private const short OPCODE_CALLVIRT = 0x6F;
    private const short OPCODE_NEWOBJ = 0x73;
    private const short OPCODE_LDFTN = unchecked((short)0xFE06);
    private const short OPCODE_LDVIRTFTN = unchecked((short)0xFE07);

    // How far past a delegate creation to look for the call that receives the delegate; covers the
    // cached-lambda sequence (newobj, dup, stsfld) the compiler emits before the consuming call.
    private const int DELEGATE_CONSUMER_LOOKAHEAD = 6;

    private readonly record struct Instruction(short OpCode, OperandType OperandType, int OperandOffset);

    /// <summary>
    /// Returns a token-normalized hash of the method body, or <see langword="null"/> when the
    /// IL is unavailable. Compiler-generated member names have numeric ordinals stripped so
    /// references to renumbered lambdas stay stable.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    internal static byte[]? Hash(MethodBase? method)
    {
        var il = ReadIl(method);
        if (il == null)
        {
            return null;
        }

        var module = method!.Module;
        Type[]? typeArgs = method.DeclaringType?.GetGenericArguments();
        Type[]? methodArgs = method is MethodInfo ? method.GetGenericArguments() : null;
        var builder = new StringBuilder(il.Length * 2);

        foreach (var instruction in Decode(il))
        {
            builder.Append(instruction.OpCode.ToString("X")).Append(';');
            int offset = instruction.OperandOffset;
            switch (instruction.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    builder.Append(il[offset]).Append(';');
                    break;
                case OperandType.InlineVar:
                    builder.Append(BitConverter.ToUInt16(il, offset)).Append(';');
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                    builder.Append(BitConverter.ToInt32(il, offset)).Append(';');
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    builder.Append(BitConverter.ToInt64(il, offset)).Append(';');
                    break;
                case OperandType.InlineString:
                    builder.Append('s').Append(Resolve(() => module.ResolveString(BitConverter.ToInt32(il, offset)))).Append(';');
                    break;
                case OperandType.InlineMethod:
                    builder.Append('m').Append(Resolve(() => Describe(module.ResolveMethod(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)))).Append(';');
                    break;
                case OperandType.InlineField:
                    builder.Append('f').Append(Resolve(() => Describe(module.ResolveField(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)))).Append(';');
                    break;
                case OperandType.InlineType:
                    builder.Append('t').Append(Resolve(() => module.ResolveType(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)?.FullName)).Append(';');
                    break;
                case OperandType.InlineTok:
                    builder.Append('k').Append(Resolve(() => Describe(module.ResolveMember(BitConverter.ToInt32(il, offset), typeArgs, methodArgs)))).Append(';');
                    break;
                case OperandType.InlineSig:
                    builder.Append("sig;");
                    break;
                case OperandType.InlineSwitch:
                    int caseCount = BitConverter.ToInt32(il, offset);
                    builder.Append("sw").Append(caseCount).Append(';');
                    for (int caseIndex = 0; caseIndex < caseCount; caseIndex++)
                    {
                        builder.Append(BitConverter.ToInt32(il, offset + 4 + caseIndex * 4)).Append(',');
                    }
                    break;
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    /// <summary>
    /// Adds to <paramref name="edges"/> the methods <paramref name="method"/> calls or constructs with
    /// and the delegate targets it creates, except delegates that go to an event subscription
    /// (<c>add_X</c> or an <c>OnX</c> markup extension), which go to <paramref name="handlerEdges"/>:
    /// those bodies run later and are replaced in place, so editing them needs no rebuild.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    internal static void CollectBuildEdges(MethodBase method, List<MethodBase> edges, List<MethodBase> handlerEdges)
    {
        var il = ReadIl(method);
        if (il == null)
        {
            return;
        }

        var module = method.Module;
        Type[]? typeArgs = method.DeclaringType?.GetGenericArguments();
        Type[]? methodArgs = method is MethodInfo ? method.GetGenericArguments() : null;
        var instructions = Decode(il);

        for (int index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            bool isCall = instruction.OpCode is OPCODE_CALL or OPCODE_CALLVIRT or OPCODE_NEWOBJ;
            bool isDelegateTarget = instruction.OpCode is OPCODE_LDFTN or OPCODE_LDVIRTFTN;
            if (!isCall && !isDelegateTarget)
            {
                continue;
            }

            var target = ResolveMethod(module, il, instruction, typeArgs, methodArgs);
            if (target == null)
            {
                continue;
            }

            if (isDelegateTarget && GoesToEventSubscription(module, il, instructions, index, typeArgs, methodArgs))
            {
                handlerEdges.Add(target);
            }
            else
            {
                edges.Add(target);
            }
        }
    }

    private static bool GoesToEventSubscription(Module module, byte[] il, List<Instruction> instructions, int delegateIndex,
        Type[]? typeArgs, Type[]? methodArgs)
    {
        int last = Math.Min(instructions.Count - 1, delegateIndex + DELEGATE_CONSUMER_LOOKAHEAD);
        for (int index = delegateIndex + 1; index <= last; index++)
        {
            var instruction = instructions[index];
            if (instruction.OpCode is not (OPCODE_CALL or OPCODE_CALLVIRT or OPCODE_NEWOBJ))
            {
                continue;
            }

            var consumer = ResolveMethod(module, il, instruction, typeArgs, methodArgs);
            if (consumer == null)
            {
                return false;
            }
            if (consumer.IsConstructor && typeof(Delegate).IsAssignableFrom(consumer.DeclaringType))
            {
                continue;
            }

            string name = consumer.Name;
            return name.StartsWith("add_", StringComparison.Ordinal)
                || (name.Length > 2 && name.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(name[2]));
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    private static MethodBase? ResolveMethod(Module module, byte[] il, Instruction instruction, Type[]? typeArgs, Type[]? methodArgs)
    {
        try
        {
            return module.ResolveMethod(BitConverter.ToInt32(il, instruction.OperandOffset), typeArgs, methodArgs);
        }
        catch
        {
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    private static byte[]? ReadIl(MethodBase? method)
    {
        if (method == null)
        {
            return null;
        }

        try
        {
            return method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            return null;
        }
    }

    private static List<Instruction> Decode(byte[] il)
    {
        var instructions = new List<Instruction>();
        int offset = 0;
        while (offset < il.Length)
        {
            short opcode = il[offset++];
            if (opcode == TWO_BYTE_PREFIX && offset < il.Length)
            {
                opcode = (short)(0xFE00 | il[offset++]);
            }

            if (!_operandTypes.TryGetValue(opcode, out var operandType))
            {
                // Malformed IL: the rest cannot be decoded, so it is left out of the hash and the edges.
                break;
            }

            int operandSize = OperandSize(operandType, il, offset);
            if (offset + operandSize > il.Length)
            {
                break;
            }

            instructions.Add(new Instruction(opcode, operandType, offset));
            offset += operandSize;
        }

        return instructions;
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => offset + 4 <= il.Length ? 4 + BitConverter.ToInt32(il, offset) * 4 : 4,
        _ => 4,
    };

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
