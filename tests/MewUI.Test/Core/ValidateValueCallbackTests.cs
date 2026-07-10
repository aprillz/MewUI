using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

[TestClass]
public sealed class ValidateValueCallbackTests
{
    private sealed class GuardedOwner : ContentControl
    {
        public static readonly MewProperty<string?> GuardedProperty =
            MewProperty<string?>.Register<GuardedOwner>(nameof(Guarded), null,
                MewPropertyOptions.None,
                static (self, oldValue, newValue) => self.ChangedCount++,
                validate: static (self, value) =>
                {
                    self.ValidateCount++;
                    if (value == "invalid")
                    {
                        throw new InvalidOperationException("Rejected by validate.");
                    }
                });

        public string? Guarded
        {
            get => GetValue(GuardedProperty);
            set => SetValue(GuardedProperty, value);
        }

        public int ValidateCount { get; set; }
        public int ChangedCount { get; set; }
    }

    [TestMethod]
    public void Validate_RejectingThrow_LeavesStoreUnchanged()
    {
        var owner = new GuardedOwner { Guarded = "first" };
        int changesBeforeRejection = owner.ChangedCount;

        Assert.ThrowsExactly<InvalidOperationException>(() => owner.Guarded = "invalid");

        Assert.AreEqual("first", owner.Guarded, "rejected value must not be committed");
        Assert.AreEqual(changesBeforeRejection, owner.ChangedCount, "changed callback must not run for a rejected set");
    }

    [TestMethod]
    public void Validate_RejectsOnBindingPathToo()
    {
        var owner = new GuardedOwner { Guarded = "first" };

        // Bindings and forwards bypass the CLR setter and write through SetLocal.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => owner.PropertyStore.SetLocal(GuardedOwner.GuardedProperty, "invalid"));

        Assert.AreEqual("first", owner.Guarded);
    }

    [TestMethod]
    public void Validate_RunsForNull()
    {
        var owner = new GuardedOwner { Guarded = "first" };
        int validationsBeforeNull = owner.ValidateCount;

        owner.Guarded = null;

        Assert.AreEqual(validationsBeforeNull + 1, owner.ValidateCount, "null must pass through validate, unlike coerce");
        Assert.IsNull(owner.Guarded);
    }

    [TestMethod]
    public void Validate_SkippedWhenValueUnchanged()
    {
        var owner = new GuardedOwner { Guarded = "first" };
        int validationsAfterFirstSet = owner.ValidateCount;

        owner.Guarded = "first";

        Assert.AreEqual(validationsAfterFirstSet, owner.ValidateCount, "same-value set is a no-op and must not validate");
    }
}
