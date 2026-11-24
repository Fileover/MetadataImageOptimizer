using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using MetadataImageOptimizer.Addons;
using MetadataImageOptimizer.Addons.BackgroundChanger;
using MetadataImageOptimizer.Helpers;
using MetadataImageOptimizer.Models;
using MetadataImageOptimizer.Services;
using MetadataImageOptimizer.Settings;
using MetadataImageOptimizer.Views;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace MetadataImageOptimizer
{
    public class MetadataImageOptimizer : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly List<SupportedAddonBase> supportedAddons;
        private readonly ImageOptimizerCli imageOptimizerCli;
        private readonly ConcurrentDictionary<Guid, byte> inProcessGameIds = new ConcurrentDictionary<Guid, byte>();

        private MetadataImageOptimizerSettingsViewModel settingsVm { get; set; }

        public override Guid Id { get; } = Guid.Parse("17b571ff-6ffe-4bea-ad25-32e52b54f9d3");

        public ImageOptimizerCli OptimizerCli => imageOptimizerCli;

        public IPlayniteAPI Api => api;

        public MetadataImageOptimizer(IPlayniteAPI api) : base(api)
        {
            this.api = api;
            settingsVm = new MetadataImageOptimizerSettingsViewModel(this);
            imageOptimizerCli = new ImageOptimizerCli(() => settingsVm.Settings, GetPluginFolder());
            supportedAddons = new List<SupportedAddonBase> { new BackgroundChangerOptimizer(api, settingsVm, imageOptimizerCli) };
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public static string GetPluginFolder()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrWhiteSpace(assemblyPath) ? null : Path.GetDirectoryName(assemblyPath);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Optimize images",
                MenuSection = "MetadataImageOptimizer",
                Action = actionArgs =>
                {
                    OptimizeGames(actionArgs.Games, settingsVm.Settings);
                }
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            // SOME kind of UI to be shown in the main menu if we're optimizing in the background
            // Gives users a way to kill it
            if (settingsVm.Settings.RunInBackground &&
                backgroundOptimizeQueue.Count > 0)
            {
                yield return new MainMenuItem()
                {
                    MenuSection = $"@MetadataImageOptimizer|Optimizing {backgroundOptimizeQueue.Count} games",
                    Description = "Cancel",
                    Action = (a) =>
                    {
                        ClearBackgroundOptimizeQueue();
                    }
                };
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settingsVm;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new MetadataImageOptimizerSettingsView();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            OptimizationCacheHelper.Load(api, GetPluginUserDataPath());

            if (!imageOptimizerCli.IsAvailable)
            {
                logger.Error($"'{ImageOptimizerCli.ExecutableName}' was not found. Image optimization is unavailable until it is provided.");
                api.Notifications.Add(
                    "metadataimageoptimizer-cli-missing"
                    , $"'{ImageOptimizerCli.ExecutableName}' was not found, so images will not be optimized.\n"
                        + "Place it next to the extension or set its path in the add-on settings."
                    , NotificationType.Error);
            }

            if (File.Exists(BackgroundOptimizeQueueFilePath))
            {
                if (settingsVm.Settings.RunInBackground)
                {
                    LoadBackgroundOptimizeQueueFromFile();
                }
                else
                {
                    File.Delete(BackgroundOptimizeQueueFilePath);
                }
            }

            api.Database.Games.ItemUpdated += OnGameUpdated;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            base.OnApplicationStopped(args);

            api.Database.Games.ItemUpdated -= OnGameUpdated;
            OptimizationCacheHelper.Save();
        }

        private void OnGameUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            var optimizerSettings = settingsVm.Settings;
            var gamesToUpdate = e.UpdatedItems;

            // Ignore games where the playing/installing status was updated
            gamesToUpdate = gamesToUpdate.Where(
                    change => change.OldData.IsInstalled == change.NewData.IsInstalled
                        && change.OldData.IsInstalling == change.NewData.IsInstalling
                        && change.OldData.IsLaunching == change.NewData.IsLaunching
                        && change.OldData.IsRunning == change.NewData.IsRunning
                        && change.OldData.IsUninstalling == change.NewData.IsUninstalling
                        && !inProcessGameIds.ContainsKey(change.NewData.Id))
                .ToList();

            if (gamesToUpdate.Count == 0)
            {
                return;
            }

            // AlwaysOptimizeOnSave is strictly for manual saves in Playnite's Edit Game dialog,
            // never for automatic library syncs, playtime updates, or install-size scans.
            var forceAllEnabledSlots = optimizerSettings.AlwaysOptimizeOnSave && IsManualGameEditSave();
            var hasEnabledAddon = optimizerSettings.AddonSettings.BackgroundChangerOptimize
                && supportedAddons.Any(x => x.IsInstalled);

            var workItems = new List<OptimizeQueueItem>();
            foreach (var change in gamesToUpdate)
            {
                var backgroundChanged = change.OldData.BackgroundImage != change.NewData.BackgroundImage;
                var coverChanged = change.OldData.CoverImage != change.NewData.CoverImage;
                var iconChanged = change.OldData.Icon != change.NewData.Icon;

                var optimizeBackground = optimizerSettings.Background.Optimize && (forceAllEnabledSlots || backgroundChanged);
                var optimizeCover = optimizerSettings.Cover.Optimize && (forceAllEnabledSlots || coverChanged);
                var optimizeIcon = optimizerSettings.Icon.Optimize && (forceAllEnabledSlots || iconChanged);
                var optimizeAddons = hasEnabledAddon && (forceAllEnabledSlots || backgroundChanged || coverChanged);

                if (optimizeBackground || optimizeCover || optimizeIcon || optimizeAddons)
                {
                    workItems.Add(new OptimizeQueueItem(change.NewData.Id, optimizeBackground, optimizeCover, optimizeIcon, optimizeAddons));
                }
            }

            if (workItems.Count == 0)
            {
                return;
            }

            if (optimizerSettings.RunInBackground)
            {
                foreach (var item in workItems)
                {
                    QueueOptimizeGame(item.GameId, item.OptimizeBackground, item.OptimizeCover, item.OptimizeIcon, item.OptimizeAddons);
                }
            }
            else
            {
                api.Dialogs.ActivateGlobalProgress(
                    globalProgress =>
                    {
                        globalProgress.ProgressMaxValue = workItems.Count;

                        foreach (var item in workItems)
                        {
                            OptimizeGame(item.GameId, item.OptimizeBackground, item.OptimizeCover, item.OptimizeIcon, item.OptimizeAddons);
                            globalProgress.CurrentProgressValue += 1;
                        }
                    }
                    , new GlobalProgressOptions("Optimizing game images...", false) { IsIndeterminate = workItems.Count == 1 });
            }
        }

        private static bool IsManualGameEditSave()
        {
            var frames = new StackTrace(false).GetFrames();
            if (frames == null)
            {
                return false;
            }

            foreach (var frame in frames)
            {
                var declaringType = frame.GetMethod()?.DeclaringType?.FullName;
                if (declaringType != null
                    && (declaringType.IndexOf("GameEditViewModel", StringComparison.Ordinal) >= 0
                        || declaringType.IndexOf("GameEditWindow", StringComparison.Ordinal) >= 0))
                {
                    return true;
                }
            }

            return false;
        }

        private void OptimizeGames(List<Game> games, MetadataImageOptimizerSettings optimizerSettings)
        {
            if (games.Count == 0)
            {
                return;
            }

            var optimizeAddons = optimizerSettings.AddonSettings.BackgroundChangerOptimize
                && supportedAddons.Any(x => x.IsInstalled);

            if (optimizerSettings.RunInBackground)
            {
                foreach (var game in games)
                {
                    QueueOptimizeGame(
                        game.Id
                        , optimizerSettings.Background.Optimize
                        , optimizerSettings.Cover.Optimize
                        , optimizerSettings.Icon.Optimize
                        , optimizeAddons);
                }

                logger.Info($"Added {games.Count} games to queue.");
                api.Dialogs.ShowMessage($"Queued up {games.Count} games to optimize. Optimizing in the background.", "Queued up optimization");
            }
            else
            {
                api.Dialogs.ActivateGlobalProgress(
                    globalProgress =>
                    {
                        globalProgress.ProgressMaxValue = games.Count;

                        foreach (var game in games)
                        {
                            OptimizeGame(
                                game.Id
                                , optimizerSettings.Background.Optimize
                                , optimizerSettings.Cover.Optimize
                                , optimizerSettings.Icon.Optimize
                                , optimizeAddons);

                            globalProgress.CurrentProgressValue += 1;
                        }
                    }
                    , new GlobalProgressOptions("Optimizing game images...", false) { IsIndeterminate = games.Count == 1 });
            }
        }

        private void OptimizeGame(Guid gameId, bool optimizeBackground, bool optimizeCover, bool optimizeIcon, bool optimizeAddons = true)
        {
            // The database update at the end of this method raises ItemUpdated for the same game.
            // Without this guard that update would start another optimization of the same images.
            inProcessGameIds[gameId] = 0;
            try
            {
                OptimizeGameImages(gameId, optimizeBackground, optimizeCover, optimizeIcon, optimizeAddons);
            }
            finally
            {
                inProcessGameIds.TryRemove(gameId, out _);
            }
        }

        private void OptimizeGameImages(Guid gameId, bool optimizeBackground, bool optimizeCover, bool optimizeIcon, bool optimizeAddons)
        {
            var pathsChanged = false;
            var optimizerSettings = settingsVm.Settings;

            // Ensure we have the latest copy of {game}
            var game = api.Database.Games.Get(gameId);
            if (game == null)
            {
                return;
            }

            var checkMainImages = OptimizationCacheHelper.IsCheckNecessary(game);
            if (!checkMainImages && !optimizeAddons)
            {
                logger.Debug($"Skipping '{game.Name}'. No change from previously cached information.");
                return;
            }

            if (checkMainImages)
            {
                string newBackgroundPath = null;
                string newCoverPath = null;
                string newIconPath = null;

                logger.Debug($"Processing '{game.Name}'. Background: {optimizeBackground} | Cover: {optimizeCover} | Icon: {optimizeIcon}");
                if (optimizeBackground && !string.IsNullOrWhiteSpace(game.BackgroundImage))
                {
                    try
                    {
                        var backgroundPath = api.Database.GetFullFilePath(game.BackgroundImage);
                        newBackgroundPath = ImageOptimizer.Optimize(backgroundPath, optimizerSettings.Background, optimizerSettings.Quality, imageOptimizerCli);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Error while optimizing background image for '{game.Name}'.");
                    }
                }

                if (optimizeCover && !string.IsNullOrWhiteSpace(game.CoverImage))
                {
                    try
                    {
                        var coverPath = api.Database.GetFullFilePath(game.CoverImage);
                        newCoverPath = ImageOptimizer.Optimize(coverPath, optimizerSettings.Cover, optimizerSettings.Quality, imageOptimizerCli);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Error while optimizing cover image for '{game.Name}'.");
                    }
                }

                if (optimizeIcon && !string.IsNullOrWhiteSpace(game.Icon))
                {
                    try
                    {
                        var iconPath = api.Database.GetFullFilePath(game.Icon);
                        newIconPath = ImageOptimizer.Optimize(iconPath, optimizerSettings.Icon, optimizerSettings.Quality, imageOptimizerCli);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Error while optimizing icon for '{game.Name}'.");
                    }
                }

                // Ensure we have the latest copy of {game} again in case it changed during the optimization
                game = api.Database.Games.Get(gameId) ?? game;

                if (newBackgroundPath != null && !string.Equals(newBackgroundPath, api.Database.GetFullFilePath(game.BackgroundImage), StringComparison.OrdinalIgnoreCase))
                {
                    PlayniteApi.Database.RemoveFile(game.BackgroundImage);
                    game.BackgroundImage = api.Database.AddFile(newBackgroundPath, game.Id);
                    File.Delete(newBackgroundPath);
                    pathsChanged = true;
                }

                if (newCoverPath != null && !string.Equals(newCoverPath, api.Database.GetFullFilePath(game.CoverImage), StringComparison.OrdinalIgnoreCase))
                {
                    PlayniteApi.Database.RemoveFile(game.CoverImage);
                    game.CoverImage = api.Database.AddFile(newCoverPath, game.Id);
                    File.Delete(newCoverPath);
                    pathsChanged = true;
                }

                if (newIconPath != null && !string.Equals(newIconPath, api.Database.GetFullFilePath(game.Icon), StringComparison.OrdinalIgnoreCase))
                {
                    PlayniteApi.Database.RemoveFile(game.Icon);
                    game.Icon = api.Database.AddFile(newIconPath, game.Id);
                    File.Delete(newIconPath);
                    pathsChanged = true;
                }
            }

            if (optimizeAddons)
            {
                supportedAddons.Where(x => x.IsInstalled).ForEach(addon => addon.OptimizeImages(game.Id));
            }

            if (pathsChanged)
            {
                api.Database.Games.Update(game);
            }

            if (checkMainImages)
            {
                OptimizationCacheHelper.Add(game);
            }
        }

        #region Background processing

        private readonly ConcurrentQueue<OptimizeQueueItem> backgroundOptimizeQueue = new ConcurrentQueue<OptimizeQueueItem>();
        private Thread backgroundOptimizeThread;
        private string BackgroundOptimizeQueueFilePath => Path.Combine(GetPluginUserDataPath(), "optimize-queue");

        private void QueueOptimizeGame(Guid gameId, bool optimizeBackground, bool optimizeCover, bool optimizeIcon, bool optimizeAddons = true)
        {
            var existingItem = backgroundOptimizeQueue.FirstOrDefault(x => x.GameId == gameId);
            if (existingItem != null)
            {
                existingItem.OptimizeBackground |= optimizeBackground;
                existingItem.OptimizeCover |= optimizeCover;
                existingItem.OptimizeIcon |= optimizeIcon;
                existingItem.OptimizeAddons |= optimizeAddons;
            }
            else
            {
                var queueItem = new OptimizeQueueItem(gameId, optimizeBackground, optimizeCover, optimizeIcon, optimizeAddons);
                backgroundOptimizeQueue.Enqueue(queueItem);
            }

            EnsureBackgroundThreadRunning();
        }

        private void EnsureBackgroundThreadRunning(bool force = false)
        {
            if (backgroundOptimizeThread != null && backgroundOptimizeThread.IsAlive)
            {
                if (force)
                {
                    try
                    {
                        backgroundOptimizeThread.Abort();
                    }
                    catch { }
                }
                else
                {
                    return;
                }
            }

            backgroundOptimizeThread = new Thread(BackgroundOptimizeThread_Run)
            { IsBackground = true, Name = nameof(MetadataImageOptimizer) };

            backgroundOptimizeThread.Start();
        }

        private void BackgroundOptimizeThread_Run()
        {
            var handled = 0u;

            if (backgroundOptimizeQueue.IsEmpty)
            {
                return;
            }

            while (!Environment.HasShutdownStarted && backgroundOptimizeQueue.TryDequeue(out var item))
            {
                OptimizeGame(item.GameId, item.OptimizeBackground, item.OptimizeCover, item.OptimizeIcon, item.OptimizeAddons);

                if (++handled % 100u == 0u)
                {
                    // Every {X} items we update the queue file to remove old items
                    // We don't do it EVERY file because then we'd be potentially hammering the disk
                    SaveBackgroundOptimizeQueueToFile();
                }

                if (!settingsVm.Settings.RunInBackground)
                {
                    // Clear remaining queue if background processing is turned off
                    logger.Info($"Background processing was turned off. Cleared queue of {backgroundOptimizeQueue.Count} items.");
                    api.Dialogs.ShowMessage(
                        $"Background processing was turned off. Cleared queue of {backgroundOptimizeQueue.Count} items."
                        , "MetadataImageOptimizer - Stopped background processing");
                    ClearBackgroundOptimizeQueue();
                    return;
                }
            }

            SaveBackgroundOptimizeQueueToFile();
        }

        private void ClearBackgroundOptimizeQueue()
        {
            lock (backgroundOptimizeQueue)
            {
                while (backgroundOptimizeQueue.Count > 0)
                {
                    backgroundOptimizeQueue.TryDequeue(out _);
                }
            }

            SaveBackgroundOptimizeQueueToFile();
        }

        private void LoadBackgroundOptimizeQueueFromFile()
        {
            var lines = File.ReadAllLines(BackgroundOptimizeQueueFilePath);
            logger.Info($"Loading queue from disk with {lines.Length} items...");

            foreach (var line in lines)
            {
                var parts = line.Split(',');

                if ((parts.Length == 4 || parts.Length == 5)
                    && Guid.TryParse(parts[0], out var gameId)
                    && bool.TryParse(parts[1], out var optimizeBackground)
                    && bool.TryParse(parts[2], out var optimizeCover)
                    && bool.TryParse(parts[3], out var optimizeIcon))
                {
                    var optimizeAddons = parts.Length < 5 || !bool.TryParse(parts[4], out var parsedAddons) || parsedAddons;
                    backgroundOptimizeQueue.Enqueue(new OptimizeQueueItem(gameId, optimizeBackground, optimizeCover, optimizeIcon, optimizeAddons));
                }
                else
                {
                    logger.Error($"Failed to parse optimize queue line: {line}");
                }
            }

            EnsureBackgroundThreadRunning();
        }

        private void SaveBackgroundOptimizeQueueToFile()
        {
            lock (backgroundOptimizeQueue)
            {
                File.WriteAllLines(
                    BackgroundOptimizeQueueFilePath
                    , backgroundOptimizeQueue.Select(
                        queueItem => $"{queueItem.GameId},{queueItem.OptimizeBackground},{queueItem.OptimizeCover},{queueItem.OptimizeIcon},{queueItem.OptimizeAddons}"));
            }
        }
        #endregion
    }
}
