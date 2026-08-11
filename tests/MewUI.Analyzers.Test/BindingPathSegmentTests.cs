using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class BindingPathSegmentTests
{
    // A self-contained mirror of the binding path surface so the rules are exercised
    // without depending on MewUI. PlainSettings is the "owner does not notify" case.
    private const string BindingApi = """

        namespace Aprillz.MewUI
        {
            public sealed class ObservableValue<T>
            {
                public T Value { get; set; }
            }

            public static class BindingPath
            {
                public static BindingPath<TRoot, TRoot> From<TRoot>() where TRoot : class => null;
            }

            public sealed class BindingPath<TRoot, TValue> where TRoot : class
            {
                public BindingPath<TRoot, TNext> Then<TNext>(System.Func<TValue, TNext> getter) => null;

                public BindingPath<TRoot, TNext> Then<TNext>(
                    System.Func<TValue, ObservableValue<TNext>> selector) => null;
            }

            public static class InpcBindingPathExtensions
            {
                public static BindingPath<TRoot, TNext> ThenNotifying<TRoot, TOwner, TNext>(
                    this BindingPath<TRoot, TOwner> path,
                    System.Func<TOwner, TNext> getter,
                    System.Action<TOwner, TNext> setter = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(getter))]
                    string getterExpression = null)
                    where TRoot : class
                    where TOwner : class, System.ComponentModel.INotifyPropertyChanged => null;
            }
        }

        public class Person : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            public string Name { get; set; }
            public Profile Profile { get; set; }
            public Aprillz.MewUI.ObservableValue<int> Zoom { get; set; }
        }

        public class Profile : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            public string DisplayName { get; set; }
        }

        public class PlainSettings
        {
            public int Level { get; set; }
        }
        """;

    [TestMethod]
    public async Task ReplacesThen_WhenOwnerRaisesPropertyChanged()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>().{|MEW1201:Then|}(x => x.Name);
            }
            """ + BindingApi;

        var fixedSource = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>().ThenNotifying(x => x.Name);
            }
            """ + BindingApi;

        await VerifyAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenOwnerDoesNotNotify()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<PlainSettings>().Then(x => x.Level);
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenSegmentSelectsAnObservableValue()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>().Then(x => x.Zoom);
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenGetterIsNotASingleMemberAccess()
    {
        // Nothing to observe on the owner, so the non-observing segment stands.
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>().Then(x => x.Profile.DisplayName);
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    [TestMethod]
    public async Task ReportsNotifyingGetter_WhenItIsMultiStep()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>()
                    .ThenNotifying({|MEW1202:x => x.Profile.DisplayName|});
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    [TestMethod]
    public async Task ReportsNotifyingGetter_WhenItComputes()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>()
                    .ThenNotifying({|MEW1202:x => x.Name + "!"|});
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenNotifyingGetterIsASingleMemberAccess()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>().ThenNotifying(x => x.Name);
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenNotifyingGetterSuppressesNull()
    {
        var source = """
            using Aprillz.MewUI;

            class C
            {
                object M() => BindingPath.From<Person>().ThenNotifying(x => x.Profile!);
            }
            """ + BindingApi;

        await VerifyAsync(source, source);
    }

    // Analyzer-only cases pass the same text as both arguments: nothing must change.
    private static async Task VerifyAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<BindingPathSegmentAnalyzer, NonObservingThenCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CompilerDiagnostics = CompilerDiagnostics.Errors,
        };

        await test.RunAsync();
    }
}
