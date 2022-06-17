using System;
using System.Buffers;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PrimitiveStaticDataGenerator.Tests
{
    public static class TestHelper
    {
        public static string ToCommaSeparatedString<T>(this ReadOnlySpan<T> span)
        {
            T[] buffer = ArrayPool<T>.Shared.Rent(span.Length);
            try
            {
                span.CopyTo(buffer);
                return string.Join(",", buffer.Take(span.Length).Select(v => v?.ToString()));
            }
            finally
            {
                ArrayPool<T>.Shared.Return(buffer);
            }
        }

        public static ReadOnlySpan<int> ToByteSpan(this int[] values, bool isLittleEndian)
        {
            if (values is null)
                throw new ArgumentNullException(nameof(values));

            var buffer = new byte[sizeof(int) * values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                ref var value = ref values[i];
                var bufferSpan = ((Span<byte>)buffer).Slice(sizeof(int) * i);
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(bufferSpan, value);
                }
                else
                {
                    BinaryPrimitives.WriteInt32BigEndian(bufferSpan, value);
                }
            }

            return MemoryMarshal.Cast<byte, int>(buffer);
        }
    }
}
