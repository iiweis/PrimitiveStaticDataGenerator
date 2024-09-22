# PrimitiveStaticDataGenerator

[![Nuget](https://img.shields.io/nuget/v/PrimitiveStaticDataGenerator?color=1f6feb)](https://www.nuget.org/packages/PrimitiveStaticDataGenerator)

> [!NOTE]
> This library was created at a time when optimization using ReadOnlySpan for constant arrays had the limitation that it did not apply to primitive types that are not single byte.
> 
> Since this restriction has been removed by the support of dotnet/roslyn#61414, there is little point in using this library in .NET 7 and later environments.<br>
> For more information on dotnet/roslyn#61414, check out [Performance Improvements in .NET 8 | Initialization](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/#initialization). 

## Usage

### user code
```cs
using System;
using PrimitiveStaticDataGenerator;

namespace MyCode
{
    public partial class UserClass
    {
        [return: PrimitiveStaticData(1, 2, 3, 4, 5)]
        public static partial ReadOnlySpan<int> Int();
        
        [return: PrimitiveStaticData('A', 'B', 'C')]
        public static partial ReadOnlySpan<char> Char();
    }
}
```

### generated code(example)

<details>
<summary>MyCode.UserClass.Char.cs</summary>

```cs
using System;
using PrimitiveStaticDataGenerator;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MyCode
{
    public partial class UserClass
    {
        public static partial ReadOnlySpan<char> Char()
        {
            ReadOnlySpan<byte> span;
            if (BitConverter.IsLittleEndian)
            {
                span = new byte[]
                {
                    65,
                    0,
                    66,
                    0,
                    67,
                    0
                };
            }
            else
            {
                span = new byte[]
                {
                    0,
                    65,
                    0,
                    66,
                    0,
                    67
                };
            }

            return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, char>(ref MemoryMarshal.GetReference(span)), 3);
        }
    }
}
```
</details>

<details>
<summary>MyCode.UserClass.Int.cs</summary>

```cs
using System;
using PrimitiveStaticDataGenerator;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MyCode
{
    public partial class UserClass
    {
        public static partial ReadOnlySpan<int> Int()
        {
            ReadOnlySpan<byte> span;
            if (BitConverter.IsLittleEndian)
            {
                span = new byte[]
                {
                    1,
                    0,
                    0,
                    0,
                    2,
                    0,
                    0,
                    0,
                    3,
                    0,
                    0,
                    0,
                    4,
                    0,
                    0,
                    0,
                    5,
                    0,
                    0,
                    0
                };
            }
            else
            {
                span = new byte[]
                {
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    2,
                    0,
                    0,
                    0,
                    3,
                    0,
                    0,
                    0,
                    4,
                    0,
                    0,
                    0,
                    5
                };
            }

            return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, int>(ref MemoryMarshal.GetReference(span)), 5);
        }
    }
}
```
</details>

## Supported Types
- bool
- char
- sbyte
- byte
- short
- ushort
- int
- uint
- long
- ulong
- float
- double
- string
