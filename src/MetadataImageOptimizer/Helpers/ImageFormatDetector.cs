using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace MetadataImageOptimizer.Helpers
{
    public enum ImageFormat
    {
        Unknown
        , Jpeg
        , Png
        , WebP
        , JpegXl
        , Bmp
        , Gif
        , Tiff
        , Ico
    }

    /// <summary>
    /// Detects image formats and dimensions by file signature and container headers.
    /// ImageOptimizer.exe uses signatures as well, so extensions that lie (e.g. a PNG named .jpg)
    /// must not drive decisions.
    /// </summary>
    public static class ImageFormatDetector
    {
        public static ImageFormat Detect(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var header = new byte[12];
                    var read = stream.Read(header, 0, header.Length);
                    return Detect(header, read);
                }
            }
            catch
            {
                return ImageFormat.Unknown;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="path"/> is a WebP carrying a lossy VP8 chunk.
        /// </summary>
        public static bool IsLossyWebP(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var riff = new byte[12];
                    if (ReadExact(stream, riff, 12) < 12 || !IsWebPHeader(riff, 12))
                    {
                        return false;
                    }

                    var chunkHeader = new byte[8];
                    while (ReadExact(stream, chunkHeader, 8) == 8)
                    {
                        var chunkLength = ReadUInt32LE(chunkHeader, 4);
                        if (MatchFourCc(chunkHeader, 0, 'V', 'P', '8', ' '))
                        {
                            return true;
                        }

                        if (MatchFourCc(chunkHeader, 0, 'V', 'P', '8', 'L'))
                        {
                            return false;
                        }

                        var skip = (long)chunkLength + (chunkLength & 1u);
                        if (stream.Position + skip > stream.Length)
                        {
                            break;
                        }

                        stream.Seek(skip, SeekOrigin.Current);
                    }
                }
            }
            catch
            {
                // Ignore IO errors and treat as undetermined.
            }

            return false;
        }

        /// <summary>
        /// Reads the display dimensions of an image from its header without decoding pixel data.
        /// </summary>
        public static bool TryGetDimensions(string path, ImageFormat format, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    switch (format)
                    {
                        case ImageFormat.Png:
                            return TryGetPngDimensions(stream, out width, out height);
                        case ImageFormat.Jpeg:
                            return TryGetJpegDimensions(stream, out width, out height);
                        case ImageFormat.WebP:
                            return TryGetWebPDimensions(stream, out width, out height);
                        case ImageFormat.JpegXl:
                            return TryGetJpegXlDimensions(stream, out width, out height);
                        case ImageFormat.Bmp:
                        case ImageFormat.Gif:
                        case ImageFormat.Tiff:
                            return TryGetWpfDimensions(stream, out width, out height);
                        default:
                            return false;
                    }
                }
            }
            catch
            {
                width = 0;
                height = 0;
                return false;
            }
        }

        private static ImageFormat Detect(byte[] header, int length)
        {
            if (length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return ImageFormat.Jpeg;
            }

            if (length >= 8
                && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                return ImageFormat.Png;
            }

            if (length >= 6 && header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8')
            {
                return ImageFormat.Gif;
            }

            if (length >= 2 && header[0] == 'B' && header[1] == 'M')
            {
                return ImageFormat.Bmp;
            }

            if (length >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00)
            {
                return ImageFormat.Ico;
            }

            if (IsWebPHeader(header, length))
            {
                return ImageFormat.WebP;
            }

            // JXL container ("JXL " box) and raw codestream (0xFF0A) signatures.
            if (length >= 12
                && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x0C
                && header[4] == 'J' && header[5] == 'X' && header[6] == 'L' && header[7] == ' ')
            {
                return ImageFormat.JpegXl;
            }

            if (length >= 2 && header[0] == 0xFF && header[1] == 0x0A)
            {
                return ImageFormat.JpegXl;
            }

            if (length >= 4
                && ((header[0] == 'I' && header[1] == 'I' && header[2] == 0x2A && header[3] == 0x00)
                    || (header[0] == 'M' && header[1] == 'M' && header[2] == 0x00 && header[3] == 0x2A)))
            {
                return ImageFormat.Tiff;
            }

            return ImageFormat.Unknown;
        }

        private static bool IsWebPHeader(byte[] header, int length)
        {
            return length >= 12
                && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P';
        }

        private static bool TryGetPngDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;

            var signature = new byte[8];
            if (ReadExact(stream, signature, 8) < 8)
            {
                return false;
            }

            var orientation = 1;
            var chunkHeader = new byte[8];
            while (ReadExact(stream, chunkHeader, 8) == 8)
            {
                var length = ReadUInt32BE(chunkHeader, 0);
                if (MatchFourCc(chunkHeader, 4, 'I', 'H', 'D', 'R') && length >= 8)
                {
                    var ihdr = new byte[8];
                    if (ReadExact(stream, ihdr, 8) < 8)
                    {
                        return false;
                    }

                    var w = ReadUInt32BE(ihdr, 0);
                    var h = ReadUInt32BE(ihdr, 4);
                    if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
                    {
                        return false;
                    }

                    width = (int)w;
                    height = (int)h;

                    var remaining = (long)length - 8 + 4;
                    if (stream.Position + remaining > stream.Length)
                    {
                        break;
                    }

                    stream.Seek(remaining, SeekOrigin.Current);
                }
                else if (MatchFourCc(chunkHeader, 4, 'e', 'X', 'I', 'f') && length >= 8 && length <= 65536)
                {
                    var exif = new byte[length];
                    if (ReadExact(stream, exif, (int)length) < (int)length)
                    {
                        break;
                    }

                    orientation = ParseExifOrientation(exif, 0, exif.Length);
                    if (stream.Position + 4 <= stream.Length)
                    {
                        stream.Seek(4, SeekOrigin.Current);
                    }
                    break;
                }
                else if (MatchFourCc(chunkHeader, 4, 'I', 'E', 'N', 'D'))
                {
                    break;
                }
                else
                {
                    var skip = (long)length + 4;
                    if (stream.Position + skip > stream.Length)
                    {
                        break;
                    }

                    stream.Seek(skip, SeekOrigin.Current);
                }
            }

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            ApplyOrientation(orientation, ref width, ref height);
            return true;
        }

        private static bool TryGetJpegDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
            {
                return false;
            }

            var orientation = 1;
            var lenBuf = new byte[2];
            while (stream.Position < stream.Length)
            {
                int b;
                do
                {
                    b = stream.ReadByte();
                    if (b < 0)
                    {
                        goto Done;
                    }
                }
                while (b != 0xFF);

                do
                {
                    b = stream.ReadByte();
                    if (b < 0)
                    {
                        goto Done;
                    }
                }
                while (b == 0xFF);

                var marker = b;
                if (marker == 0xD9 || marker == 0xDA)
                {
                    break;
                }

                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    continue;
                }

                if (ReadExact(stream, lenBuf, 2) < 2)
                {
                    break;
                }

                var segLength = (lenBuf[0] << 8) | lenBuf[1];
                if (segLength < 2)
                {
                    break;
                }

                var payloadLength = segLength - 2;
                if (stream.Position + payloadLength > stream.Length)
                {
                    break;
                }

                if (marker == 0xE1 && orientation == 1 && payloadLength > 6 && payloadLength <= 65536)
                {
                    var seg = new byte[payloadLength];
                    if (ReadExact(stream, seg, payloadLength) < payloadLength)
                    {
                        break;
                    }

                    if (seg[0] == 'E' && seg[1] == 'x' && seg[2] == 'i' && seg[3] == 'f' && seg[4] == 0 && seg[5] == 0)
                    {
                        orientation = ParseExifOrientation(seg, 6, seg.Length - 6);
                    }
                }
                else if (IsSofMarker(marker) && payloadLength >= 5)
                {
                    var sof = new byte[5];
                    if (ReadExact(stream, sof, 5) < 5)
                    {
                        break;
                    }

                    var h = (sof[1] << 8) | sof[2];
                    var w = (sof[3] << 8) | sof[4];
                    if (w > 0 && h > 0)
                    {
                        width = w;
                        height = h;
                    }

                    if (payloadLength > 5)
                    {
                        stream.Seek(payloadLength - 5, SeekOrigin.Current);
                    }
                }
                else
                {
                    stream.Seek(payloadLength, SeekOrigin.Current);
                }
            }

        Done:
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            ApplyOrientation(orientation, ref width, ref height);
            return true;
        }

        private static bool IsSofMarker(int marker)
        {
            return (marker >= 0xC0 && marker <= 0xC3)
                || (marker >= 0xC5 && marker <= 0xC7)
                || (marker >= 0xC9 && marker <= 0xCB)
                || (marker >= 0xCD && marker <= 0xCF);
        }

        private static bool TryGetWebPDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;

            var riff = new byte[12];
            if (ReadExact(stream, riff, 12) < 12 || !IsWebPHeader(riff, 12))
            {
                return false;
            }

            var orientation = 1;
            var chunkHeader = new byte[8];
            while (ReadExact(stream, chunkHeader, 8) == 8)
            {
                var chunkLength = ReadUInt32LE(chunkHeader, 4);
                var nextChunkPos = stream.Position + chunkLength + (chunkLength & 1u);

                if (width == 0 && MatchFourCc(chunkHeader, 0, 'V', 'P', '8', 'X') && chunkLength >= 10)
                {
                    var vp8x = new byte[10];
                    if (ReadExact(stream, vp8x, 10) < 10)
                    {
                        return false;
                    }

                    width = 1 + (vp8x[4] | (vp8x[5] << 8) | (vp8x[6] << 16));
                    height = 1 + (vp8x[7] | (vp8x[8] << 8) | (vp8x[9] << 16));
                }
                else if (width == 0 && MatchFourCc(chunkHeader, 0, 'V', 'P', '8', ' ') && chunkLength >= 10)
                {
                    var vp8 = new byte[10];
                    if (ReadExact(stream, vp8, 10) < 10)
                    {
                        return false;
                    }

                    if ((vp8[0] & 1) == 0 && vp8[3] == 0x9D && vp8[4] == 0x01 && vp8[5] == 0x2A)
                    {
                        width = (vp8[6] | (vp8[7] << 8)) & 0x3FFF;
                        height = (vp8[8] | (vp8[9] << 8)) & 0x3FFF;
                    }
                }
                else if (width == 0 && MatchFourCc(chunkHeader, 0, 'V', 'P', '8', 'L') && chunkLength >= 5)
                {
                    var vp8l = new byte[5];
                    if (ReadExact(stream, vp8l, 5) < 5)
                    {
                        return false;
                    }

                    if (vp8l[0] == 0x2F)
                    {
                        var bits = ReadUInt32LE(vp8l, 1);
                        width = (int)(bits & 0x3FFFu) + 1;
                        height = (int)((bits >> 14) & 0x3FFFu) + 1;
                    }
                }
                else if (orientation == 1 && MatchFourCc(chunkHeader, 0, 'E', 'X', 'I', 'F') && chunkLength >= 8 && chunkLength <= 65536)
                {
                    var exif = new byte[chunkLength];
                    if (ReadExact(stream, exif, (int)chunkLength) == (int)chunkLength)
                    {
                        var offset = exif.Length > 6
                            && exif[0] == 'E' && exif[1] == 'x' && exif[2] == 'i' && exif[3] == 'f' && exif[4] == 0 && exif[5] == 0
                            ? 6
                            : 0;
                        orientation = ParseExifOrientation(exif, offset, exif.Length - offset);
                    }
                }

                if (nextChunkPos > stream.Length)
                {
                    break;
                }

                stream.Position = nextChunkPos;
            }

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            ApplyOrientation(orientation, ref width, ref height);
            return true;
        }

        private static bool TryGetJpegXlDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;

            var prefix = new byte[16];
            var read = ReadExact(stream, prefix, prefix.Length);
            if (read < 2)
            {
                return false;
            }

            if (prefix[0] == 0xFF && prefix[1] == 0x0A)
            {
                return TryParseJxlCodestreamSize(prefix, read, out width, out height);
            }

            if (read < 12
                || prefix[0] != 0x00 || prefix[1] != 0x00 || prefix[2] != 0x00 || prefix[3] != 0x0C
                || prefix[4] != 'J' || prefix[5] != 'X' || prefix[6] != 'L' || prefix[7] != ' ')
            {
                return false;
            }

            stream.Position = 0;
            var boxHeader = new byte[8];
            var largeSizeBuf = new byte[8];
            while (ReadExact(stream, boxHeader, 8) == 8)
            {
                ulong boxSize = ReadUInt32BE(boxHeader, 0);
                var headerSize = 8L;
                if (boxSize == 1)
                {
                    if (ReadExact(stream, largeSizeBuf, 8) < 8)
                    {
                        return false;
                    }

                    boxSize = ((ulong)ReadUInt32BE(largeSizeBuf, 0) << 32) | ReadUInt32BE(largeSizeBuf, 4);
                    headerSize = 16L;
                }
                else if (boxSize == 0)
                {
                    boxSize = (ulong)(stream.Length - (stream.Position - 8));
                }

                if (boxSize < (ulong)headerSize)
                {
                    return false;
                }

                var payloadLength = (long)boxSize - headerSize;
                var nextBoxPos = stream.Position + payloadLength;

                if (MatchFourCc(boxHeader, 4, 'j', 'x', 'l', 'c') && payloadLength >= 2)
                {
                    var sampleLen = (int)Math.Min(payloadLength, 32L);
                    var codestream = new byte[sampleLen];
                    var got = ReadExact(stream, codestream, sampleLen);
                    return TryParseJxlCodestreamSize(codestream, got, out width, out height);
                }

                if (MatchFourCc(boxHeader, 4, 'j', 'x', 'l', 'p') && payloadLength >= 6)
                {
                    var idxBuf = new byte[4];
                    if (ReadExact(stream, idxBuf, 4) < 4)
                    {
                        return false;
                    }

                    var sequenceIndex = ReadUInt32BE(idxBuf, 0) & 0x7FFFFFFFu;
                    if (sequenceIndex == 0)
                    {
                        var sampleLen = (int)Math.Min(payloadLength - 4, 32L);
                        var codestream = new byte[sampleLen];
                        var got = ReadExact(stream, codestream, sampleLen);
                        return TryParseJxlCodestreamSize(codestream, got, out width, out height);
                    }
                }

                if (nextBoxPos > stream.Length)
                {
                    break;
                }

                stream.Position = nextBoxPos;
            }

            return false;
        }

        /// <summary>
        /// Parses ISO/IEC 18181-1 SizeHeader + ImageMetadata orientation right after the 0xFF0A signature.
        /// </summary>
        private static bool TryParseJxlCodestreamSize(byte[] data, int length, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (length < 3 || data[0] != 0xFF || data[1] != 0x0A)
            {
                return false;
            }

            var bitPos = 16;
            var totalBits = length * 8;

            var div8 = ReadBitsLsb(data, ref bitPos, totalBits, 1);
            if (div8 < 0)
            {
                return false;
            }

            long h;
            if (div8 == 1)
            {
                var v = ReadBitsLsb(data, ref bitPos, totalBits, 5);
                if (v < 0)
                {
                    return false;
                }

                h = 8L * (1L + v);
            }
            else
            {
                var dist = ReadBitsLsb(data, ref bitPos, totalBits, 2);
                if (dist < 0)
                {
                    return false;
                }

                var bits = dist == 0 ? 9 : dist == 1 ? 13 : dist == 2 ? 18 : 30;
                var v = ReadBitsLsb(data, ref bitPos, totalBits, bits);
                if (v < 0)
                {
                    return false;
                }

                h = 1L + v;
            }

            var ratio = ReadBitsLsb(data, ref bitPos, totalBits, 3);
            if (ratio < 0)
            {
                return false;
            }

            long w;
            switch (ratio)
            {
                case 0:
                    if (div8 == 1)
                    {
                        var v = ReadBitsLsb(data, ref bitPos, totalBits, 5);
                        if (v < 0)
                        {
                            return false;
                        }

                        w = 8L * (1L + v);
                    }
                    else
                    {
                        var dist = ReadBitsLsb(data, ref bitPos, totalBits, 2);
                        if (dist < 0)
                        {
                            return false;
                        }

                        var bits = dist == 0 ? 9 : dist == 1 ? 13 : dist == 2 ? 18 : 30;
                        var v = ReadBitsLsb(data, ref bitPos, totalBits, bits);
                        if (v < 0)
                        {
                            return false;
                        }

                        w = 1L + v;
                    }
                    break;
                case 1:
                    w = h;
                    break;
                case 2:
                    w = (h * 12L) / 10L;
                    break;
                case 3:
                    w = (h * 4L) / 3L;
                    break;
                case 4:
                    w = (h * 3L) / 2L;
                    break;
                case 5:
                    w = (h * 16L) / 9L;
                    break;
                case 6:
                    w = (h * 5L) / 4L;
                    break;
                default:
                    w = h * 2L;
                    break;
            }

            if (w <= 0 || h <= 0 || w > int.MaxValue || h > int.MaxValue)
            {
                return false;
            }

            // ImageMetadata starts with all_default (1 bit); if 0, extra_fields (1 bit); if 1, orientation (3 bits, 0..7 => 1..8).
            var allDefault = ReadBitsLsb(data, ref bitPos, totalBits, 1);
            var orientation = 1;
            if (allDefault == 0)
            {
                var extraFields = ReadBitsLsb(data, ref bitPos, totalBits, 1);
                if (extraFields == 1)
                {
                    var orientMinusOne = ReadBitsLsb(data, ref bitPos, totalBits, 3);
                    if (orientMinusOne >= 0)
                    {
                        orientation = (int)orientMinusOne + 1;
                    }
                }
            }

            width = (int)w;
            height = (int)h;
            ApplyOrientation(orientation, ref width, ref height);
            return true;
        }

        private static bool TryGetWpfDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
            {
                return false;
            }

            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
            return width > 0 && height > 0;
        }

        private static int ParseExifOrientation(byte[] data, int offset, int length)
        {
            if (length < 8 || offset < 0 || offset + length > data.Length)
            {
                return 1;
            }

            bool littleEndian;
            if (data[offset] == 'I' && data[offset + 1] == 'I')
            {
                littleEndian = true;
            }
            else if (data[offset] == 'M' && data[offset + 1] == 'M')
            {
                littleEndian = false;
            }
            else
            {
                return 1;
            }

            if (ReadExifUInt16(data, offset + 2, littleEndian) != 42)
            {
                return 1;
            }

            var ifdOffset = ReadExifUInt32(data, offset + 4, littleEndian);
            if (ifdOffset < 8 || ifdOffset > (uint)(length - 2))
            {
                return 1;
            }

            var pos = offset + (int)ifdOffset;
            int entries = ReadExifUInt16(data, pos, littleEndian);
            pos += 2;

            for (var i = 0; i < entries && pos + 12 <= offset + length; i++, pos += 12)
            {
                var tag = ReadExifUInt16(data, pos, littleEndian);
                if (tag == 0x0112)
                {
                    var val = ReadExifUInt16(data, pos + 8, littleEndian);
                    return val >= 1 && val <= 8 ? val : 1;
                }
            }

            return 1;
        }

        private static void ApplyOrientation(int orientation, ref int width, ref int height)
        {
            if (orientation >= 5 && orientation <= 8)
            {
                var tmp = width;
                width = height;
                height = tmp;
            }
        }

        private static long ReadBitsLsb(byte[] data, ref int bitPos, int totalBits, int count)
        {
            if (bitPos + count > totalBits)
            {
                return -1;
            }

            long value = 0;
            for (var i = 0; i < count; i++)
            {
                var byteIdx = bitPos >> 3;
                var bitIdx = bitPos & 7;
                if (((data[byteIdx] >> bitIdx) & 1) != 0)
                {
                    value |= 1L << i;
                }

                bitPos++;
            }

            return value;
        }

        private static int ReadExact(Stream stream, byte[] buffer, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static bool MatchFourCc(byte[] b, int offset, char c0, char c1, char c2, char c3)
        {
            return b[offset] == (byte)c0
                && b[offset + 1] == (byte)c1
                && b[offset + 2] == (byte)c2
                && b[offset + 3] == (byte)c3;
        }

        private static uint ReadUInt32BE(byte[] b, int offset)
        {
            return ((uint)b[offset] << 24)
                | ((uint)b[offset + 1] << 16)
                | ((uint)b[offset + 2] << 8)
                | b[offset + 3];
        }

        private static uint ReadUInt32LE(byte[] b, int offset)
        {
            return b[offset]
                | ((uint)b[offset + 1] << 8)
                | ((uint)b[offset + 2] << 16)
                | ((uint)b[offset + 3] << 24);
        }

        private static ushort ReadExifUInt16(byte[] b, int offset, bool littleEndian)
        {
            return littleEndian
                ? (ushort)(b[offset] | (b[offset + 1] << 8))
                : (ushort)((b[offset] << 8) | b[offset + 1]);
        }

        private static uint ReadExifUInt32(byte[] b, int offset, bool littleEndian)
        {
            return littleEndian ? ReadUInt32LE(b, offset) : ReadUInt32BE(b, offset);
        }
    }
}
