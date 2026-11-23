using System;
using System.Collections.Generic;
using System.ComponentModel;
using MetadataImageOptimizer.Helpers;
using MetadataImageOptimizer.Services;
using MetadataImageOptimizer.Settings;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace MetadataImageOptimizer.Views
{
    public class MetadataImageOptimizerSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly MetadataImageOptimizer plugin;

        private MetadataImageOptimizerSettings editingClone;
        private MetadataImageOptimizerSettings settings;

        public MetadataImageOptimizerSettingsViewModel(MetadataImageOptimizer plugin)
        {
            this.plugin = plugin;

            BrowseImageOptimizerCommand = new RelayCommand(BrowseImageOptimizer);

            Settings = LoadSettings();
        }

        private MetadataImageOptimizerSettings LoadSettings()
        {
            try
            {
                return plugin.LoadPluginSettings<MetadataImageOptimizerSettings>() ?? new MetadataImageOptimizerSettings();
            }
            catch (Exception ex)
            {
                // Settings written by an older version may not deserialize. Starting from defaults
                // is better than failing to load the add-on at all.
                logger.Error(ex, "Failed to load settings. Using defaults.");
                return new MetadataImageOptimizerSettings();
            }
        }

        public MetadataImageOptimizerSettings Settings
        {
            get => settings;
            set
            {
                if (ReferenceEquals(settings, value))
                {
                    return;
                }

                if (settings != null)
                {
                    settings.PropertyChanged -= OnSettingsPropertyChanged;
                }

                settings = value;

                if (settings != null)
                {
                    settings.PropertyChanged += OnSettingsPropertyChanged;
                }

                OnPropertyChanged(nameof(Settings));
                OnPropertyChanged(nameof(ImageOptimizerPathHint));
            }
        }

        public RelayCommand BrowseImageOptimizerCommand { get; }

        public string ImageOptimizerPathHint
        {
            get
            {
                var cli = plugin.OptimizerCli;
                return cli.IsAvailable
                    ? $"Using: {cli.ExecutablePath}"
                    : $"'{ImageOptimizerCli.ExecutableName}' was not found. Place it next to the extension, or select it above.";
            }
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            if (!Serialization.AreObjectsEqual(Settings, editingClone))
            {
                OptimizationCacheHelper.Clear();
            }

            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }

        private void BrowseImageOptimizer()
        {
            var selectedPath = plugin.Api.Dialogs.SelectFile($"Executable files|{ImageOptimizerCli.ExecutableName}|All files|*.*");
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                Settings.ImageOptimizerPath = selectedPath;
                OnPropertyChanged(nameof(ImageOptimizerPathHint));
            }
        }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MetadataImageOptimizerSettings.ImageOptimizerPath))
            {
                OnPropertyChanged(nameof(ImageOptimizerPathHint));
            }
        }
    }
}
