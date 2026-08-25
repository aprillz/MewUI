using System.Reflection;
using System.Runtime.CompilerServices;
using Aprillz.MewUI;

namespace MewUI.Test.Core;

/// <summary>
/// The core's internals are an in-box contract: only framework-owned assemblies that ship and version
/// with it may see them. This fails if an InternalsVisibleTo grant names a non-framework assembly.
/// </summary>
[TestClass]
public sealed class InternalsVisibleToAllowlistTests
{
    [TestMethod]
    public void CoreExposesInternalsOnlyToFrameworkAssemblies()
    {
        var core = typeof(Application).Assembly;
        var friends = core
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
            .ToList();

        Assert.IsGreaterThan(0, friends.Count);
        foreach (var friend in friends)
        {
            Assert.IsTrue(
                friend.StartsWith("Aprillz.MewUI.", StringComparison.Ordinal),
                $"InternalsVisibleTo exposes core internals to a non-framework assembly: {friend}");
        }
    }
}
