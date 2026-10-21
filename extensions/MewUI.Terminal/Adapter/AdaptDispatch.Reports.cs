// Ported from Windows Terminal src/terminal/adapter/adaptDispatch.cpp (MIT).

using System.Text;

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Adapter;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;

internal sealed partial class AdaptDispatch
{
    // Macros are not supported, so DECMSR reports the full macro space of the original.
    private const int MACRO_SPACE_BYTES = 0x40000;

    // The cell size in pixels that pixel size reports use: a VT340 cell, as sixel images assume.
    private static readonly TilSize _reportCellSize = new(10, 20);

    public override void RequestChecksumRectangularArea(int id, int page, int top, int left, int bottom, int right)
    {
        ushort checksum = 0;

        // The checksum is only calculated when enabled, since it can reveal the screen content to the host.
        if ((_optionalFeatures & OptionalFeature.ChecksumReport) != 0 && page != 0)
        {
            // Default colors count as their 16-color equivalents.
            int defaultForegroundIndex = _renderSettings.GetColorAliasIndex(ColorAlias.DefaultForeground);
            int defaultBackgroundIndex = _renderSettings.GetColorAliasIndex(ColorAlias.DefaultBackground);
            defaultForegroundIndex = defaultForegroundIndex < 16 ? defaultForegroundIndex : 7;
            defaultBackgroundIndex = defaultBackgroundIndex < 16 ? defaultBackgroundIndex : 0;

            var target = _pages.Get(page);
            var eraseRect = CalculateRectArea(target, top, left, bottom, right);
            for (int row = eraseRect.Top; row < eraseRect.Bottom; row++)
            {
                for (int col = eraseRect.Left; col < eraseRect.Right; col++)
                {
                    var cell = target.Buffer.ReadCell(new TilPoint(col, row));
                    foreach (char character in cell.Glyph)
                    {
                        // The SUB substitution symbol counts as the ESC it replaced on DEC terminals.
                        checksum -= character == '␦' ? (ushort)0x1B : character;
                    }

                    var attr = cell.Attribute;
                    checksum -= (ushort)(attr.IsProtected() ? 0x04 : 0);
                    checksum -= (ushort)(attr.IsInvisible() ? 0x08 : 0);
                    checksum -= (ushort)(attr.IsUnderlined() ? 0x10 : 0);
                    checksum -= (ushort)(attr.IsReverseVideo() ? 0x20 : 0);
                    checksum -= (ushort)(attr.IsBlinking() ? 0x40 : 0);
                    checksum -= (ushort)(attr.IsIntense() ? 0x80 : 0);
                    int foregroundIndex = attr.Foreground.IsLegacy() ? attr.Foreground.Index : defaultForegroundIndex;
                    int backgroundIndex = attr.Background.IsLegacy() ? attr.Background.Index : defaultBackgroundIndex;
                    checksum -= (ushort)(foregroundIndex << 4);
                    checksum -= (ushort)backgroundIndex;
                }
            }
        }

        ReturnDcsResponse($"{id}!~{checksum:X4}");
    }

    public override void DeviceStatusReport(StatusType statusType, VTParameter id)
    {
        switch (statusType)
        {
            case StatusType.OperatingStatus:
                DeviceStatusReportResponse("0");
                break;
            case StatusType.CursorPositionReport:
                CursorPositionReport(false);
                break;
            case StatusType.ExtendedCursorPositionReport:
                CursorPositionReport(true);
                break;
            case StatusType.PrinterStatus:
                DeviceStatusReportResponse("?13");
                break;
            case StatusType.UserDefinedKeys:
                DeviceStatusReportResponse("?23");
                break;
            case StatusType.KeyboardStatus:
                DeviceStatusReportResponse("?27;0;0;5");
                break;
            case StatusType.LocatorStatus:
                DeviceStatusReportResponse("?53");
                break;
            case StatusType.LocatorIdentity:
                DeviceStatusReportResponse("?57;0");
                break;
            case StatusType.MacroSpaceReport:
                ReturnCsiResponse($"{MACRO_SPACE_BYTES / 16}*{{");
                break;
            case StatusType.MemoryChecksum:
                ReturnDcsResponse($"{id.ValueOr(0)}!~0000");
                break;
            case StatusType.DataIntegrity:
                DeviceStatusReportResponse("?70");
                break;
            case StatusType.MultipleSessionStatus:
                DeviceStatusReportResponse("?83");
                break;
        }
    }

