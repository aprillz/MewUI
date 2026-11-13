using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Aprillz.MewUI.Platform.Win32.Automation;

// UIAutomationCore.h provider interfaces in header vtable order; values cross raw so nothing needs runtime marshalling.

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    [PreserveSig] int GetProviderOptions(out int options);

    [PreserveSig] int GetPatternProvider(int patternId, out IRawElementProviderSimple? provider);

    [PreserveSig] int GetPropertyValue(int propertyId, out UiaVariant value);

    [PreserveSig] int GetHostRawElementProvider(out nint provider);
}

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    [PreserveSig] int Navigate(int direction, out IRawElementProviderFragment? element);

    [PreserveSig] int GetRuntimeId(out nint runtimeId);

    [PreserveSig] int GetBoundingRectangle(out UiaRect bounds);

    [PreserveSig] int GetEmbeddedFragmentRoots(out nint roots);

    [PreserveSig] int SetFocus();

    [PreserveSig] int GetFragmentRoot(out IRawElementProviderFragmentRoot? root);
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    [PreserveSig] int ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? element);

    [PreserveSig] int GetFocus(out IRawElementProviderFragment? element);
}

[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IUiaInvokeProvider
{
    [PreserveSig] int Invoke();
}

[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IUiaToggleProvider
{
    [PreserveSig] int Toggle();

    [PreserveSig] int GetToggleState(out int state);
}

[GeneratedComInterface]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IUiaValueProvider
{
    [PreserveSig] int SetValue(nint value);

    [PreserveSig] int GetValue(out nint value);

    [PreserveSig] int GetIsReadOnly(out int isReadOnly);
}

[GeneratedComInterface]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IUiaRangeValueProvider
{
    [PreserveSig] int SetValue(double value);

    [PreserveSig] int GetValue(out double value);

    [PreserveSig] int GetIsReadOnly(out int isReadOnly);

    [PreserveSig] int GetMaximum(out double maximum);

    [PreserveSig] int GetMinimum(out double minimum);

    [PreserveSig] int GetLargeChange(out double change);

    [PreserveSig] int GetSmallChange(out double change);
}

[GeneratedComInterface]
[Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
internal partial interface IUiaSelectionProvider
{
    [PreserveSig] int GetSelection(out nint selection);

    [PreserveSig] int GetCanSelectMultiple(out int canSelectMultiple);

    [PreserveSig] int GetIsSelectionRequired(out int isSelectionRequired);
}

[GeneratedComInterface]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface IUiaSelectionItemProvider
{
    [PreserveSig] int Select();

    [PreserveSig] int AddToSelection();

    [PreserveSig] int RemoveFromSelection();

    [PreserveSig] int GetIsSelected(out int isSelected);

    [PreserveSig] int GetSelectionContainer(out IRawElementProviderSimple? container);
}

[GeneratedComInterface]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IUiaExpandCollapseProvider
{
    [PreserveSig] int Expand();

    [PreserveSig] int Collapse();

    [PreserveSig] int GetExpandCollapseState(out int state);
}

[GeneratedComInterface]
[Guid("b38b8077-1fc3-42a5-8cae-d40c2215055a")]
internal partial interface IUiaScrollProvider
{
    [PreserveSig] int Scroll(int horizontalAmount, int verticalAmount);

    [PreserveSig] int SetScrollPercent(double horizontalPercent, double verticalPercent);

    [PreserveSig] int GetHorizontalScrollPercent(out double percent);

    [PreserveSig] int GetVerticalScrollPercent(out double percent);

    [PreserveSig] int GetHorizontalViewSize(out double size);

    [PreserveSig] int GetVerticalViewSize(out double size);

    [PreserveSig] int GetHorizontallyScrollable(out int scrollable);

    [PreserveSig] int GetVerticallyScrollable(out int scrollable);
}

[GeneratedComInterface]
[Guid("2360c714-4bf1-4b26-ba65-9b21316127eb")]
internal partial interface IUiaScrollItemProvider
{
    [PreserveSig] int ScrollIntoView();
}

[GeneratedComInterface]
[Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface IUiaTextProvider
{
    [PreserveSig] int GetSelection(out nint ranges);

    [PreserveSig] int GetVisibleRanges(out nint ranges);

    [PreserveSig] int RangeFromChild(nint childElement, out IUiaTextRangeProvider? range);

    [PreserveSig] int RangeFromPoint(UiaPoint point, out IUiaTextRangeProvider? range);

    [PreserveSig] int GetDocumentRange(out IUiaTextRangeProvider? range);

    [PreserveSig] int GetSupportedTextSelection(out int selection);
}

[GeneratedComInterface]
[Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface IUiaTextRangeProvider
{
    [PreserveSig] int Clone(out IUiaTextRangeProvider? clone);

    [PreserveSig] int Compare(IUiaTextRangeProvider? range, out int same);

    [PreserveSig] int CompareEndpoints(int endpoint, IUiaTextRangeProvider? targetRange, int targetEndpoint, out int comparison);

    [PreserveSig] int ExpandToEnclosingUnit(int unit);

    [PreserveSig] int FindAttribute(int attributeId, UiaVariant value, int backward, out IUiaTextRangeProvider? found);

    [PreserveSig] int FindText(nint text, int backward, int ignoreCase, out IUiaTextRangeProvider? found);

    [PreserveSig] int GetAttributeValue(int attributeId, out UiaVariant value);

    [PreserveSig] int GetBoundingRectangles(out nint rectangles);

    [PreserveSig] int GetEnclosingElement(out IRawElementProviderSimple? element);

    [PreserveSig] int GetText(int maxLength, out nint text);

    [PreserveSig] int Move(int unit, int count, out int moved);

    [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);

    [PreserveSig] int MoveEndpointByRange(int endpoint, IUiaTextRangeProvider? targetRange, int targetEndpoint);

    [PreserveSig] int Select();

    [PreserveSig] int AddToSelection();

    [PreserveSig] int RemoveFromSelection();

    [PreserveSig] int ScrollIntoView(int alignToTop);

    [PreserveSig] int GetChildren(out nint children);
}
