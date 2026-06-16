using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Layouts;

namespace Aprillz.MewUI.MewCharts.Drawing.Layouts;

/// <summary>A stack layout of drawn elements (the MewUI backend binding of the core layout).</summary>
public class StackLayout : CoreStackLayout<MewDrawingContext> { }

/// <summary>An absolute layout of drawn elements.</summary>
public class AbsoluteLayout : CoreAbsoluteLayout<MewDrawingContext> { }

/// <summary>A table layout of drawn elements.</summary>
public class TableLayout : CoreTableLayout<MewDrawingContext> { }

/// <summary>A container with a background shape and a single content element.</summary>
public class Container<TShape> : BaseContainer<TShape, MewDrawingContext>
    where TShape : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>, new()
{ }

/// <summary>A container with a rounded-rectangle background.</summary>
public class Container : Container<RoundedRectangleGeometry> { }
