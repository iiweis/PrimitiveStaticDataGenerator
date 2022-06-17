﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp.RuntimeBinder;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PrimitiveStaticDataGenerator.Tests
{
    [Collection("EndianTest")]
    public partial class BigEndianTest
    {
        private readonly ITestOutputHelper output;

        public BigEndianTest(ITestOutputHelper output)
        {
#pragma warning disable CS0436
            BitConverter.IsLittleEndian = false;
#pragma warning restore CS0436

            this.output = output;
        }

        [Fact]
        public void Test()
        {
            ReadOnlySpan<int> generateIntSpan = Int();
            ReadOnlySpan<int> intSpan = new int[] { 0, 1, -1, int.MinValue, int.MaxValue }.ToByteSpan(isLittleEndian: false);

            output.WriteLine($"Generate: {generateIntSpan.ToCommaSeparatedString()}");
            output.WriteLine($"UserCode: {intSpan.ToCommaSeparatedString()}");

            Assert.True(generateIntSpan.SequenceEqual(intSpan));
        }

        [return: PrimitiveStaticData(0, 1, -1, int.MinValue, int.MaxValue)]
        public static partial ReadOnlySpan<int> Int();
    }
}