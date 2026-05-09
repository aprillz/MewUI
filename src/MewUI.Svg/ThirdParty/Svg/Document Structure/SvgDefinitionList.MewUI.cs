namespace Svg;

public partial class SvgDefinitionList
{
    protected override void Render(ISvgRenderer renderer)
    {
        // defs contents are definition-only and must not render directly.
    }
}
