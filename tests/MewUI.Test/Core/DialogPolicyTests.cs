using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

[TestClass]
public sealed class DialogPolicyTests
{
    [TestMethod]
    public void FileDialogOptions_PreferNativeByDefault()
    {
        Assert.IsTrue(new OpenFileDialogOptions().PreferNative);
        Assert.IsTrue(new SaveFileDialogOptions().PreferNative);
        Assert.IsTrue(new FolderDialogOptions().PreferNative);
    }

    [TestMethod]
    public void FileDialog_ManagedSelectionTreatsNativeAsPreference()
    {
        Assert.IsTrue(FileDialog.ShouldUseManaged(preferNative: false, nativeAvailable: false));
        Assert.IsTrue(FileDialog.ShouldUseManaged(preferNative: false, nativeAvailable: true));
        Assert.IsTrue(FileDialog.ShouldUseManaged(preferNative: true, nativeAvailable: false));
        Assert.IsFalse(FileDialog.ShouldUseManaged(preferNative: true, nativeAvailable: true));
    }

    [TestMethod]
    public void NativeMessageBox_MapsEveryIconToManagedPrompt()
    {
        Assert.AreEqual(PromptIconKind.None, NativeMessageBox.ToManagedIcon(NativeMessageBoxIcon.None));
        Assert.AreEqual(PromptIconKind.Info, NativeMessageBox.ToManagedIcon(NativeMessageBoxIcon.Information));
        Assert.AreEqual(PromptIconKind.Warning, NativeMessageBox.ToManagedIcon(NativeMessageBoxIcon.Warning));
        Assert.AreEqual(PromptIconKind.Error, NativeMessageBox.ToManagedIcon(NativeMessageBoxIcon.Error));
        Assert.AreEqual(PromptIconKind.Question, NativeMessageBox.ToManagedIcon(NativeMessageBoxIcon.Question));
    }

    [TestMethod]
    public void NativeMessageBox_MapsEveryButtonSetToManagedPrompt()
    {
        CollectionAssert.AreEqual(
            new[] { MessageButtonRole.Accept },
            NativeMessageBox.ToManagedButtons(NativeMessageBoxButtons.Ok).Select(static x => x.Role).ToArray());
        CollectionAssert.AreEqual(
            new[] { MessageButtonRole.Accept, MessageButtonRole.Reject },
            NativeMessageBox.ToManagedButtons(NativeMessageBoxButtons.OkCancel).Select(static x => x.Role).ToArray());
        CollectionAssert.AreEqual(
            new[] { MessageButtonRole.Accept, MessageButtonRole.Destructive },
            NativeMessageBox.ToManagedButtons(NativeMessageBoxButtons.YesNo).Select(static x => x.Role).ToArray());
        CollectionAssert.AreEqual(
            new[] { MessageButtonRole.Accept, MessageButtonRole.Destructive, MessageButtonRole.Reject },
            NativeMessageBox.ToManagedButtons(NativeMessageBoxButtons.YesNoCancel).Select(static x => x.Role).ToArray());
    }
}
