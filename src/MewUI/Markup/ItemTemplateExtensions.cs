namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent item-template overloads that hand the bind callback the template root as the type the
/// build callback returned, so the callback needs no cast.
/// </summary>
public static class ItemTemplateExtensions
{
    private static DelegateTemplate<TItem> Typed<TItem, TElement>(
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(bind);

        if (unbind == null)
        {
            return new DelegateTemplate<TItem>(
                build,
                (view, item, index, context) => bind((TElement)view, item, index, context));
        }
        else
        {
            return new DelegateTemplate<TItem>(
                build,
                (view, item, index, context) => bind((TElement)view, item, index, context),
                (view, item, index, context) => unbind((TElement)view, item, index, context));
        }
    }

    private static DelegateTemplate<TItem> Typed<TItem, TElement>(
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(bind);

        return new DelegateTemplate<TItem>(build, (view, item, _, _) => bind((TElement)view, item));
    }

    /// <summary>Sets the item template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ListBox ItemTemplate<TItem, TElement>(
        this ListBox listBox,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemTemplate = Typed(build, bind, unbind);
        return listBox;
    }

    /// <summary>Sets the item template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ListBox ItemTemplate<TItem, TElement>(
        this ListBox listBox,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemTemplate = Typed(build, bind);
        return listBox;
    }

    /// <summary>Sets the node template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static TreeView ItemTemplate<TItem, TElement>(
        this TreeView treeView,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.ItemTemplate = Typed(build, bind, unbind);
        return treeView;
    }

    /// <summary>Sets the node template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static TreeView ItemTemplate<TItem, TElement>(
        this TreeView treeView,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.ItemTemplate = Typed(build, bind);
        return treeView;
    }

    /// <summary>Sets the drop-down item template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ComboBox ItemTemplate<TItem, TElement>(
        this ComboBox comboBox,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemTemplate = Typed(build, bind, unbind);
        return comboBox;
    }

    /// <summary>Sets the drop-down item template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ComboBox ItemTemplate<TItem, TElement>(
        this ComboBox comboBox,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemTemplate = Typed(build, bind);
        return comboBox;
    }

    /// <summary>Sets the header template for the selected item; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ComboBox SelectedItemTemplate<TItem, TElement>(
        this ComboBox comboBox,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.SelectedItemTemplate = Typed(build, bind, unbind);
        return comboBox;
    }

    /// <summary>Sets the header template for the selected item from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ComboBox SelectedItemTemplate<TItem, TElement>(
        this ComboBox comboBox,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.SelectedItemTemplate = Typed(build, bind);
        return comboBox;
    }

    /// <summary>Sets the item template using delegate-based templating.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    public static ItemsControl ItemTemplate<TItem>(
        this ItemsControl itemsControl,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemTemplate = new DelegateTemplate<TItem>(build, bind, unbind);
        return itemsControl;
    }

    /// <summary>Sets the item template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ItemsControl ItemTemplate<TItem, TElement>(
        this ItemsControl itemsControl,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemTemplate = Typed(build, bind, unbind);
        return itemsControl;
    }

    /// <summary>Sets the item template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ItemsControl ItemTemplate<TItem, TElement>(
        this ItemsControl itemsControl,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemTemplate = Typed(build, bind);
        return itemsControl;
    }

    /// <summary>Sets the item template.</summary>
    public static NavigationList ItemTemplate(this NavigationList navigationList, IDataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(navigationList);
        ArgumentNullException.ThrowIfNull(template);

        navigationList.ItemTemplate = template;
        return navigationList;
    }

    /// <summary>Sets the item template using delegate-based templating.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    public static NavigationList ItemTemplate<TItem>(
        this NavigationList navigationList,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
        => ItemTemplate(navigationList, new DelegateTemplate<TItem>(build, bind, unbind));

    /// <summary>Sets the item template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static NavigationList ItemTemplate<TItem, TElement>(
        this NavigationList navigationList,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
        => ItemTemplate(navigationList, Typed(build, bind, unbind));

    /// <summary>Sets the item template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static NavigationList ItemTemplate<TItem, TElement>(
        this NavigationList navigationList,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
        => ItemTemplate(navigationList, Typed(build, bind));

    /// <summary>Sets the segment template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static SegmentedControl ItemTemplate<TItem, TElement>(
        this SegmentedControl control,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemTemplate = Typed(build, bind, unbind);
        return control;
    }

    /// <summary>Sets the segment template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static SegmentedControl ItemTemplate<TItem, TElement>(
        this SegmentedControl control,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemTemplate = Typed(build, bind);
        return control;
    }

    /// <summary>Sets the segment template; the bind callback receives the root the build callback returned.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ButtonGroup ItemTemplate<TItem, TElement>(
        this ButtonGroup control,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind,
        Action<TElement, TItem, int, TemplateContext>? unbind = null)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemTemplate = Typed(build, bind, unbind);
        return control;
    }

    /// <summary>Sets the segment template from a root element and a bind callback that needs only the item.</summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    public static ButtonGroup ItemTemplate<TItem, TElement>(
        this ButtonGroup control,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind)
        where TElement : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemTemplate = Typed(build, bind);
        return control;
    }
}
