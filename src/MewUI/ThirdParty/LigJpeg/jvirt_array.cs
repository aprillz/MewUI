/*
 * This file contains the JPEG system-independent memory management
 * routines.
 */

/*
 * About virtual array management:
 *
 * Full-image-sized buffers are handled as "virtual" arrays.  The array is still accessed a strip at a
 * time, but the memory manager must save the whole array for repeated
 * accesses.
 *
 * The Access method is responsible for making a specific strip area accessible.
 */

using System.Diagnostics;

namespace BitMiracle.LibJpeg.Classic;

internal readonly struct JvirtArrayWindow<T>
{
    private readonly T[][] m_buffer;
    private readonly int m_startRow;

    public int Length { get; }

    public JvirtArrayWindow(T[][] buffer, int startRow, int numberOfRows)
    {
        m_buffer = buffer;
        m_startRow = startRow;
        Length = numberOfRows;
    }

    public T[] this[int rowIndex] => m_buffer[m_startRow + rowIndex];
}

/// <summary>
/// JPEG virtual array.
/// </summary>
/// <typeparam name="T">The type of array's elements.</typeparam>
/// <remarks>You can't create virtual array manually. For creation use methods
/// <see cref="jpeg_common_struct.CreateSamplesArray"/> and
/// <see cref="jpeg_common_struct.CreateBlocksArray"/>.
/// </remarks>
internal class jvirt_array<T>
{
    internal delegate T[][] Allocator(int width, int height);

    private T[][] m_buffer;   /* => the in-memory buffer */

    /// <summary>
    /// Request a virtual 2-D array
    /// </summary>
    /// <param name="width">Width of array</param>
    /// <param name="height">Total virtual array height</param>
    /// <param name="allocator">The allocator.</param>
    internal jvirt_array(int width, int height, Allocator allocator)
    {
        m_buffer = allocator(width, height);

        Debug.Assert(m_buffer != null);
    }

    /// <summary>
    /// Gets or sets the error processor.
    /// </summary>
    /// <value>The error processor.<br/>
    /// Default value: <c>null</c>
    /// </value>
    /// <remarks>Uses only for calling 
    /// <see cref="M:BitMiracle.LibJpeg.Classic.jpeg_common_struct.ERREXIT(BitMiracle.LibJpeg.Classic.J_MESSAGE_CODE)">jpeg_common_struct.ERREXIT</see>
    /// on error.</remarks>
    public jpeg_common_struct? ErrorProcessor { get; set; }

    /// <summary>
    /// Access the part of a virtual array.
    /// </summary>
    /// <param name="startRow">The first row in required block.</param>
    /// <param name="numberOfRows">The number of required rows.</param>
    /// <returns>The required part of virtual array.</returns>
    public T[][] Access(int startRow, int numberOfRows)
    {
        /* debugging check */
        if (startRow + numberOfRows > m_buffer.Length)
        {
            if (ErrorProcessor != null)
            {
                ErrorProcessor.ERREXIT(J_MESSAGE_CODE.JERR_BAD_VIRTUAL_ACCESS);
            }
            else
            {
                throw new InvalidOperationException("Bogus virtual array access");
            }
        }

        /* Return proper part of the buffer */
        T[][] ret = GC.AllocateUninitializedArray<T[]>(numberOfRows);
        for (int i = 0; i < numberOfRows; i++)
        {
            ret[i] = m_buffer[startRow + i];
        }

        return ret;
    }

    internal JvirtArrayWindow<T> AccessWindow(int startRow, int numberOfRows)
    {
        /* debugging check */
        if (startRow + numberOfRows > m_buffer.Length)
        {
            if (ErrorProcessor != null)
            {
                ErrorProcessor.ERREXIT(J_MESSAGE_CODE.JERR_BAD_VIRTUAL_ACCESS);
            }
            else
            {
                throw new InvalidOperationException("Bogus virtual array access");
            }
        }

        return new JvirtArrayWindow<T>(m_buffer, startRow, numberOfRows);
    }

    internal void CopyRowsTo(T[][] destination, int startRow, int numberOfRows)
    {
        /* debugging check */
        if (startRow + numberOfRows > m_buffer.Length || numberOfRows > destination.Length)
        {
            if (ErrorProcessor != null)
            {
                ErrorProcessor.ERREXIT(J_MESSAGE_CODE.JERR_BAD_VIRTUAL_ACCESS);
            }
            else
            {
                throw new InvalidOperationException("Bogus virtual array access");
            }
        }

        for (int i = 0; i < numberOfRows; i++)
        {
            destination[i] = m_buffer[startRow + i];
        }
    }
}
