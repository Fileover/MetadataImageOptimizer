using System.Collections.Generic;
using Playnite.SDK.Data;

namespace MetadataImageOptimizer.Settings
{
    public class MetadataImageOptimizerSettings : ObservableObject
    {
        private bool alwaysOptimizeOnSave;
        private bool runInBackground;
        private string imageOptimizerPath;
        private AddonSettings addonSettings;
        private ImageTypeSettings background;
        private ImageTypeSettings cover;
        private ImageTypeSettings icon;
        private QualitySettings quality;

        public MetadataImageOptimizerSettings()
        {
            SetDefaults();
        }

        [DontSerialize]
        public Dictionary<string, string> AvailableImageFormats { get; } = new Dictionary<string, string>
        {
            { "jpg", "JPG" }
            , { "png", "PNG" }
            , { "webp", "WebP" }
            , { "jxl", "JPEG XL" }
            , { "original", "Keep original format" }
        };

        public bool AlwaysOptimizeOnSave { get => alwaysOptimizeOnSave; set => SetValue(ref alwaysOptimizeOnSave, value); }
        public bool RunInBackground { get => runInBackground; set => SetValue(ref runInBackground, value); }

        /// <summary>
        /// Path to ImageOptimizer.exe. Empty means the executable bundled with the extension is used.
        /// </summary>
        public string ImageOptimizerPath { get => imageOptimizerPath; set => SetValue(ref imageOptimizerPath, value); }

        public AddonSettings AddonSettings { get => addonSettings; set => SetValue(ref addonSettings, value); }
        public ImageTypeSettings Background { get => background; set => SetValue(ref background, value); }
        public ImageTypeSettings Cover { get => cover; set => SetValue(ref cover, value); }
        public ImageTypeSettings Icon { get => icon; set => SetValue(ref icon, value); }
        public QualitySettings Quality { get => quality; set => SetValue(ref quality, value); }

        private void SetDefaults()
        {
            imageOptimizerPath = null;

            addonSettings = new AddonSettings
            {
                BackgroundChangerOptimize = false
            };

            background = new ImageTypeSettings
            {
                Format = "jpg"
                , MaxHeight = 1080
                , MaxWidth = 1920
                , Optimize = false
                , Lossless = false
            };

            cover = new ImageTypeSettings
            {
                Format = "jpg"
                , MaxHeight = 900
                , MaxWidth = 600
                , Optimize = false
                , Lossless = false
            };

            icon = new ImageTypeSettings
            {
                Format = "png"
                , MaxHeight = 256
                , MaxWidth = 256
                , Optimize = false
                , Lossless = false
            };

            quality = new QualitySettings
            {
                JpgQuality = 90
                , PngPreset = 4
                , WebpQuality = 90
                , WebpMethod = 6
                , JxlDistance = 1.0
                , JxlEffort = 9
                , JxlExpertOptions = false
                , JxlBrotliEffort = 11
                , StripMetadata = false
                , UseZopfli = false
            };
        }
    }
}
