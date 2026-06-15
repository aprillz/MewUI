using System.Linq;

namespace Svg;

public partial class SvgTextRef
{
    internal override IEnumerable<ISvgNode> GetContentNodes()
    {
        var refText = OwnerDocument?.IdManager.GetElementById(ReferencedElement) as SvgTextBase;
        var contentNodes = refText is null
            ? base.GetContentNodes()
            : refText.GetContentNodes();

        return contentNodes.Where(o => o is not ISvgDescriptiveElement);
    }
}
