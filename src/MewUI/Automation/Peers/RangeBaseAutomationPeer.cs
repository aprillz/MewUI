using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a range control: <see cref="Slider"/>, <see cref="NumericUpDown"/>, <see cref="ScrollBar"/>, and the
/// read-only <see cref="ProgressBar"/> and <see cref="ProgressRing"/>.
/// </summary>
internal sealed class RangeBaseAutomationPeer : UIElementAutomationPeer, IRangeValueProvider
{
    public RangeBaseAutomationPeer(RangeBase owner)
        : base(owner)
    {
    }

    private RangeBase OwnerRange => (RangeBase)Owner;

    protected override AutomationControlType GetControlTypeCore() => Owner switch
    {
        Slider => AutomationControlType.Slider,
        NumericUpDown => AutomationControlType.Spinner,
        ScrollBar => AutomationControlType.ScrollBar,
        _ => AutomationControlType.ProgressBar,
    };

    internal override bool IsLeaf => true;

    public double Value => OwnerRange.Value;

    public double Minimum => OwnerRange.Minimum;

    public double Maximum => OwnerRange.Maximum;

    public double SmallChange => OwnerRange.SmallChange;

    public double LargeChange => OwnerRange.LargeChange;

    /// <summary>A progress indicator only reports; the user does not set it.</summary>
    public bool IsReadOnly => Owner is ProgressBar or ProgressRing;

    public void SetValue(double value)
    {
        AutomationGuard.ThrowIfDisabled(this);
        AutomationGuard.ThrowIfReadOnly(IsReadOnly, this);
        if (value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The value must lie between {Minimum} and {Maximum}.");
        }

        Owner.CommitFromAutomation(RangeBase.ValueProperty, value);
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == RangeBase.ValueProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.RangeValueProperty, null, Value);
        }
        else if (property == RangeBase.MinimumProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.RangeMinimumProperty, null, Minimum);
        }
        else if (property == RangeBase.MaximumProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.RangeMaximumProperty, null, Maximum);
        }

        base.OnOwnerPropertyChanged(property);
    }
}
