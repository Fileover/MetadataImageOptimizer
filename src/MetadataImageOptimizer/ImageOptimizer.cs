using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using MetadataImageOptimizer.Helpers;
using MetadataImageOptimizer.Services;
using MetadataImageOptimizer.Settings;
using Playnite.SDK;

namespace MetadataImageOptimizer
{
    public static class ImageOptimizer
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private enum TargetFormat
        {
            Jpeg
            , Png
            , WebP
            , JpegXl
        }

        /// <summary>
        /// Optimizes an image with the specified requirements
        /// </summary>
        /// <param name="imagePath">The full path to the image that should be optimized</param>
        /// <param name="imageSettings">The settings to use for optimizing the image</param>
        /// <param name="qualitySettings">The quality settings for optimizing the image</param>
        /// <param name="cli">The ImageOptimizer executable wrapper</param>
        /// <returns>
        /// The path of the optimized image, or <paramref name="imagePath"/> when nothing changed.
        /// Temporary files are owned by the caller and must be deleted after use.
        /// </returns>
        public static string Optimize(string imagePath, ImageTypeSettings imageSettings, QualitySettings qualitySettings, ImageOptimizerCli cli)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return imagePath;
            }

            if (cli == null || !cli.IsAvailable)
            {
                logger.Warn($"Skipping '{imagePath}' because '{ImageOptimizerCli.ExecutableName}' is not available.");
                return imagePath;
            }

            string intermediatePath = null;
            try
            {
                var sourcePath = imagePath;
                var sourceFormat = ImageFormatDetector.Detect(imagePath);

                if (sourceFormat == ImageFormat.Ico)
                {
                    intermediatePath = ExtractLargestIconFrame(imagePath);
                    if (intermediatePath == null)
                    {
                        return imagePath;
                    }

                    sourcePath = intermediatePath;
                    sourceFormat = ImageFormat.Png;
                }

                var targetFormat = ResolveTargetFormat(imageSettings.Format, sourceFormat);
                if (targetFormat == null)
                {
                    logger.Debug($"Unsupported image format for '{imagePath}'. Skipping.");
                    return imagePath;
                }

                // PNG -> JPEG cannot be lossless. The tool would skip the file, so fall back to lossy.
                var lossless = imageSettings.Lossless
                    && !(targetFormat == TargetFormat.Jpeg && sourceFormat != ImageFormat.Jpeg);

                var outputPath = CreateTempFilePath(Extension(targetFormat.Value));
                if (!cli.TryOptimize(sourcePath, outputPath, CliName(targetFormat.Value), lossless, qualitySettings, BuildResize(imageSettings), out var error))
                {
                    if (error == null)
                    {
                        logger.Debug($"ImageOptimizer kept '{imagePath}' unchanged.");
                    }
                    else
                    {
                        logger.Warn($"Failed to optimize '{imagePath}': {error}");
                    }

                    DeleteFile(outputPath);
                    return imagePath;
                }

                return outputPath;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error while optimizing '{imagePath}'.");
                return imagePath;
            }
            finally
            {
                DeleteFile(intermediatePath);
            }
        }

        /// <summary>
        /// Builds the "WxH" spec understood by --resize. Either side may be empty ("256x", "x256").
        /// </summary>
        private static string BuildResize(ImageTypeSettings imageSettings)
        {
            var maxWidth = imageSettings.MaxWidth;
            var maxHeight = imageSettings.MaxHeight;
            if (maxWidth <= 0 && maxHeight <= 0)
            {
                return null;
            }

            var width = maxWidth > 0 ? maxWidth.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var height = maxHeight > 0 ? maxHeight.ToString(CultureInfo.InvariantCulture) : string.Empty;
            return width + "x" + height;
        }

        private static TargetFormat? ResolveTargetFormat(string format, ImageFormat sourceFormat)
        {
            switch ((format ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "jpg":
                case "jpeg":
                    return TargetFormat.Jpeg;
                case "png":
                    return TargetFormat.Png;
                case "webp":
                    return TargetFormat.WebP;
                case "jxl":
                    return TargetFormat.JpegXl;
                case "original":
                case "":
                    return FromSourceFormat(sourceFormat);
                default:
                    return null;
            }
        }

        private static TargetFormat? FromSourceFormat(ImageFormat sourceFormat)
        {
            switch (sourceFormat)
            {
                case ImageFormat.Jpeg:
                    return TargetFormat.Jpeg;
                case ImageFormat.Png:
                    return TargetFormat.Png;
                case ImageFormat.WebP:
                    return TargetFormat.WebP;
                case ImageFormat.JpegXl:
                    return TargetFormat.JpegXl;
                case ImageFormat.Bmp:
                case ImageFormat.Gif:
                case ImageFormat.Tiff:
                    return TargetFormat.Png;
                default:
                    return null;
            }
        }

        private static string CliName(TargetFormat format)
        {
            switch (format)
            {
                case TargetFormat.Jpeg:
                    return "jpeg";
                case TargetFormat.WebP:
                    return "webp";
                case TargetFormat.JpegXl:
                    return "jxl";
                default:
                    return "png";
            }
        }

        private static string Extension(TargetFormat format)
        {
            switch (format)
            {
                case TargetFormat.Jpeg:
                    return "jpg";
                case TargetFormat.WebP:
                    return "webp";
                case TargetFormat.JpegXl:
                    return "jxl";
                default:
                    return "png";
            }
        }

        /// <summary>
        /// Extracts the largest frame of an .ico file to a PNG. ImageOptimizer does not read icons.
        /// </summary>
        private static string ExtractLargestIconFrame(string iconPath)
        {
            try
            {
                using (var stream = File.OpenRead(iconPath))
                {
                    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames.OrderByDescending(x => (long)x.PixelWidth * x.PixelHeight).FirstOrDefault();
                    if (frame == null)
                    {
                        logger.Warn($"Icon '{iconPath}' does not contain any frame.");
                        return null;
                    }

                    var pngPath = CreateTempFilePath("png");
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    using (var output = File.Create(pngPath))
                    {
                        encoder.Save(output);
                    }

                    return pngPath;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to extract an image from icon '{iconPath}'.");
                return null;
            }
        }

        private static string CreateTempFilePath(string extension)
        {
            return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "." + extension);
        }

        private static void DeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Failed to delete temporary file '{path}'.");
            }
        }
    }
}