    public override void DeviceAttributes()
    {
        // Clipboard access (52) is only announced while it is allowed.
        if ((_optionalFeatures & OptionalFeature.ClipboardWrite) != 0)
        {
            ReturnCsiResponse("?61;4;6;7;14;21;22;23;24;28;32;42;52c");
        }
        else
        {
            ReturnCsiResponse("?61;4;6;7;14;21;22;23;24;28;32;42c");
        }
    }

    public override void SecondaryDeviceAttributes() => ReturnCsiResponse(">0;10;1c");

    public override void TertiaryDeviceAttributes() => ReturnDcsResponse("!|00000000");

    public override void Vt52DeviceAttributes() => _api.ReturnResponse("\u001b/Z");

    public override void RequestTerminalParameters(ReportingPermission permission)
    {
        // No parity, 8 bits, 38400 baud both ways, clock multiplier 1, no STP flags.
        switch (permission)
        {
            case ReportingPermission.Unsolicited:
                ReturnCsiResponse("2;1;1;128;128;1;0x");
                break;
            case ReportingPermission.Solicited:
                ReturnCsiResponse("3;1;1;128;128;1;0x");
                break;
        }
    }

    private void DeviceStatusReportResponse(string parameters) => ReturnCsiResponse($"{parameters}n");

    private void CursorPositionReport(bool extendedReport)
    {
        var page = _pages.ActivePage();

        // The position is 1-based and page-relative, and margin-relative in origin mode.
        var cursorPosition = page.Cursor.Position;
        int row = cursorPosition.Y - page.Top + 1;
        int column = cursorPosition.X + 1;
        if (HasMode(Mode.Origin))
        {
            column -= GetHorizontalMargins(page.Width).Left;
            row -= GetVerticalMargins(page, false).Top;
        }

        if (extendedReport)
        {
            ReturnCsiResponse($"?{row};{column};{page.Number}R");
        }
        else
        {
            ReturnCsiResponse($"{row};{column}R");
        }
    }

    public override void RequestDisplayedExtent()
    {
        var page = _pages.VisiblePage();
        int width = page.Viewport.Width;
        int height = page.Viewport.Height;
        int left = page.XPanOffset + 1;
        int top = page.YPanOffset + 1;
        ReturnCsiResponse($"{height};{width};{left};{top};{page.Number}\"w");
    }

    public override void WindowManipulation(WindowManipulationType function, VTParameter parameter1, VTParameter parameter2)
    {
        void ReportSize(TilSize size) => ReturnCsiResponse($"{(int)function - 10};{size.Height};{size.Width}t");

        switch (function)
        {
            case WindowManipulationType.DeIconifyWindow:
                _api.ShowWindow(true);
                break;
            case WindowManipulationType.IconifyWindow:
                _api.ShowWindow(false);
                break;
            case WindowManipulationType.RefreshWindow:
                _pages.VisiblePage().Buffer.TriggerRedrawAll();
                break;
            case WindowManipulationType.ResizeWindowInCharacters:
                _api.ResizeWindow(parameter2.ValueOr(0), parameter1.ValueOr(0));
                break;
            case WindowManipulationType.ReportTextSizeInCharacters:
                ReportSize(_pages.VisiblePage().Size);
                break;
            case WindowManipulationType.ReportTextSizeInPixels:
                var pageSize = _pages.VisiblePage().Size;
                ReportSize(new TilSize(pageSize.Width * _reportCellSize.Width, pageSize.Height * _reportCellSize.Height));
                break;
            case WindowManipulationType.ReportCharacterCellSize:
                ReportSize(_reportCellSize);
                break;
        }
    }

