using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Veldrid.Tests
{
    public class FormatSizeHelpersTests : IDisposable
    {
        private TraceListener[] traceListeners;

        public FormatSizeHelpersTests()
        {
            // temporarily disables debug trace listeners to prevent Debug.Assert
            // from causing test failures in cases where we're explicitly trying
            // to test invalid inputs
            traceListeners = new TraceListener[Trace.Listeners.Count];
            Trace.Listeners.CopyTo(traceListeners, 0);
            Trace.Listeners.Clear();
        }

        public void Dispose()
        {
            Trace.Listeners.AddRange(traceListeners);
        }

        [Fact]
        public void GetSizeInBytesDefinedForAllVertexElementFormats()
        {
            foreach (VertexElementFormat format in System.Enum.GetValues(typeof(VertexElementFormat)))
            {
                Assert.True(0 < FormatSizeHelpers.GetSizeInBytes(format));
            }
        }

        private static HashSet<PixelFormat> CompressedPixelFormats = new HashSet<PixelFormat>() {
            PixelFormat.Bc1RgbaUNorm,
            PixelFormat.Bc1RgbaUNormSRgb,
            PixelFormat.Bc1RgbUNorm,
            PixelFormat.Bc1RgbUNormSRgb,

            PixelFormat.Bc2UNorm,
            PixelFormat.Bc2UNormSRgb,

            PixelFormat.Bc3UNorm,
            PixelFormat.Bc3UNormSRgb,

            PixelFormat.Bc4SNorm,
            PixelFormat.Bc4UNorm,

            PixelFormat.Bc5SNorm,
            PixelFormat.Bc5UNorm,

            PixelFormat.Bc7UNorm,
            PixelFormat.Bc7UNormSRgb,

            PixelFormat.Etc2R8G8B8A1UNorm,
            PixelFormat.Etc2R8G8B8A8UNorm,
            PixelFormat.Etc2R8G8B8UNorm,
        };
        private static IEnumerable<PixelFormat> UncompressedPixelFormats
            = System.Enum.GetValues(typeof(PixelFormat)).Cast<PixelFormat>()
                .Where(format => !CompressedPixelFormats.Contains(format));
        public static IEnumerable<object[]> CompressedPixelFormatMemberData => CompressedPixelFormats.Select(format => new object[] { format });
        public static IEnumerable<object[]> UncompressedPixelFormatMemberData => UncompressedPixelFormats.Select(format => new object[] { format });

        [Theory]
        [MemberData(nameof(UncompressedPixelFormatMemberData))]
        public void GetSizeInBytesDefinedForAllNonCompressedPixelFormats(PixelFormat format)
        {
            Assert.True(0 < FormatSizeHelpers.GetSizeInBytes(format));
        }

        [Theory]
        [MemberData(nameof(CompressedPixelFormatMemberData))]
        public void GetSizeInBytesThrowsForAllCompressedPixelFormats(PixelFormat format)
        {
            Assert.ThrowsAny<VeldridException>(() => FormatSizeHelpers.GetSizeInBytes(format));
        }
    }
}
