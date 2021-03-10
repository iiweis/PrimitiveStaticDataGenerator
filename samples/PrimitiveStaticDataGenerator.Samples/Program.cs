using System;
using PrimitiveStaticDataGenerator;
using ReadOnlySpanByte = System.ReadOnlySpan<byte>;
using AttributeAlias = PrimitiveStaticDataGenerator.PrimitiveStaticDataAttribute;

Root.Write();

static partial class Root
{
    public static void Write()
    {
        Write<bool>(Bool());
        Write<byte>(Byte());
        Write<sbyte>(SByte());
        Write<char>(Char());
        Write<short>(Short());
        Write<ushort>(UShort());
        Write<char>(String());
        ChildStruct.Write();
        ChildClass<object>.Write();
        Write(MyNamespace.MyClass.OneTwoThree());
    }

    static void Write<T>(ReadOnlySpan<T> span)
    {
        Console.Write($"{typeof(T)} ");
        foreach (var item in span)
            Console.Write($"{item}, ");
        Console.WriteLine();
    }

    [return: AttributeAlias(true, false)]
    public static partial System.ReadOnlySpan<bool> Bool();

    [return: PrimitiveStaticData(new byte[] { 1, 2, 3, byte.MinValue, byte.MaxValue })]
    public static partial ReadOnlySpanByte Byte();

    [return: PrimitiveStaticData(new SByte[] { 0, 1, -1, sbyte.MinValue, sbyte.MaxValue })]
    public static partial ReadOnlySpan<SByte> SByte();

    [return: PrimitiveStaticData('A', 'B', 'C', char.MinValue, char.MaxValue)]
    public static partial ReadOnlySpan<char> Char();

    [return: PrimitiveStaticData(new short[] { 0, 1, -1, short.MinValue, short.MaxValue })]
    public static partial ReadOnlySpan<short> Short();

    [return: PrimitiveStaticData(new ushort[] { 1, 2, 3, ushort.MinValue, ushort.MaxValue })]
    public static partial ReadOnlySpan<ushort> UShort();

    [return: PrimitiveStaticData("あアaA")]
    public static partial ReadOnlySpan<char> String();

    public partial struct ChildStruct
    {
        public static void Write()
        {
            Write<int>(Int());
            Write<uint>(UInt());
            Write<float>(Float());
        }

        [return: PrimitiveStaticData(0, 1, -1, int.MinValue, int.MaxValue)]
        public static partial ReadOnlySpan<int> Int();

        [return: PrimitiveStaticData(1, 2, 3, uint.MinValue, uint.MaxValue)]
        internal static partial ReadOnlySpan<uint> UInt();

        [return: PrimitiveStaticData(0, 1, -1, float.MinValue, float.MaxValue)]
        internal static partial ReadOnlySpan<float> Float();
    }

    protected partial class ChildClass<TDummy>
    {
        public static void Write()
        {
            Write<long>(Long());
            Write<ulong>(ULong());
            Write<double>(Double());
        }

        [return: PrimitiveStaticData(0, 1, -1, long.MinValue, long.MaxValue)]
        private static partial ReadOnlySpan<long> Long();

        [return: PrimitiveStaticData(1, 2, 3, ulong.MinValue, ulong.MaxValue)]
        internal static partial ReadOnlySpan<ulong> ULong();

        [return: PrimitiveStaticData(0, 1, -1, double.MinValue, double.MaxValue)]
        protected static partial ReadOnlySpan<double> Double();
    }
}

namespace MyNamespace
{
    using StaticData = PrimitiveStaticDataGenerator.PrimitiveStaticDataAttribute;

    partial class MyClass
    {
        void Dummy(string[] args) { }

        [return: StaticData(1, 2, 3)]
        public static partial ReadOnlySpan<int> OneTwoThree();

    }
}