    public override void RequestUserPreferenceCharset()
    {
        int size = _termOutput.UserPreferenceCharsetSize;
        var id = _termOutput.UserPreferenceCharsetId;
        ReturnDcsResponse($"{(size == 96 ? 1 : 0)}!u{id}");
    }

    public override StringHandler? AssignUserPreferenceCharset(CharsetSize charsetSize)
    {
        var idBuilder = new VTIDBuilder();
        return character =>
        {
            if (character >= ' ' && character <= '/')
            {
                idBuilder.AddIntermediate(character);
            }
            else if (character >= '0' && character <= '~')
            {
                var id = idBuilder.Finalize(character);
                _termOutput.AssignUserPreferenceCharset(id, charsetSize == CharsetSize.Size96);
                return false;
            }

            return true;
        };
    }

    public override void RequestTerminalStateReport(ReportFormat format, VTParameter formatOption)
    {
        if (format == ReportFormat.ColorTableReport)
        {
            ReportColorTable((ColorModel)formatOption.ValueOr(0));
        }
    }

    public override StringHandler? RestoreTerminalState(ReportFormat format)
    {
        if (format == ReportFormat.ColorTableReport)
        {
            return RestoreColorTable();
        }

        return null;
    }

    private void ReportColorTable(ColorModel colorModel)
    {
        if (colorModel != ColorModel.HLS && colorModel != ColorModel.RGB)
        {
            return;
        }

        var response = new StringBuilder("2$s");
        int modelNumber = (int)colorModel;
        for (int colorNumber = 0; colorNumber < TextColor.TABLE_SIZE; colorNumber++)
        {
            uint color = _renderSettings.GetColorTableEntry(colorNumber);
            if (color != TextAttribute.INVALID_COLOR)
            {
                response.Append(colorNumber > 0 ? "/" : string.Empty);
                var (first, second, third) = colorModel == ColorModel.HLS ? VTUtils.ColorToHls(color) : VTUtils.ColorToRgb100(color);
                response.Append($"{colorNumber};{modelNumber};{first};{second};{third}");
            }
        }

        ReturnDcsResponse(response.ToString());
    }

    private StringHandler RestoreColorTable()
    {
        int parameter = 0;
        var parameters = new List<VTParameter>();
        return character =>
        {
            if (character >= '0' && character <= '9')
            {
                parameter *= 10;
                parameter += character - '0';
                parameter = Math.Min(parameter, StateMachine.MAX_PARAMETER_VALUE);
            }
            else if (character == ';')
            {
                if (parameters.Count < 5)
                {
                    parameters.Add(parameter);
                }

                parameter = 0;
            }
            else if (character == '/' || character == AsciiChars.ESC)
            {
                parameters.Add(parameter);
                var colorParameters = new VTParameters(parameters.ToArray());
                int colorNumber = colorParameters.At(0).ValueOr(0);
                if (colorNumber < TextColor.TABLE_SIZE)
                {
                    var colorModel = (ColorModel)colorParameters.At(1).ValueOr(0);
                    int first = colorParameters.At(2).ValueOr(0);
                    int second = colorParameters.At(3).ValueOr(0);
                    int third = colorParameters.At(4).ValueOr(0);
                    if (colorModel == ColorModel.HLS)
                    {
                        SetColorTableEntry(colorNumber, VTUtils.ColorFromHls(first, second, third));
                    }
                    else if (colorModel == ColorModel.RGB)
                    {
                        SetColorTableEntry(colorNumber, VTUtils.ColorFromRgb100(first, second, third));
                    }
                }

                parameters.Clear();
                parameter = 0;
            }

            return character != AsciiChars.ESC;
        };
    }

