namespace Aprillz.MewUI.Controls;

/// <summary>
/// A reusable definition of a control's visual tree. One definition can be applied to any
/// number of controls; each application builds an independent tree.
/// </summary>
public abstract class ControlTemplate
{
    /// <summary>
    /// Builds the visual tree for one control instance, registering named parts into
    /// <paramref name="context"/>, and returns the visual root.
    /// </summary>
    /// <param name="owner">The control the tree is being built for.</param>
    /// <param name="context">The per-instance part registry.</param>
    public abstract Element Build(Control owner, ControlTemplateContext context);
}

/// <summary>
/// A <see cref="ControlTemplate"/> defined by a build delegate.
/// </summary>
/// <typeparam name="TControl">The control type the template targets.</typeparam>
public sealed class DelegateControlTemplate<TControl> : ControlTemplate where TControl : Control
{
    private readonly Func<TControl, ControlTemplateContext, Element> _build;

    public DelegateControlTemplate(Func<TControl, ControlTemplateContext, Element> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    /// <inheritdoc/>
    public override Element Build(Control owner, ControlTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(context);

        if (owner is not TControl typedOwner)
        {
            throw new InvalidOperationException(
                $"The template targets {typeof(TControl).Name} but was applied to {owner.GetType().Name}.");
        }

        return _build(typedOwner, context);
    }
}

/// <summary>
/// Per-control state of an applied template: the built visual root and its part registry.
/// Owned by the control; discarded when the template is replaced or cleared.
/// </summary>
internal sealed class ControlTemplateInstance
{
    public required Element VisualRoot { get; init; }
    public required ControlTemplateContext Context { get; init; }
}
