using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using MetadataImageOptimizer.Settings;
using Playnite.SDK;

namespace MetadataImageOptimizer.Services
{
    /// <summary>
    /// Wraps ImageOptimizer.exe (https://github.com/Fileover/ImageOptimizer).
    /// The tool never modifies its input, so callers always get a separate output file.
    /// </summary>
    public class ImageOptimizerCli
    {
        public const string ExecutableName = "ImageOptimizer.exe";

        private const int ProcessTimeoutMs = 10 * 60 * 1000;

        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly Func<MetadataImageOptimizerSettings> settingsProvider;
        private readonly string pluginFolder;
        private readonly object optionsLock = new object();

        private HashSet<string> supportedOptions;
        private string supportedOptionsPath;

        public ImageOptimizerCli(Func<MetadataImageOptimizerSettings> settingsProvider, string pluginFolder)
        {
            this.settingsProvider = settingsProvider;
            this.pluginFolder = pluginFolder;
        }

        /// <summary>
        /// Gets the executable that will be used, or <c>null</c> when none could be found.
        /// A path configured in the settings takes precedence over the bundled executable.
        /// </summary>
        public string ExecutablePath
        {
            get
            {
                var configuredPath = settingsProvider?.Invoke()?.ImageOptimizerPath;
                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
                    return File.Exists(expandedPath) ? expandedPath : null;
                }

                if (string.IsNullOrWhiteSpace(pluginFolder))
                {
                    return null;
                }

                var bundledPath = Path.Combine(pluginFolder, ExecutableName);
                if (File.Exists(bundledPath))
                {
                    return bundledPath;
                }

                var toolsPath = Path.Combine(pluginFolder, "tools", ExecutableName);
                return File.Exists(toolsPath) ? toolsPath : null;
            }
        }

        public bool IsAvailable => ExecutablePath != null;

        /// <summary>
        /// Runs the tool for a single file.
        /// </summary>
        /// <param name="inputPath">Source image. Never modified.</param>
        /// <param name="outputPath">Exact path the optimized image is written to.</param>
        /// <param name="format">Target format as understood by the tool: jpeg, png, webp or jxl.</param>
        /// <param name="lossless">Forces strict lossless output.</param>
        /// <param name="quality">Quality settings to translate into command line arguments.</param>
        /// <param name="resize">Downscale spec such as "256x256", "256x" or "x256". Null disables resizing.</param>
        /// <param name="error">Reason the optimization did not produce a file.</param>
        /// <returns><c>true</c> when <paramref name="outputPath"/> was written.</returns>
        public bool TryOptimize(
            string inputPath
            , string outputPath
            , string format
            , bool lossless
            , QualitySettings quality
            , string resize
            , out string error)
        {
            error = null;

            var executablePath = ExecutablePath;
            if (executablePath == null)
            {
                error = $"'{ExecutableName}' could not be found.";
                return false;
            }

            var arguments = BuildArguments(inputPath, outputPath, format, lossless, quality, resize);

            try
            {
                using (var process = new Process { StartInfo = CreateStartInfo(executablePath, arguments) })
                {
                    var standardOutput = new StringBuilder();
                    var standardError = new StringBuilder();

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            standardOutput.AppendLine(args.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            standardError.AppendLine(args.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(ProcessTimeoutMs))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "Failed to kill ImageOptimizer process after timeout.");
                        }

                        error = $"ImageOptimizer timed out after {ProcessTimeoutMs / 1000} seconds.";
                        return false;
                    }

                    // Ensures the asynchronous output handlers have flushed.
                    process.WaitForExit();

                    var exitCode = process.ExitCode;
                    logger.Debug(
                        $"ImageOptimizer [{format}{(lossless ? ", lossless" : string.Empty)}] '{Path.GetFileName(inputPath)}' exited with {exitCode}."
                        + Environment.NewLine + standardOutput.ToString().TrimEnd()
                        + Environment.NewLine + standardError.ToString().TrimEnd());

                    if (File.Exists(outputPath))
                    {
                        return true;
                    }

                    // Exit code 0 without output means the tool deliberately kept the original file
                    // (nothing to gain, or the settings do not apply to this image). Not an error.
                    var errorLine = FirstLine(standardError.ToString()) ?? FirstLine(standardOutput.ToString());
                    if (errorLine != null)
                    {
                        error = errorLine;
                        return false;
                    }

                    if (exitCode == 0)
                    {
                        return false;
                    }

                    error = $"ImageOptimizer exited with code {exitCode} without writing a file.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static ProcessStartInfo CreateStartInfo(string executablePath, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath
                , Arguments = arguments
                , UseShellExecute = false
                , CreateNoWindow = true
                , RedirectStandardOutput = true
                , RedirectStandardError = true
            };