    public override StringHandler? RequestSetting()
    {
        int parameter = 0;
        var idBuilder = new VTIDBuilder();
        return character =>
        {
            bool isFinal = character >= '@' && character <= '~';
            if (isFinal)
            {
                var id = idBuilder.Finalize(character);
                switch (id.ToString())
                {
                    case "m":
                        ReportSgrSetting();
                        break;
                    case "r":
                        ReportDecstbmSetting();
                        break;
                    case "s":
                        ReportDecslrmSetting();
                        break;
                    case " q":
                        ReportDecscusrSetting();
                        break;
                    case "\"q":
                        ReportDecscaSetting();
                        break;
                    case "*x":
                        ReportDecsaceSetting();
                        break;
                    case ",|":
                        // An omitted or 0 item number means item 1, as for any counted parameter.
                        ReportDecacSetting(new VTParameter(parameter));
                        break;
                    default:
                        ReturnDcsResponse("0$r");
                        break;
                }

                return false;
            }

            bool isParameterPrefix = character >= '<' && character <= '?';

            // The original excludes 9 from the parameter digits.
            bool isParameter = character >= '0' && character < '9';
            bool isIntermediate = character >= ' ' && character <= '/';
            if (isParameterPrefix || isIntermediate)
            {
                idBuilder.AddIntermediate(character);
            }
            else if (isParameter)
            {
                parameter *= 10;
                parameter += character - '0';
                parameter = Math.Min(parameter, StateMachine.MAX_PARAMETER_VALUE);
            }

            return true;
        };
    }

    private void ReportSgrSetting()
    {
        // A report always starts with 0, so the reported attributes replace the current ones when applied.
        var response = new StringBuilder("1$r0");
        var attr = _pages.ActivePage().Attributes;
        var underlineStyle = attr.UnderlineStyle;

        void AddAttribute(string parameter, bool enabled)
        {
            if (enabled)
            {
                response.Append(parameter);
            }
        }

        AddAttribute(";1", attr.IsIntense());
        AddAttribute(";2", attr.IsFaint());
        AddAttribute(";3", attr.IsItalic());
        AddAttribute(";4", underlineStyle == UnderlineStyle.SinglyUnderlined);
        AddAttribute(";4:3", underlineStyle == UnderlineStyle.CurlyUnderlined);
        AddAttribute(";4:4", underlineStyle == UnderlineStyle.DottedUnderlined);
        AddAttribute(";4:5", underlineStyle == UnderlineStyle.DashedUnderlined);
        AddAttribute(";5", attr.IsBlinking());
        AddAttribute(";7", attr.IsReverseVideo());
        AddAttribute(";8", attr.IsInvisible());
        AddAttribute(";9", attr.IsCrossedOut());
        AddAttribute(";21", underlineStyle == UnderlineStyle.DoublyUnderlined);
        AddAttribute(";53", attr.IsOverlined());

        void AddColor(int colorBase, TextColor color)
        {
            if (color.IsIndex16())
            {
                int index = color.Index;
                int colorParameter = colorBase + (index >= 8 ? 60 : 0) + (index % 8);
                response.Append($";{colorParameter}");
            }
            else if (color.IsIndex256())
            {
                response.Append($";{colorBase + 8}:5:{color.Index}");
            }
            else if (color.IsRgb())
            {
                response.Append($";{colorBase + 8}:2::{color.R}:{color.G}:{color.B}");
            }
        }

        AddColor(30, attr.Foreground);
        AddColor(40, attr.Background);
        AddColor(50, attr.UnderlineColor);

        response.Append('m');
        ReturnDcsResponse(response.ToString());
    }

    private void ReportDecstbmSetting()
    {
        var page = _pages.ActivePage();
        var (marginTop, marginBottom) = GetVerticalMargins(page, false);
        ReturnDcsResponse($"1$r{marginTop + 1};{marginBottom + 1}r");
    }

    private void ReportDecslrmSetting()
    {
        int pageWidth = _pages.ActivePage().Width;
        var (marginLeft, marginRight) = GetHorizontalMargins(pageWidth);
        ReturnDcsResponse($"1$r{marginLeft + 1};{marginRight + 1}s");
    }

