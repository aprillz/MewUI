using Aprillz.MewUI;
using Aprillz.MewUI.Markdown;

internal sealed class DemoImageResolver : IMarkdownImageResolver
{
    public async ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
    {
        if (request.Url == "demo:slow")
        {
            await Task.Delay(1200, cancellationToken);
        }
        else if (request.Url != "demo:checker" && request.Url != "demo:wide")
        {
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        int width = request.Url == "demo:wide" ? 960 : 160;
        int height = request.Url == "demo:wide" ? 120 : 80;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int stride = (width * 3 + 3) & ~3;
        writer.Write((ushort)0x4d42);
        writer.Write(54 + stride * height);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(stride * height);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                bool tile = (column / 20 + row / 20) % 2 == 0;
                writer.Write((byte)(tile ? 220 : 80));
                writer.Write((byte)(40 + row * 180 / height));
                writer.Write((byte)(30 + column * 210 / width));
            }
            for (int padding = width * 3; padding < stride; padding++) writer.Write((byte)0);
        }
        var source = ImageSource.FromBytes(stream.ToArray());
        return new MarkdownImageLease(source, source.Dispose);
    }
}
