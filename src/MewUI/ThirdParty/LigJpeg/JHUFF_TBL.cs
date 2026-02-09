namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// Huffman coding table.
/// </summary>
internal class JHUFF_TBL
{
    /* These two fields directly represent the contents of a JPEG DHT marker */

    /* length k bits; bits[0] is unused */


    internal JHUFF_TBL()
    {
    }

    internal byte[] Bits { get; } = new byte[17];

    internal byte[] Huffval { get; } = new byte[256];

    /// <summary>
    /// Gets or sets a value indicating whether the table has been output to file.
    /// </summary>
    /// <value>It's initialized <c>false</c> when the table is created, and set 
    /// <c>true</c> when it's been output to the file. You could suppress output 
    /// of a table by setting this to <c>true</c>.
    /// </value>
    /// <remarks>This property is used only during compression. It's initialized
    /// <c>false</c> when the table is created, and set <c>true</c> when it's been
    /// output to the file. You could suppress output of a table by setting this to
    /// <c>true</c>.</remarks>
    public bool Sent_table { get; set; }
}