    private void ReportDecscusrSetting()
    {
        var cursor = _pages.ActivePage().Cursor;
        bool blinking = cursor.IsBlinking;
        switch (cursor.Type)
        {
            case CursorType.FullBox:
                ReturnDcsResponse(blinking ? "1$r1 q" : "1$r2 q");
                break;
            case CursorType.Underscore:
                ReturnDcsResponse(blinking ? "1$r3 q" : "1$r4 q");
                break;
            case CursorType.VerticalBar:
                ReturnDcsResponse(blinking ? "1$r5 q" : "1$r6 q");
                break;
            default:
                // Shapes DECSCUSR cannot set are reported as the default.
                ReturnDcsResponse("1$r0 q");
                break;
        }
    }

    private void ReportDecscaSetting()
    {
        bool isProtected = _pages.ActivePage().Attributes.IsProtected();
        ReturnDcsResponse(isProtected ? "1$r1\"q" : "1$r0\"q");
    }

    private void ReportDecsaceSetting()
    {
        bool rectangularExtent = HasMode(Mode.RectangularChangeExtent);
        ReturnDcsResponse(rectangularExtent ? "1$r2*x" : "1$r1*x");
    }

    private void ReportDecacSetting(int itemNumber)
    {
        int foregroundIndex;
        int backgroundIndex;
        switch ((ColorItem)itemNumber)
        {
            case ColorItem.NormalText:
                foregroundIndex = _renderSettings.GetColorAliasIndex(ColorAlias.DefaultForeground);
                backgroundIndex = _renderSettings.GetColorAliasIndex(ColorAlias.DefaultBackground);
                break;
            case ColorItem.WindowFrame:
                foregroundIndex = _renderSettings.GetColorAliasIndex(ColorAlias.FrameForeground);
                backgroundIndex = _renderSettings.GetColorAliasIndex(ColorAlias.FrameBackground);
                break;
            default:
                ReturnDcsResponse("0$r");
                return;
        }

        ReturnDcsResponse($"1$r{itemNumber};{foregroundIndex};{backgroundIndex},|");
    }

    public override void RequestPresentationStateReport(PresentationReportFormat format)
    {
        switch (format)
        {
            case PresentationReportFormat.CursorInformationReport:
                ReportCursorInformation();
                break;
            case PresentationReportFormat.TabulationStopReport:
                ReportTabStops();
                break;
        }
    }

    public override StringHandler? RestorePresentationState(PresentationReportFormat format)
    {
        switch (format)
        {
            case PresentationReportFormat.CursorInformationReport:
                return RestoreCursorInformation();
            case PresentationReportFormat.TabulationStopReport:
                return RestoreTabStops();
            default:
                return null;
        }
    }

    private void ReportCursorInformation()
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        var attributes = page.Attributes;

        var cursorPosition = cursor.Position;
        int row = cursorPosition.Y - page.Top + 1;
        int column = cursorPosition.X + 1;
        if (HasMode(Mode.Origin))
        {
            column -= GetHorizontalMargins(page.Width).Left;
            row -= GetVerticalMargins(page, false).Top;
        }

        // The flag fields are bit sets added to '@' (0100 0000).
        char renditionAttributes = (char)('@'
            + (attributes.IsIntense() ? 1 : 0)
            + (attributes.IsUnderlined() ? 2 : 0)
            + (attributes.IsBlinking() ? 4 : 0)
            + (attributes.IsReverseVideo() ? 8 : 0)
            + (attributes.IsInvisible() ? 16 : 0));

        char characterAttributes = attributes.IsProtected() ? 'A' : '@';

        char flags = (char)('@'
            + (HasMode(Mode.Origin) ? 1 : 0)
            + (_termOutput.IsSingleShiftPending(2) ? 2 : 0)
            + (_termOutput.IsSingleShiftPending(3) ? 4 : 0)
            + (cursor.DelayEolWrap.HasValue ? 8 : 0));

        int leftSetNumber = _termOutput.LeftSetNumber;
        int rightSetNumber = _termOutput.RightSetNumber;

