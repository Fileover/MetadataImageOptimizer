using System.IO;

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
    /// Detects image formats by file signature. ImageOptimizer.exe uses signatures as well, so
    /// extensions that lie (e.g. a PNG named .jpg) must not drive the decision.
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

            if (length >= 12
                && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
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
    }
}