            var workingDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            return startInfo;
        }

        private string BuildArguments(
            string inputPath
            , string outputPath
            , string format
            , bool lossless
            , QualitySettings quality
            , string resize)
        {
            var arguments = new StringBuilder();
            arguments.Append(Quote(inputPath)).Append(' ').Append(Quote(outputPath));
            arguments.Append(" -f ").Append(format);
            arguments.Append(" --overwrite");
            arguments.Append(" --quiet");

            var resizeOption = resize == null ? null : FindOption("--resize");
            var applyResize = resizeOption != null;
            if (applyResize)
            {
                arguments.Append(' ').Append(resizeOption).Append(' ').Append(resize);
            }

            if (lossless)
            {
                arguments.Append(" --lossless");
            }
            else if (IsJpeg(format))
            {
                arguments.Append(" -q ").Append(quality.JpgQuality.ToString(CultureInfo.InvariantCulture));
            }
            else if (IsWebp(format))
            {
                arguments.Append(" -q ").Append(quality.WebpQuality.ToString(CultureInfo.InvariantCulture));
            }

            if (IsPng(format))
            {
                arguments.Append(" --png-preset ").Append(quality.PngPreset.ToString(CultureInfo.InvariantCulture));
                if (!quality.UseZopfli)
                {
                    // Zopfli needs minutes per image at realistic resolutions.
                    arguments.Append(" --no-zopfli");
                }
            }

            if (IsWebp(format))
            {
                arguments.Append(" --method ").Append(quality.WebpMethod.ToString(CultureInfo.InvariantCulture));
            }

            if (IsJxl(format))
            {
                AppendJxlArguments(arguments, lossless, quality);
            }

            if (quality.StripMetadata)
            {
                arguments.Append(" --strip-metadata");
            }

            if (applyResize)
            {
                // A resized image is usually "larger" than nothing, but the resize itself must not
                // be dropped just because the encoded result is not smaller than the source.
                arguments.Append(" --keep-larger");
            }

            return arguments.ToString();
        }

        private static bool IsJpeg(string format)
        {
            return string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPng(string format)
        {
            return string.Equals(format, "png", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWebp(string format)
        {
            return string.Equals(format, "webp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsJxl(string format)
        {
            return string.Equals(format, "jxl", StringComparison.OrdinalIgnoreCase);
        }

        private void AppendJxlArguments(StringBuilder arguments, bool lossless, QualitySettings quality)
        {
            if (!lossless)
            {
                arguments.Append(" --distance ").Append(quality.JxlDistance.ToString("0.###", CultureInfo.InvariantCulture));
            }

            var expertFlag = FindOption("--allow-expert-options", "--allow_expert_options");
            var expert = quality.JxlExpertOptions && expertFlag != null;
            if (expert)
            {
                arguments.Append(' ').Append(expertFlag);
            }

            // Effort 11 is an expert-only value, so fall back to the documented maximum.
            var effort = quality.JxlEffort > 10 && !expert ? 10 : quality.JxlEffort;
            arguments.Append(" --effort ").Append(effort.ToString(CultureInfo.InvariantCulture));

            var brotliFlag = expert ? FindOption("--brotli-effort", "--brotli_effort") : null;
            if (brotliFlag != null)
            {
                arguments.Append(' ').Append(brotliFlag).Append(' ').Append(quality.JxlBrotliEffort.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// Returns the spelling of an option that the current executable understands, or <c>null</c>.
        /// Keeps the extension working across builds that add or rename flags.
        /// </summary>
        private string FindOption(params string[] names)
        {
            var options = GetSupportedOptions();
            foreach (var name in names)
            {
                if (options.Contains(name))
                {
                    return name;
                }
            }

            return null;
        }

        private HashSet<string> GetSupportedOptions()
        {
            var executablePath = ExecutablePath;

            lock (optionsLock)
            {
                if (supportedOptions != null && string.Equals(supportedOptionsPath, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return supportedOptions;
                }

                supportedOptionsPath = executablePath;
                supportedOptions = new HashSet<string>(StringComparer.Ordinal);

                if (executablePath == null)
                {
                    return supportedOptions;
                }

                try
                {
                    using (var process = Process.Start(CreateStartInfo(executablePath, "--help")))
                    {
                        if (process == null)
                        {
                            return supportedOptions;
                        }

                        var helpText = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(10000);

                        foreach (var token in helpText.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '=' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var option = token.TrimEnd('.', ')', ',', ']');

                            var bracketIndex = option.IndexOf('[');
                            if (bracketIndex > 0)
                            {
                                option = option.Substring(0, bracketIndex);
                            }

                            if (option.Length > 2 && option.StartsWith("--", StringComparison.Ordinal))
                            {
                                supportedOptions.Add(option);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to read the ImageOptimizer command line options.");
                }

                return supportedOptions;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? null : lines[0].Trim();
        }
    }
}
