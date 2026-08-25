using System.Reflection;

using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// Gates the rebuilt text input hierarchy against the frozen legacy public surface
/// (agent/textBase/plan.md): additions are allowed, removals require an explicit
/// per-symbol decision recorded in the plan's Breaking Changes.
/// </summary>
[TestClass]
public sealed class LegacyTextSurfaceSnapshotTests
{
    [TestMethod]
    public void TextBox_CoversLegacyPublicSurface()
    {
        var chain = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in new[] { typeof(TextBox), typeof(SingleLineTextBase), typeof(TextBase) })
        {
            foreach (var entry in GetDeclaredSurface(type, publicOnly: true))
            {
                chain.Add(StripInheritanceModifiers(entry));
            }
        }

        var missing = _textBoxPublicSurface.Where(entry => !chain.Contains(entry)).ToList();
        Assert.IsTrue(missing.Count == 0,
            $"Rebuilt TextBox chain lost legacy public surface.\nMissing:\n  {string.Join("\n  ", missing)}");
    }

    private static string StripInheritanceModifiers(string entry)
        => entry.Replace(":abstract", "").Replace(":virtual", "");

    [TestMethod]
    public void PasswordBox_CoversLegacyPublicSurface()
    {
        var chain = GetPublicChainSurface(typeof(PasswordBox), typeof(SingleLineTextBase), typeof(TextBase));

        var missing = _passwordBoxPublicSurface.Where(entry => !chain.Contains(entry)).ToList();
        Assert.IsTrue(missing.Count == 0,
            $"Rebuilt PasswordBox chain lost legacy public surface.\nMissing:\n  {string.Join("\n  ", missing)}");

        // Deferral design: the password surface must never expose document text publicly.
        Assert.IsFalse(chain.Any(entry => entry.StartsWith("P:Text:", StringComparison.Ordinal)),
            "PasswordBox chain must not expose a public Text property");
        Assert.IsFalse(chain.Any(entry => entry.StartsWith("P:SelectedText:", StringComparison.Ordinal)),
            "PasswordBox chain must not expose a public SelectedText property");
        Assert.IsFalse(chain.Any(entry => entry.StartsWith("F:TextProperty:", StringComparison.Ordinal)),
            "PasswordBox chain must not expose a public TextProperty field");
    }

    private static HashSet<string> GetPublicChainSurface(params Type[] chainTypes)
    {
        var chain = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in chainTypes)
        {
            foreach (var entry in GetDeclaredSurface(type, publicOnly: true))
            {
                chain.Add(StripInheritanceModifiers(entry));
            }
        }
        return chain;
    }

    /// <summary>
    /// Formats the declared public/protected members of a type into stable snapshot entries.
    /// Declaring-type names are excluded so the entries survive class renames.
    /// </summary>
    internal static HashSet<string> GetDeclaredSurface(Type type, bool publicOnly = false)
    {
        const BindingFlags FLAGS = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var entries = new HashSet<string>(StringComparer.Ordinal);

        bool Visible(MethodBase? method) => method != null && (publicOnly
            ? method.IsPublic
            : method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);
        static string TypeName(Type memberType) => memberType.IsGenericType
            ? $"{memberType.Name.Split('`')[0]}<{string.Join(",", memberType.GetGenericArguments().Select(TypeName))}>"
            : memberType.Name;
        static string Mod(MethodBase method) => method.IsAbstract ? ":abstract" : (method.IsVirtual && !method.IsFinal ? ":virtual" : "");
        static string ParameterList(MethodBase method) => string.Join(",", method.GetParameters().Select(parameter => TypeName(parameter.ParameterType)));

        foreach (var field in type.GetFields(FLAGS))
        {
            bool fieldVisible = publicOnly
                ? field.IsPublic
                : field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
            if (!fieldVisible) continue;
            entries.Add($"F:{field.Name}:{TypeName(field.FieldType)}");
        }

        foreach (var constructor in type.GetConstructors(FLAGS))
        {
            if (!Visible(constructor)) continue;
            entries.Add($"C:({ParameterList(constructor)})");
        }

        foreach (var property in type.GetProperties(FLAGS))
        {
            var hasGet = Visible(property.GetMethod);
            var hasSet = Visible(property.SetMethod);
            if (!hasGet && !hasSet) continue;
            var accessor = hasGet ? property.GetMethod! : property.SetMethod!;
            entries.Add($"P:{property.Name}:{TypeName(property.PropertyType)}:{(hasGet ? "get" : "")}{(hasSet ? "set" : "")}{Mod(accessor)}");
        }

        foreach (var eventInfo in type.GetEvents(FLAGS))
        {
            if (!Visible(eventInfo.AddMethod)) continue;
            entries.Add($"E:{eventInfo.Name}:{TypeName(eventInfo.EventHandlerType!)}");
        }

        foreach (var method in type.GetMethods(FLAGS))
        {
            if (!Visible(method) || method.IsSpecialName) continue;
            entries.Add($"M:{method.Name}({ParameterList(method)}):{TypeName(method.ReturnType)}{Mod(method)}");
        }

        return entries;
    }

    // Legacy public surface of TextBox and its legacy base chain, minus decided removals
    // (WrapChanged: multiline-only; see agent/textBase/plan.md Breaking Changes).
    // Inheritance modifiers are stripped because virtual-ness is free to change in the rebuild.
    private static readonly string[] _textBoxPublicSurface =
    {
        "C:()",
        "E:TextChanged:Action<String>",
        "E:TextCompositionEnd:Action<TextCompositionEventArgs>",
        "E:TextCompositionStart:Action<TextCompositionEventArgs>",
        "E:TextCompositionUpdate:Action<TextCompositionEventArgs>",
        "E:TextInput:Action<TextInputEventArgs>",
        "F:AcceptTabProperty:MewProperty<Boolean>",
        "F:ImeModeProperty:MewProperty<ImeMode>",
        "F:IsReadOnlyProperty:MewProperty<Boolean>",
        "F:MaxLengthProperty:MewProperty<Int32>",
        "F:PlaceholderProperty:MewProperty<String>",
        "F:SelectionLengthProperty:MewProperty<Int32>",
        "F:SelectionStartProperty:MewProperty<Int32>",
        "F:TextProperty:MewProperty<String>",
        "M:AppendText(String,Boolean):Void",
        "M:Copy():Void",
        "M:Cut():Void",
        "M:GetCharRectInWindow(Int32):Rect",
        "M:Paste():Void",
        "M:Redo():Void",
        "M:ScrollToCaret():Void",
        "M:SelectAll():Void",
        "M:Undo():Void",
        "P:AcceptTab:Boolean:getset",
        "P:CanRedo:Boolean:get",
        "P:CanUndo:Boolean:get",
        "P:CaretPosition:Int32:getset",
        "P:ImeMode:ImeMode:getset",
        "P:IsReadOnly:Boolean:getset",
        "P:MaxLength:Int32:getset",
        "P:Placeholder:String:getset",
        "P:SelectedText:String:get",
        "P:SelectionLength:Int32:get",
        "P:SelectionStart:Int32:get",
        "P:Text:String:getset",
    };

    // Legacy public surface of PasswordBox and its legacy base chain, minus decided removals
    // (SelectedText and WrapChanged; see agent/textBase/plan.md Breaking Changes).
    private static readonly string[] _passwordBoxPublicSurface =
    {
        "C:()",
        "E:PasswordChanged:Action",
        "E:TextCompositionEnd:Action<TextCompositionEventArgs>",
        "E:TextCompositionStart:Action<TextCompositionEventArgs>",
        "E:TextCompositionUpdate:Action<TextCompositionEventArgs>",
        "E:TextInput:Action<TextInputEventArgs>",
        "F:AcceptTabProperty:MewProperty<Boolean>",
        "F:ImeModeProperty:MewProperty<ImeMode>",
        "F:IsReadOnlyProperty:MewProperty<Boolean>",
        "F:MaxLengthProperty:MewProperty<Int32>",
        "F:PasswordCharProperty:MewProperty<Char>",
        "F:PasswordProperty:MewProperty<String>",
        "F:PlaceholderProperty:MewProperty<String>",
        "F:SelectionLengthProperty:MewProperty<Int32>",
        "F:SelectionStartProperty:MewProperty<Int32>",
        "M:AppendText(String,Boolean):Void",
        "M:Copy():Void",
        "M:Cut():Void",
        "M:GetCharRectInWindow(Int32):Rect",
        "M:Paste():Void",
        "M:Redo():Void",
        "M:ScrollToCaret():Void",
        "M:SelectAll():Void",
        "M:Undo():Void",
        "P:AcceptTab:Boolean:getset",
        "P:CanRedo:Boolean:get",
        "P:CanUndo:Boolean:get",
        "P:CaretPosition:Int32:getset",
        "P:ImeMode:ImeMode:getset",
        "P:IsReadOnly:Boolean:getset",
        "P:MaxLength:Int32:getset",
        "P:Password:String:getset",
        "P:PasswordChar:Char:getset",
        "P:Placeholder:String:getset",
        "P:SelectionLength:Int32:get",
        "P:SelectionStart:Int32:get",
    };
}
