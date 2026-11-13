using Aprillz.MewUI.Automation;

using Tmds.DBus.Protocol;

namespace Aprillz.MewUI.Platform.Linux.Automation;

internal sealed partial class AtSpiServer
{
    private const int COORD_SCREEN = 0;
    private const int COORD_WINDOW = 1;
    private const uint LAYER_WIDGET = 3;
    private const uint LAYER_WINDOW = 7;

    /// <summary>Answers one request on the UI thread. A request for an object that is gone fails as an unknown object.</summary>
    private void Dispatch(MethodContext context)
    {
        var request = context.Request;
        string path = request.PathAsString ?? string.Empty;
        string @interface = request.InterfaceAsString ?? string.Empty;
        string member = request.MemberAsString ?? string.Empty;
        bool isRoot = path == ROOT_PATH;
        var node = isRoot ? null : NodeAt(path);
        if (!isRoot && (node == null || !IsShown(node.Peer)))
        {
            context.ReplyError("org.freedesktop.DBus.Error.UnknownObject", $"No object at {path}.");
            return;
        }

        var reader = request.GetBodyReader();
        switch (@interface)
        {
            case PROPERTIES:
                HandleProperties(context, node, member, ref reader);
                break;
            case ACCESSIBLE:
                HandleAccessible(context, node, member, ref reader);
                break;
            case APPLICATION when isRoot && member == "GetLocale":
                ReplyString(context, string.Empty);
                break;
            case COMPONENT when node != null:
                HandleComponent(context, node, member, ref reader);
                break;
            case ACTION when node != null:
                HandleAction(context, node, member, ref reader);
                break;
            case SELECTION when node != null && node.Peer is ISelectionProvider:
                HandleSelection(context, node, member, ref reader);
                break;
            case TEXT when node?.Peer is ITextProvider:
                HandleText(context, node, member, ref reader);
                break;
            case EDITABLE_TEXT when node != null && member == "SetTextContents" && node.Peer is IValueProvider editable:
                string text = reader.ReadString();
                bool accepted = !editable.IsReadOnly && node.Peer.IsEnabled();
                if (accepted)
                {
                    editable.SetValue(text);
                }

                ReplyBool(context, accepted);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private IReadOnlyList<AutomationPeer> ChildrenOf(AtSpiNode? node)
    {
        if (node != null)
        {
            return AutomationSurface.ChildrenOf(node.Peer);
        }

        var children = new List<AutomationPeer>();
        foreach (var surface in Surfaces())
        {
            if (surface.GetAutomationPeer() is AutomationPeer peer)
            {
                children.Add(peer);
            }
        }

        return children;
    }

    /// <summary>The parent of <paramref name="node"/>: its peer's parent, or the application object for a window.</summary>
    private AtSpiNode? ParentOf(AtSpiNode node)
        => AutomationSurface.ParentOf(node.Peer) is AutomationPeer parent ? NodeOf(parent) : null;

    private IReadOnlyList<string> InterfacesOf(AtSpiNode? node)
    {
        if (node == null)
        {
            return [ACCESSIBLE, APPLICATION];
        }

        var interfaces = new List<string> { ACCESSIBLE, COMPONENT };
        if (node.Actions().Count > 0)
        {
            interfaces.Add(ACTION);
        }

        if (node.Peer is IRangeValueProvider)
        {
            interfaces.Add(VALUE);
        }

        if (node.Peer is ISelectionProvider)
        {
            interfaces.Add(SELECTION);
        }

        if (node.Peer is ITextProvider)
        {
            interfaces.Add(TEXT);
        }

        if (node.Peer is IValueProvider value && !value.IsReadOnly)
        {
            interfaces.Add(EDITABLE_TEXT);
        }

        return interfaces;
    }

    private void HandleAccessible(MethodContext context, AtSpiNode? node, string member, ref Reader reader)
    {
        var writer = context.CreateReplyWriter(SignatureOfAccessible(member));
        try
        {
            switch (member)
            {
                case "GetChildAtIndex":
                {
                    int index = reader.ReadInt32();
                    var children = ChildrenOf(node);
                    WriteReferenceOf(ref writer, (uint)index < (uint)children.Count ? children[index] : null);
                    break;
                }
                case "GetChildren":
                    WriteReferenceArray(ref writer, ChildrenOf(node));
                    break;
                case "GetIndexInParent":
                    writer.WriteInt32(IndexInParent(node));
                    break;
                case "GetRelationSet":
                {
                    var relations = writer.WriteArrayStart(DBusType.Struct);
                    writer.WriteArrayEnd(relations);
                    break;
                }
                case "GetRole":
                    writer.WriteUInt32(node == null ? AtSpiMapping.ROLE_APPLICATION : AtSpiMapping.Role(node.Peer));
                    break;
                case "GetRoleName":
                case "GetLocalizedRoleName":
                    writer.WriteString(string.Empty);
                    break;
                case "GetState":
                {
                    var (low, high) = node == null ? (0u, 0u) : AtSpiMapping.States(node.Peer, AutomationSurface.Of(node.Peer));
                    var states = writer.WriteArrayStart(DBusType.UInt32);
                    writer.WriteUInt32(low);
                    writer.WriteUInt32(high);
                    writer.WriteArrayEnd(states);
                    break;
                }
                case "GetAttributes":
                {
                    var attributes = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEnd(attributes);
                    break;
                }
                case "GetApplication":
                    WriteReference(ref writer, null);
                    break;
                case "GetInterfaces":
                    writer.WriteArray(InterfacesOf(node).ToArray());
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    return;
            }

            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static string SignatureOfAccessible(string member) => member switch
    {
        "GetChildAtIndex" or "GetApplication" => "(so)",
        "GetChildren" => "a(so)",
        "GetIndexInParent" => "i",
        "GetRelationSet" => "a(ua(so))",
        "GetRole" => "u",
        "GetRoleName" or "GetLocalizedRoleName" => "s",
        "GetState" => "au",
        "GetAttributes" => "a{ss}",
        "GetInterfaces" => "as",
        _ => string.Empty,
    };

    private int IndexInParent(AtSpiNode? node)
    {
        if (node == null)
        {
            return -1;
        }

        var parent = ParentOf(node);
        var siblings = ChildrenOf(parent);
        for (int index = 0; index < siblings.Count; index++)
        {
            if (ReferenceEquals(siblings[index], node.Peer))
            {
                return index;
            }
        }

        return -1;
    }

    private void HandleProperties(MethodContext context, AtSpiNode? node, string member, ref Reader reader)
    {
        string @interface = reader.ReadString();
        if (member == "Get")
        {
            string property = reader.ReadString();
            var writer = context.CreateReplyWriter("v");
            try
            {
                if (!WriteProperty(ref writer, node, @interface, property))
                {
                    context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"No property {@interface}.{property}.");
                    return;
                }

                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        else if (member == "GetAll")
        {
            var writer = context.CreateReplyWriter("a{sv}");
            try
            {
                var dictionary = writer.WriteDictionaryStart();
                foreach (string property in PropertyNames(@interface, node))
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(property);
                    WriteProperty(ref writer, node, @interface, property);
                }

                writer.WriteDictionaryEnd(dictionary);
                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        else if (member == "Set")
        {
            string property = reader.ReadString();
            reader.ReadSignature();
            if (@interface == VALUE && property == "CurrentValue" && node?.Peer is IRangeValueProvider range && !range.IsReadOnly
                && node.Peer.IsEnabled())
            {
                range.SetValue(Math.Clamp(reader.ReadDouble(), range.Minimum, range.Maximum));
            }

            context.Reply(context.CreateReplyWriter(null).CreateMessage());
        }
        else
        {
            context.ReplyUnknownMethodError();
        }
    }

    private static IEnumerable<string> PropertyNames(string @interface, AtSpiNode? node) => @interface switch
    {
        ACCESSIBLE => ["Name", "Description", "Parent", "ChildCount", "Locale", "AccessibleId", "HelpText"],
        APPLICATION when node == null => ["ToolkitName", "Version", "AtspiVersion", "Id"],
        ACTION => ["NActions"],
        VALUE => ["MinimumValue", "MaximumValue", "MinimumIncrement", "CurrentValue", "Text"],
        SELECTION => ["NSelectedChildren"],
        TEXT => ["CharacterCount", "CaretOffset"],
        _ => [],
    };

    /// <summary>Writes the property as a variant; false when the object has no such property.</summary>
    private bool WriteProperty(ref MessageWriter writer, AtSpiNode? node, string @interface, string property)
    {
        var peer = node?.Peer;
        switch (@interface, property)
        {
            case (ACCESSIBLE, "Name"):
                writer.WriteVariantString(peer?.GetName() ?? AppDomain.CurrentDomain.FriendlyName);
                return true;
            case (ACCESSIBLE, "Description"):
            case (ACCESSIBLE, "HelpText"):
                writer.WriteVariantString(peer?.GetHelpText() ?? string.Empty);
                return true;
            case (ACCESSIBLE, "Parent"):
                writer.WriteSignature("(so)");
                if (node == null)
                {
                    writer.WriteStructureStart();
                    writer.WriteString(_desktop.Name);
                    writer.WriteObjectPath(_desktop.Path);
                }
                else
                {
                    WriteReference(ref writer, ParentOf(node));
                }

                return true;
            case (ACCESSIBLE, "ChildCount"):
                writer.WriteVariantInt32(ChildrenOf(node).Count);
                return true;
            case (ACCESSIBLE, "Locale"):
                writer.WriteVariantString(string.Empty);
                return true;
            case (ACCESSIBLE, "AccessibleId"):
                writer.WriteVariantString(peer?.GetAutomationId() ?? string.Empty);
                return true;
            case (APPLICATION, "ToolkitName"):
                writer.WriteVariantString("MewUI");
                return true;
            case (APPLICATION, "Version"):
                writer.WriteVariantString(typeof(Window).Assembly.GetName().Version?.ToString() ?? string.Empty);
                return true;
            case (APPLICATION, "AtspiVersion"):
                writer.WriteVariantString("2.1");
                return true;
            case (APPLICATION, "Id"):
                writer.WriteVariantInt32(0);
                return true;
            case (ACTION, "NActions") when node != null:
                writer.WriteVariantInt32(node.Actions().Count);
                return true;
            case (VALUE, _) when peer is IRangeValueProvider range:
                return WriteValueProperty(ref writer, range, property);
            case (SELECTION, "NSelectedChildren") when peer is ISelectionProvider selection:
                writer.WriteVariantInt32(selection.GetSelection().Count);
                return true;
            case (TEXT, "CharacterCount") when peer is ITextProvider text:
                writer.WriteVariantInt32(text.Text.Length);
                return true;
            case (TEXT, "CaretOffset") when peer is ITextProvider text:
                writer.WriteVariantInt32(text.CaretOffset);
                return true;
            default:
                return false;
        }
    }

    private static bool WriteValueProperty(ref MessageWriter writer, IRangeValueProvider range, string property)
    {
        switch (property)
        {
            case "MinimumValue":
                writer.WriteVariantDouble(range.Minimum);
                return true;
            case "MaximumValue":
                writer.WriteVariantDouble(range.Maximum);
                return true;
            case "MinimumIncrement":
                writer.WriteVariantDouble(range.SmallChange);
                return true;
            case "CurrentValue":
                writer.WriteVariantDouble(range.Value);
                return true;
            case "Text":
                writer.WriteVariantString(range.Value.ToString(System.Globalization.CultureInfo.CurrentCulture));
                return true;
            default:
                return false;
        }
    }

    private void HandleComponent(MethodContext context, AtSpiNode node, string member, ref Reader reader)
    {
        switch (member)
        {
            case "GetExtents":
            {
                var extents = Extents(node, (int)reader.ReadUInt32());
                var writer = context.CreateReplyWriter("(iiii)");
                writer.WriteStructureStart();
                writer.WriteInt32(extents.X);
                writer.WriteInt32(extents.Y);
                writer.WriteInt32(extents.Width);
                writer.WriteInt32(extents.Height);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetPosition":
            {
                var extents = Extents(node, (int)reader.ReadUInt32());
                var writer = context.CreateReplyWriter("ii");
                writer.WriteInt32(extents.X);
                writer.WriteInt32(extents.Y);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetSize":
            {
                var extents = Extents(node, COORD_SCREEN);
                var writer = context.CreateReplyWriter("ii");
                writer.WriteInt32(extents.Width);
                writer.WriteInt32(extents.Height);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "Contains":
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                var extents = Extents(node, (int)reader.ReadUInt32());
                ReplyBool(context, x >= extents.X && y >= extents.Y && x < extents.X + extents.Width && y < extents.Y + extents.Height);
                break;
            }
            case "GetAccessibleAtPoint":
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                var point = ToScreen(node, x, y, (int)reader.ReadUInt32());
                AutomationPeer? hit = null;
                var children = AutomationSurface.ChildrenOf(node.Peer);
                for (int index = children.Count - 1; index >= 0; index--)
                {
                    if (!children[index].IsOffscreen() && AutomationSurface.ScreenBoundsOf(children[index]).Contains(point))
                    {
                        hit = children[index];
                        break;
                    }
                }

                var writer = context.CreateReplyWriter("(so)");
                WriteReferenceOf(ref writer, hit);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetLayer":
            {
                var writer = context.CreateReplyWriter("u");
                writer.WriteUInt32(node.Peer is WindowAutomationPeer ? LAYER_WINDOW : LAYER_WIDGET);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetMDIZOrder":
            {
                var writer = context.CreateReplyWriter("n");
                writer.WriteInt16(-1);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GrabFocus":
                node.Peer.SetFocus();
                ReplyBool(context, node.Peer.HasKeyboardFocus());
                break;
            case "GetAlpha":
            {
                var writer = context.CreateReplyWriter("d");
                writer.WriteDouble(1);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "ScrollTo":
            case "ScrollToPoint":
                if (node.Peer is IScrollItemProvider scrollItem)
                {
                    scrollItem.ScrollIntoView();
                }

                ReplyBool(context, node.Peer is IScrollItemProvider);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    /// <summary>The node's bounds in device pixels, relative to the screen, its window, or its parent.</summary>
    private (int X, int Y, int Width, int Height) Extents(AtSpiNode node, int coordType)
    {
        var bounds = AutomationSurface.ScreenBoundsOf(node.Peer);
        var origin = OriginOf(node, coordType);
        return ((int)Math.Round(bounds.X - origin.X), (int)Math.Round(bounds.Y - origin.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
    }

    private Point ToScreen(AtSpiNode node, int x, int y, int coordType)
    {
        var origin = OriginOf(node, coordType);
        return new Point(x + origin.X, y + origin.Y);
    }

    private Point OriginOf(AtSpiNode node, int coordType)
    {
        if (coordType == COORD_SCREEN)
        {
            return default;
        }
        else if (coordType == COORD_WINDOW)
        {
            return AutomationSurface.Of(node.Peer) is Window window ? window.ClientToScreen(default) : default;
        }
        else
        {
            return ParentOf(node) is AtSpiNode parent ? AutomationSurface.ScreenBoundsOf(parent.Peer).TopLeft : default;
        }
    }

    private void HandleAction(MethodContext context, AtSpiNode node, string member, ref Reader reader)
    {
        var actions = node.Actions();
        if (member == "GetActions")
        {
            var writer = context.CreateReplyWriter("a(sss)");
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (string action in actions)
            {
                writer.WriteStructureStart();
                writer.WriteString(action);
                writer.WriteString(action);
                writer.WriteString(string.Empty);
            }

            writer.WriteArrayEnd(array);
            ReplyAndDispose(context, ref writer);
            return;
        }

        int index = reader.ReadInt32();
        string name = (uint)index < (uint)actions.Count ? actions[index] : string.Empty;
        switch (member)
        {
            case "DoAction":
                ReplyBool(context, name.Length > 0 && node.DoAction(name));
                break;
            case "GetName":
            case "GetLocalizedName":
            case "GetDescription":
                ReplyString(context, name);
                break;
            case "GetKeyBinding":
                ReplyString(context, string.Empty);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void HandleSelection(MethodContext context, AtSpiNode node, string member, ref Reader reader)
    {
        var selection = (ISelectionProvider)node.Peer;
        switch (member)
        {
            case "GetSelectedChild":
            {
                int index = reader.ReadInt32();
                var selected = selection.GetSelection();
                var writer = context.CreateReplyWriter("(so)");
                WriteReferenceOf(ref writer, (uint)index < (uint)selected.Count ? selected[index] : null);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "SelectChild":
            case "DeselectChild":
            case "IsChildSelected":
            {
                int index = reader.ReadInt32();
                var children = AutomationSurface.ChildrenOf(node.Peer);
                var item = (uint)index < (uint)children.Count ? children[index] as ISelectionItemProvider : null;
                if (item == null)
                {
                    ReplyBool(context, false);
                }
                else if (member == "IsChildSelected")
                {
                    ReplyBool(context, item.IsSelected);
                }
                else if (member == "SelectChild")
                {
                    item.Select();
                    ReplyBool(context, true);
                }
                else
                {
                    item.RemoveFromSelection();
                    ReplyBool(context, true);
                }

                break;
            }
            case "DeselectSelectedChild":
            {
                int index = reader.ReadInt32();
                var selected = selection.GetSelection();
                bool done = (uint)index < (uint)selected.Count && selected[index] is ISelectionItemProvider item;
                if (done)
                {
                    ((ISelectionItemProvider)selected[index]).RemoveFromSelection();
                }

                ReplyBool(context, done);
                break;
            }
            case "SelectAll":
            case "ClearSelection":
                ReplyBool(context, false);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    // AT-SPI2 text boundary (AtspiTextBoundaryType) and granularity (AtspiTextGranularity) values.
    private const int BOUNDARY_CHAR = 0;
    private const int BOUNDARY_WORD_END = 2;
    private const int GRANULARITY_CHAR = 0;
    private const int GRANULARITY_WORD = 1;
    private const int GRANULARITY_PARAGRAPH = 4;

    private static TextUnit UnitOfBoundary(int boundary) => boundary switch
    {
        BOUNDARY_CHAR => TextUnit.Character,
        <= BOUNDARY_WORD_END => TextUnit.Word,
        // Sentences are read as the lines that hold them.
        _ => TextUnit.Line,
    };

    private static TextUnit UnitOfGranularity(int granularity) => granularity switch
    {
        GRANULARITY_CHAR => TextUnit.Character,
        GRANULARITY_WORD => TextUnit.Word,
        GRANULARITY_PARAGRAPH => TextUnit.Paragraph,
        _ => TextUnit.Line,
    };

    private void HandleText(MethodContext context, AtSpiNode node, string member, ref Reader reader)
    {
        var pattern = (ITextProvider)node.Peer;
        string text = pattern.Text;
        switch (member)
        {
            case "GetText":
            {
                int start = Math.Clamp(reader.ReadInt32(), 0, text.Length);
                int end = reader.ReadInt32();
                end = end < 0 ? text.Length : Math.Clamp(end, start, text.Length);
                ReplyString(context, text[start..end]);
                break;
            }
            case "GetTextAtOffset":
            case "GetTextBeforeOffset":
            case "GetTextAfterOffset":
            case "GetStringAtOffset":
            {
                int offset = reader.ReadInt32();
                int kind = (int)reader.ReadUInt32();
                var unit = member == "GetStringAtOffset" ? UnitOfGranularity(kind) : UnitOfBoundary(kind);
                var range = pattern.GetUnitAt(offset, unit);
                if (member == "GetTextBeforeOffset")
                {
                    range = range.Start > 0 ? pattern.GetUnitAt(range.Start - 1, unit) : new Aprillz.MewUI.Text.TextRange(0, 0);
                }
                else if (member == "GetTextAfterOffset")
                {
                    int next = range.Start + range.Length;
                    range = next < text.Length ? pattern.GetUnitAt(next, unit) : new Aprillz.MewUI.Text.TextRange(text.Length, 0);
                }

                var writer = context.CreateReplyWriter("sii");
                writer.WriteString(text.Substring(range.Start, range.Length));
                writer.WriteInt32(range.Start);
                writer.WriteInt32(range.Start + range.Length);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetCharacterAtOffset":
            {
                int offset = reader.ReadInt32();
                var writer = context.CreateReplyWriter("i");
                writer.WriteInt32((uint)offset < (uint)text.Length ? char.ConvertToUtf32(text, offset) : 0);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetCaretOffset":
            {
                var writer = context.CreateReplyWriter("i");
                writer.WriteInt32(pattern.CaretOffset);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "SetCaretOffset":
            {
                int offset = reader.ReadInt32();
                bool canSet = pattern.CaretOffset >= 0 && node.Peer.IsEnabled();
                if (canSet)
                {
                    pattern.Select(offset, 0);
                }

                ReplyBool(context, canSet);
                break;
            }
            case "GetCharacterExtents":
            case "GetRangeExtents":
            {
                int start = reader.ReadInt32();
                int end = member == "GetRangeExtents" ? reader.ReadInt32() : start + 1;
                var extents = TextExtents(node, pattern, start, end, (int)reader.ReadUInt32());
                var writer = context.CreateReplyWriter("iiii");
                writer.WriteInt32(extents.X);
                writer.WriteInt32(extents.Y);
                writer.WriteInt32(extents.Width);
                writer.WriteInt32(extents.Height);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetOffsetAtPoint":
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                var screen = ToScreen(node, x, y, (int)reader.ReadUInt32());
                var writer = context.CreateReplyWriter("i");
                writer.WriteInt32(pattern.GetOffsetAt(AutomationSurface.ScreenToClient(node.Peer, screen)));
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetNSelections":
            {
                var writer = context.CreateReplyWriter("i");
                writer.WriteInt32(pattern.GetSelection().Count);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetSelection":
            {
                int index = reader.ReadInt32();
                var selection = pattern.GetSelection();
                var range = (uint)index < (uint)selection.Count ? selection[index] : new Aprillz.MewUI.Text.TextRange(0, 0);
                var writer = context.CreateReplyWriter("ii");
                writer.WriteInt32(range.Start);
                writer.WriteInt32(range.Start + range.Length);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "AddSelection":
            case "SetSelection":
            {
                if (member == "SetSelection")
                {
                    reader.ReadInt32();
                }

                int start = reader.ReadInt32();
                int end = reader.ReadInt32();
                bool canSelect = pattern.CaretOffset >= 0 && node.Peer.IsEnabled();
                if (canSelect)
                {
                    pattern.Select(Math.Min(start, end), Math.Abs(end - start));
                }

                ReplyBool(context, canSelect);
                break;
            }
            case "RemoveSelection":
            {
                reader.ReadInt32();
                bool canSelect = pattern.CaretOffset >= 0 && node.Peer.IsEnabled();
                if (canSelect)
                {
                    pattern.Select(pattern.CaretOffset, 0);
                }

                ReplyBool(context, canSelect);
                break;
            }
            case "GetAttributes":
            case "GetAttributeRun":
            {
                // The text carries one format and no attributes: the run is the whole text.
                var writer = context.CreateReplyWriter("a{ss}ii");
                var attributes = writer.WriteDictionaryStart();
                writer.WriteDictionaryEnd(attributes);
                writer.WriteInt32(0);
                writer.WriteInt32(text.Length);
                ReplyAndDispose(context, ref writer);
                break;
            }
            case "GetDefaultAttributes":
            {
                var writer = context.CreateReplyWriter("a{ss}");
                var attributes = writer.WriteDictionaryStart();
                writer.WriteDictionaryEnd(attributes);
                ReplyAndDispose(context, ref writer);
                break;
            }
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    /// <summary>The bounds of the characters in device pixels, relative to the screen, the window, or the parent.</summary>
    private (int X, int Y, int Width, int Height) TextExtents(AtSpiNode node, ITextProvider pattern, int start, int end, int coordType)
    {
        var union = Rect.Empty;
        foreach (var line in pattern.GetBounds(start, Math.Max(0, end - start)))
        {
            union = union.IsEmpty ? line : union.Union(line);
        }

        var bounds = AutomationSurface.ClientToScreen(node.Peer, union);
        var origin = OriginOf(node, coordType);
        return ((int)Math.Round(bounds.X - origin.X), (int)Math.Round(bounds.Y - origin.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
    }

    private static void ReplyAndDispose(MethodContext context, ref MessageWriter writer)
    {
        try
        {
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void ReplyBool(MethodContext context, bool value)
    {
        var writer = context.CreateReplyWriter("b");
        writer.WriteBool(value);
        ReplyAndDispose(context, ref writer);
    }

    private static void ReplyString(MethodContext context, string value)
    {
        var writer = context.CreateReplyWriter("s");
        writer.WriteString(value);
        ReplyAndDispose(context, ref writer);
    }
}
