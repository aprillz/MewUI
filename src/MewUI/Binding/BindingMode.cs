namespace Aprillz.MewUI;

/// <summary>
/// Specifies the direction of data flow in a binding.
/// </summary>
public enum BindingMode
{
    /// <summary>Changes flow from the source to the target only.</summary>
    OneWay,

    /// <summary>Changes flow in both directions between the source and the target.</summary>
    TwoWay,
}
