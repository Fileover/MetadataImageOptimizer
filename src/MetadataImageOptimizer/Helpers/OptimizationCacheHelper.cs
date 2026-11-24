using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetadataImageOptimizer.Models;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace MetadataImageOptimizer.Helpers
{
    public class OptimizationCacheHelper
    {
        private const string OptimizationCacheFileName = "optimization-cache";

        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<Guid, OptimizationCacheItem> OptimizationCache = new Dictionary<Guid, OptimizationCacheItem>();

        private static IPlayniteAPI api;
        private static string optimizationCacheFilePath;

        /// <summary>
        /// Adds or updates the specified game in the optimization cache and persists changes.
        /// </summary>
        /// <param name="game">The game to add</param>
        public static void Add(Game game)
        {
            try
            {
                var backgroundSize = GetFileSizeOrZero(game.BackgroundImage);
                var coverSize = GetFileSizeOrZero(game.CoverImage);
                var iconSize = GetFileSizeOrZero(game.Icon);

                var changed = false;
                lock (CacheLock)
                {
                    if (OptimizationCache.TryGetValue(game.Id, out var existingCacheItem))
                    {
                        if (existingCacheItem.BackgroundFileSize != backgroundSize
                            || existingCacheItem.CoverFileSize != coverSize
                            || existingCacheItem.IconFileSize != iconSize)
                        {
                            existingCacheItem.BackgroundFileSize = backgroundSize;
                            existingCacheItem.CoverFileSize = coverSize;
                            existingCacheItem.IconFileSize = iconSize;
                            changed = true;
                        }
                    }
                    else
                    {
                        OptimizationCache[game.Id] = new OptimizationCacheItem(game.Id, backgroundSize, coverSize, iconSize);
                        changed = true;
                    }

                    if (changed)
                    {
                        SaveLocked();
                    }
                }
            }
            catch (Exception ex)
            {
                var errorMsg = "Failed to store game information in optimization cache.";
                if (ex is ArgumentException)
                {
                    errorMsg += string.Format(
                        "A path for '{0}' likely contains invalid characters.{1}Background: '{2}'{1}Cover: '{3}'{1}Icon: '{4}'"
                        , game.Name
                        , Environment.NewLine
                        , game.BackgroundImage
                        , game.CoverImage
                        , game.Icon);
                }

                Logger.Error(ex, errorMsg);
            }
        }

        /// <summary>
        /// Clears all items from the optimization cache
        /// </summary>
        public static void Clear()
        {
            lock (CacheLock)
            {
                OptimizationCache.Clear();
                SaveLocked();
            }
        }

        /// <summary>
        /// Checks whether it is necessary to check the specified game's images for needed optimization
        /// </summary>
        /// <param name="game">The game to check</param>
        /// <returns><c>true</c> if the game's images should be checked; otherwise <c>false</c></returns>
        public static bool IsCheckNecessary(Game game)
        {
            OptimizationCacheItem cacheItem;
            lock (CacheLock)
            {
                if (!OptimizationCache.TryGetValue(game.Id, out cacheItem))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(game.BackgroundImage))
            {
                var backgroundPath = api.Database.GetFullFilePath(game.BackgroundImage);
                var backgroundFile = new FileInfo(backgroundPath);
                if (backgroundFile.Exists && backgroundFile.Length != cacheItem.BackgroundFileSize)
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(game.CoverImage))
            {
                var coverPath = api.Database.GetFullFilePath(game.CoverImage);
                var coverFile = new FileInfo(coverPath);
                if (coverFile.Exists && coverFile.Length != cacheItem.CoverFileSize)
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(game.Icon))
            {
                var iconPath = api.Database.GetFullFilePath(game.Icon);
                var iconFile = new FileInfo(iconPath);
                if (iconFile.Exists && iconFile.Length != cacheItem.IconFileSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Loads an existing optimization cache from disk
        /// </summary>
        /// <param name="api">The Playnite API</param>
        /// <param name="pluginUserDataPath">The path where the plugin's user data is stored</param>
        public static void Load(IPlayniteAPI api, string pluginUserDataPath)
        {
            lock (CacheLock)
            {
                OptimizationCacheHelper.api = api;
                optimizationCacheFilePath = Path.Combine(pluginUserDataPath, OptimizationCacheFileName);
                OptimizationCache.Clear();

                if (!File.Exists(optimizationCacheFilePath))
                {
                    return;
                }

                var lines = File.ReadAllLines(optimizationCacheFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split(',');

                    if (parts.Length == 4
                        && Guid.TryParse(parts[0], out var gameId)
                        && long.TryParse(parts[1], out var backgroundFileSize)
                        && long.TryParse(parts[2], out var coverFileSize)
                        && long.TryParse(parts[3], out var iconFileSize))
                    {
                        OptimizationCache[gameId] = new OptimizationCacheItem(gameId, backgroundFileSize, coverFileSize, iconFileSize);
                    }
                    else
                    {
                        Logger.Error($"Failed to parse optimize cache line: {line}");
                    }
                }
            }
        }

        /// <summary>
        /// Saves the current optimization cache to disk
        /// </summary>
        public static void Save()
        {
            lock (CacheLock)
            {
                SaveLocked();
            }
        }

        private static long GetFileSizeOrZero(string databaseRelativePath)
        {
            if (string.IsNullOrWhiteSpace(databaseRelativePath))
            {
                return 0;
            }

            var fullPath = api.Database.GetFullFilePath(databaseRelativePath);
            var file = new FileInfo(fullPath);
            return file.Exists ? file.Length : 0;
        }

        private static void SaveLocked()
        {
            if (string.IsNullOrWhiteSpace(optimizationCacheFilePath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(optimizationCacheFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllLines(
                    optimizationCacheFilePath
                    , OptimizationCache.Values.Select(
                        cacheItem => $"{cacheItem.GameId},{cacheItem.BackgroundFileSize},{cacheItem.CoverFileSize},{cacheItem.IconFileSize}"));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save optimization cache to disk.");
            }
        }
    }
}
