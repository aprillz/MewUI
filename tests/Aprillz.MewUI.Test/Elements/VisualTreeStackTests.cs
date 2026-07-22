using System.Collections;
using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Elements;

/// <summary>
/// A visitor that throws mid-traversal must not strand element references on the reused traversal
/// stack (which would leak them for the process lifetime).
/// </summary>
[TestClass]
public sealed class VisualTreeStackTests
{
    [TestMethod]
    public void Visit_WhenVisitorThrows_DoesNotStrandStack()
    {
        var root = new StackPanel().Children(new Border(), new Border(), new Border());

        int visits = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VisualTree.Visit(root, _ =>
            {
                visits++;
                if (visits == 2)
                {
                    throw new InvalidOperationException("boom");
                }
            }));

        Assert.AreEqual(0, CurrentThreadStackCount());
    }

    private static int CurrentThreadStackCount()
    {
        var field = typeof(VisualTree).GetField("_stack", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field);
        var stack = (ICollection?)field.GetValue(null);
        return stack?.Count ?? 0;
    }
}