        char charsetSizes = (char)('@'
            + (_termOutput.GetCharsetSize(0) == 96 ? 1 : 0)
            + (_termOutput.GetCharsetSize(1) == 96 ? 2 : 0)
            + (_termOutput.GetCharsetSize(2) == 96 ? 4 : 0)
            + (_termOutput.GetCharsetSize(3) == 96 ? 8 : 0));

        var charset0 = _termOutput.GetCharsetId(0);
        var charset1 = _termOutput.GetCharsetId(1);
        var charset2 = _termOutput.GetCharsetId(2);
        var charset3 = _termOutput.GetCharsetId(3);

        ReturnDcsResponse(
            $"1$u{row};{column};{page.Number};{renditionAttributes};{characterAttributes};{flags};{leftSetNumber};{rightSetNumber};{charsetSizes};{charset0}{charset1}{charset2}{charset3}");
    }

    private enum CursorInformationField
    {
        Row,
        Column,
        Page,
        Sgr,
        Attr,
        Flags,
        GL,
        GR,
        Sizes,
        G0,
        G1,
        G2,
        G3,
    }

    private StringHandler RestoreCursorInformation()
    {
        var field = CursorInformationField.Row;
        int value = 0;
        var charsetId = new VTIDBuilder();
        var charset96 = new bool[4];
        var row = VTParameter.Omitted;
        var column = VTParameter.Omitted;

        return character =>
        {
            bool isNumeric = field is CursorInformationField.Row or CursorInformationField.Column or CursorInformationField.Page
                or CursorInformationField.GL or CursorInformationField.GR;
            bool isFlags = field is CursorInformationField.Sgr or CursorInformationField.Attr or CursorInformationField.Flags
                or CursorInformationField.Sizes;
            bool isCharset = field is CursorInformationField.G0 or CursorInformationField.G1 or CursorInformationField.G2
                or CursorInformationField.G3;

            if (isNumeric)
            {
                if (character >= '0' && character <= '9')
                {
                    value *= 10;
                    value += character - '0';
                    value = Math.Min(value, StateMachine.MAX_PARAMETER_VALUE);
                }
                else if (character == ';' || character == AsciiChars.ESC)
                {
                    if (field == CursorInformationField.Row)
                    {
                        row = value;
                    }
                    else if (field == CursorInformationField.Column)
                    {
                        column = value;
                    }
                    else if (field == CursorInformationField.Page)
                    {
                        PagePositionAbsolute(value);
                    }
                    else if (field == CursorInformationField.GL && value <= 3)
                    {
                        LockingShift(value);
                    }
                    else if (field == CursorInformationField.GR && value <= 3)
                    {
                        LockingShiftRight(value);
                    }

                    value = 0;
                    field++;
                }
            }
            else if (isFlags)
            {
                if (character >= '@' && character <= '~' && value == 0)
                {
                    value = character;
                    if (field == CursorInformationField.Sgr)
                    {
                        var page = _pages.ActivePage();
                        var attr = page.Attributes;
                        attr.SetIntense((value & 1) != 0);
                        attr.SetUnderlineStyle((value & 2) != 0 ? UnderlineStyle.SinglyUnderlined : UnderlineStyle.NoUnderline);
                        attr.SetBlinking((value & 4) != 0);
                        attr.SetReverseVideo((value & 8) != 0);
                        attr.SetInvisible((value & 16) != 0);
                        page.SetAttributes(attr);
                    }
                    else if (field == CursorInformationField.Attr)
                    {
                        var page = _pages.ActivePage();
                        var attr = page.Attributes;
                        attr.SetProtected((value & 1) != 0);
                        page.SetAttributes(attr);
                    }
                    else if (field == CursorInformationField.Sizes)
                    {
                        charset96[0] = (value & 1) != 0;
                        charset96[1] = (value & 2) != 0;
                        charset96[2] = (value & 4) != 0;
                        charset96[3] = (value & 8) != 0;
                    }
                    else if (field == CursorInformationField.Flags)
                    {
                        // The position is applied here, after origin mode is known.
                        SetModeFlag(Mode.Origin, (value & 1) != 0);
                        CursorPosition(row, column);
                        bool singleShift2 = (value & 2) != 0;
                        bool singleShift3 = (value & 4) != 0;
                        _termOutput.SingleShift(singleShift3 ? 3 : (singleShift2 ? 2 : 0));
                        if ((value & 8) != 0)
                        {
                            _pages.ActivePage().Cursor.DelayEolWrapAtCurrentPosition();
                        }
                    }
                }
                else if (character == ';')
                {
                    value = 0;
                    field++;
                }
            }
            else if (isCharset)
            {
                if (character >= ' ' && character <= '/')
                {
                    charsetId.AddIntermediate(character);
                }
                else if (character >= '0' && character <= '~')
                {
                    var id = charsetId.Finalize(character);
                    int gset = field - CursorInformationField.G0;
                    if (charset96[gset])
                    {
                        Designate96Charset(gset, id);
                    }
                    else
                    {
                        Designate94Charset(gset, id);
                    }

                    charsetId.Clear();
                    field++;
                }
            }

            return character != AsciiChars.ESC;
        };
    }

    private void ReportTabStops()
    {
        int width = _pages.ActivePage().Width;
        InitTabStopsForWidth(width);

        var response = new StringBuilder("2$u");
        bool needSeparator = false;
        for (int column = 0; column < width; column++)
        {
            if (_tabStopColumns[column])
            {
                response.Append(needSeparator ? "/" : string.Empty);
                response.Append(column + 1);
                needSeparator = true;
            }
        }

        ReturnDcsResponse(response.ToString());
    }

    private StringHandler RestoreTabStops()
    {
        // Stops are restored up to at least 132 columns, so a narrower page keeps them for a later resize.
        int width = Math.Max(_pages.ActivePage().Width, 132);
        ClearAllTabStops();
        InitTabStopsForWidth(width);

        int column = 0;
        return character =>
        {
            if (character >= '0' && character <= '9')
            {
                column *= 10;
                column += character - '0';
                column = Math.Min(column, StateMachine.MAX_PARAMETER_VALUE);
            }
            else if (character == '/' || character == AsciiChars.ESC)
            {
                // Column 1 is never a tab stop.
                if (column > 1 && column <= width)
                {
                    _tabStopColumns[column - 1] = true;
                }

                column = 0;
            }
            else
            {
                return false;
            }

            return character != AsciiChars.ESC;
        };
    }

    private void ReturnCsiResponse(string response)
    {
        string csi = _terminalInput.GetInputMode(TerminalInput.Mode.SendC1) ? "\u009b" : "\u001b[";
        _api.ReturnResponse(csi + response);
    }

    private void ReturnDcsResponse(string response)
    {
        bool sendC1 = _terminalInput.GetInputMode(TerminalInput.Mode.SendC1);
        string dcs = sendC1 ? "\u0090" : "\u001bP";
        string st = sendC1 ? "\u009c" : "\u001b\\";
        _api.ReturnResponse(dcs + response + st);
    }

    private void ReturnOscResponse(string response)
    {
        bool sendC1 = _terminalInput.GetInputMode(TerminalInput.Mode.SendC1);
        string osc = sendC1 ? "\u009d" : "\u001b]";
        string st = sendC1 ? "\u009c" : "\u001b\\";
        _api.ReturnResponse(osc + response + st);
    }

    public override void PlaySounds(VTParameters parameters)
    {
        // The volume is 0 to 7 and the duration is in 1/32 seconds; note 0 is silence and 1 to 25 are C5 to C7.
        int velocity = Math.Min(parameters.At(0).ValueOr(0), 7) * 127 / 7;
        var duration = TimeSpan.FromTicks(Math.Min(parameters.At(1).ValueOr(0), 255) * TimeSpan.TicksPerSecond / 32);
        var notes = parameters.Subspan(2).Values;
        foreach (var note in notes)
        {
            int noteNumber = Math.Min(note.ValueOr(0), 25) + 71;
            _api.PlayMidiNote(noteNumber, noteNumber == 71 ? 0 : velocity, duration);
        }
    }
}
