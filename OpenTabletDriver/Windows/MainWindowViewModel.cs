using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenTabletDriver.Models;
using OpenTabletDriver.Plugins;
using OpenTabletDriver.Tools;
using ReactiveUI;
using TabletDriverLib;
using TabletDriverLib.Interop;
using TabletDriverLib.Interop.Display;
using TabletDriverPlugin;
using TabletDriverPlugin.Logging;
using TabletDriverPlugin.Tablet;

namespace OpenTabletDriver.Windows
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
        }

        public async Task Initialize()
        {
            Log.Output += HandleLogOutput;

            Driver = new Driver();

            VirtualScreen = Platform.VirtualScreen;
            Log.Write("Display", $"Detected displays: {string.Join(", ", VirtualScreen.Displays)}");

            if (Program.ConfigurationDirectory.Exists)
            {
                Log.Write("Detect", $"Configuration directory: {Program.ConfigurationDirectory.FullName}");
                var tablets = LoadTablets(Program.ConfigurationDirectory);
                Tablets = new ObservableCollection<TabletProperties>(tablets);
                DetectTablets();
            }

            if (Program.PluginDirectory.Exists)
            {
                Log.Write("Plugins", $"Plugin directory: {Program.PluginDirectory.FullName}");
                await LoadPlugins(Program.PluginDirectory);
            }

            if (Program.SettingsDirectory.Exists)
            {
                var settingsPath = Path.Join(Program.SettingsDirectory.FullName, "settings.xml");
                var settingsFile = new FileInfo(settingsPath);
                if (settingsFile.Exists)
                {
                    var settings = LoadSettings(settingsFile);
                    if (settings != null)
                        ApplySettings(settings);
                }
                else
                    ApplySettings(DefaultSettings);
            }
            else
                ApplySettings(DefaultSettings);

            UpdateTheme(Settings.Theme);
            Log.Write("Settings", $"Using theme {Settings.Theme}");
        }

        #region Properties
            
        private ObservableCollection<LogMessage> _messages = new ObservableCollection<LogMessage>();        
        public ObservableCollection<LogMessage> Messages
        {
            set => this.RaiseAndSetIfChanged(ref _messages, value);
            get => _messages;
        }

        private LogMessage _status;
        public LogMessage StatusMessage
        {
            set => this.RaiseAndSetIfChanged(ref _status, value);
            get => _status;
        }
        
        private Driver _driver;
        public Driver Driver
        {
            set => this.RaiseAndSetIfChanged(ref _driver, value);
            get => _driver;
        }

        private Settings _settings;
        public Settings Settings
        {
            set
            {
                this.RaiseAndSetIfChanged(ref _settings, value);
            }
            get => _settings;
        }

        private ObservableCollection<TabletProperties> _tablets;
        public ObservableCollection<TabletProperties> Tablets
        {
            set => this.RaiseAndSetIfChanged(ref _tablets, value);
            get => _tablets;
        }

        private TabletProperties _tablet;
        public TabletProperties Tablet
        {
            set => this.RaiseAndSetIfChanged(ref _tablet, value);
            get => _tablet;
        }

        private IVirtualScreen _virtualScreen;
        public IVirtualScreen VirtualScreen
        {
            set => this.RaiseAndSetIfChanged(ref _virtualScreen, value);
            get => _virtualScreen;
        }

        private IDisplay _selectedDisplay;
        public IDisplay SelectedDisplay
        {
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDisplay, value);
                SelectDisplay(value);
            }
            get => _selectedDisplay;
        }

        public bool DriverEnabled
        {
            set
            {
                Driver.BindingEnabled = value;
                this.RaisePropertyChanged();
            }
            get => Driver?.BindingEnabled ?? false;
        }

        #endregion

        #region Plugins
            
        public async Task LoadPlugins(DirectoryInfo directory)
        {
            foreach (var path in Directory.GetFiles(directory.FullName, "*.dll", SearchOption.AllDirectories))
            {
                var file = new FileInfo(path);
                await PluginManager.AddPlugin(file);
            }

            var filters = from filter in PluginManager.GetChildTypes<IFilter>()
                where !filter.IsInterface
                where !filter.IsPluginIgnored()
                select new PluginReference(filter.FullName);
            PluginFilters = new ObservableCollection<PluginReference>(filters);
            PluginFilters.Insert(0, PluginReference.Disable);

            var outputModes = from mode in PluginManager.GetChildTypes<IOutputMode>()
                where !mode.IsInterface
                where !mode.IsPluginIgnored()
                select new PluginReference(mode.FullName);
            OutputModes = new ObservableCollection<PluginReference>(outputModes);
        }

        private bool _absolute;
        public bool IsAbsolute
        {
            set => this.RaiseAndSetIfChanged(ref _absolute, value);
            get => _absolute;
        }

        private bool _isRelative;
        public bool IsRelative
        {
            set => this.RaiseAndSetIfChanged(ref _isRelative, value);
            get => _isRelative;
        }

        private bool _isBindable;
        public bool IsBindable
        {
            set => this.RaiseAndSetIfChanged(ref _isBindable, value);
            get => _isBindable;
        }

        private bool _isFilterable;
        public bool IsFilterable
        {
            set => this.RaiseAndSetIfChanged(ref _isFilterable, value);
            get => _isFilterable;
        }

        private ObservableCollection<PluginReference> _pluginFilters;
        public ObservableCollection<PluginReference> PluginFilters
        {
            set => this.RaiseAndSetIfChanged(ref _pluginFilters, value);
            get => _pluginFilters;
        }

        private PluginReference _filter;
        public PluginReference CurrentFilter
        {
            set
            {
                if (value == PluginReference.Disable)
                    value = null;
                Settings.ActiveFilterName = value?.Path;
                FilterTemplate = value?.Construct<IFilter>();
                this.RaiseAndSetIfChanged(ref _filter, value);
            }
            get => _filter;
        }

        private ObservableCollection<PluginReference> _outputModes;
        public ObservableCollection<PluginReference> OutputModes
        {
            set => this.RaiseAndSetIfChanged(ref _outputModes, value);
            get => _outputModes;
        }

        private IFilter _filterTemplate;
        public IFilter FilterTemplate
        {
            set
            {
                var controls = PropertyTools.GetPropertyControls(value, nameof(FilterTemplate), Settings.PluginSettings);
                FilterControls = new ObservableCollection<IControl>(controls);
                this.RaiseAndSetIfChanged(ref _filterTemplate, value);
            }
            get => _filterTemplate;
        }

        private ObservableCollection<IControl> _filterControls = new ObservableCollection<IControl>();
        public ObservableCollection<IControl> FilterControls
        {
            set => this.RaiseAndSetIfChanged(ref _filterControls, value);
            get => _filterControls;
        }

        #endregion

        #region TabControl
            
        private int _tabIndex;
        public int TabIndex
        {
            set => this.RaiseAndSetIfChanged(ref _tabIndex, value);
            get => _tabIndex;
        }

        #endregion

        #region Event Handlers

        private void HandleLogOutput(object sender, LogMessage message)
        {
            if (message is DebugLogMessage)
            {
                if (Driver.Debugging)
                {
                    LogOutput(message);
                }
            }
            else
            {
                LogOutput(message);
            }
        }

        private void LogOutput(LogMessage message)
        {
            Dispatcher.UIThread.Post(() => 
            {
                Messages.Add(message);
                StatusMessage = message;
            });
        }

        #endregion

        #region Driver
            
        public IEnumerable<TabletProperties> LoadTablets(DirectoryInfo directory)
        {
            foreach (string path in Directory.GetFiles(Program.ConfigurationDirectory.FullName, "*.xml", SearchOption.AllDirectories))
            {
                var file = new FileInfo(path);
                TabletProperties tablet = null;
                
                try
                {
                    tablet = TabletProperties.Read(file);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }

                if (tablet != null)
                    yield return tablet;
            }
        }

        public bool DetectTablets()
        {
            foreach (var tablet in Tablets)
            {
                if (Driver.Open(tablet))
                {
                    Log.Write("Detect", $"Tablet found: '{tablet.TabletName}'");
                    Tablet = Driver.TabletProperties;
                    return true;
                }
            }
            Log.Write("Detect", $"No tablets found. Make sure that your tablet is connected.", true);
            return false;
        }

        public void SetOutputMode(PluginReference mode)
        {
            Settings.OutputMode = mode.Path;
            ApplySettings(Settings);
        }

        #endregion

        #region User Settings

        public Settings DefaultSettings => new Settings()
        {
            Theme = "Light",
            WindowWidth = 1280,
            WindowHeight = 720,
            OutputMode = typeof(TabletDriverLib.Output.AbsoluteMode).FullName,
            AutoHook = true,
            DisplayWidth = VirtualScreen.Width,
            DisplayHeight = VirtualScreen.Height,
            DisplayX = VirtualScreen.Width / 2,
            DisplayY = VirtualScreen.Height / 2,
            TabletWidth = Tablet.Width,
            TabletHeight = Tablet.Height,
            TabletX = Tablet.Width / 2,
            TabletY = Tablet.Height / 2,
            EnableClipping = true,
            TipButton = "TabletDriverLib.Binding.MouseBinding, Left",
            TipActivationPressure = 1,
            PenButtons = new ObservableCollection<string>(new string[2]),
            AuxButtons = new ObservableCollection<string>(new string[4]),
            PluginSettings = new SerializableDictionary<string, string>(),
            XSensitivity = 10,
            YSensitivity = 10,
            ResetTime = TimeSpan.FromMilliseconds(100)
        };

        public Settings LoadSettings(FileInfo file)
        {
            try
            {
                return Settings.Deserialize(file);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return null;
            }
        }

        public void SaveSettings(FileInfo file)
        {
            ApplySettings(Settings);
            Settings.Serialize(file);
        }

        public void ApplySettings(Settings settings)
        {
            Settings = settings;
            UpdatePluginSettings();
            UpdateSettings();

            Driver.OutputMode = PluginManager.ConstructObject<IOutputMode>(Settings.OutputMode);
            
            if (Driver.OutputMode != null)
            {
                Log.Write("Settings", $"Output mode: {Driver.OutputMode.GetType().FullName}");
            }

            if (Driver.OutputMode is IOutputMode outputMode)
            {                
                outputMode.Filter = PluginManager.ConstructObject<IFilter>(Settings.ActiveFilterName);
                FilterTemplate.CopyPropertiesTo(outputMode.Filter);
                if (outputMode.Filter != null)
                    Log.Write("Settings", $"Filter: {outputMode.Filter.GetType().FullName}");
                
                outputMode.TabletProperties = Driver.TabletProperties;
            }
            
            if (Driver.OutputMode is IAbsoluteMode absoluteMode)
            {
                absoluteMode.Output = new Area
                {
                    Width = Settings.DisplayWidth,
                    Height = Settings.DisplayHeight,
                    Position = new Point
                    {
                        X = Settings.DisplayX,
                        Y = Settings.DisplayY
                    }
                };
                Log.Write("Settings", $"Display area: {absoluteMode.Output}");

                absoluteMode.Input = new Area
                {
                    Width = Settings.TabletWidth,
                    Height = Settings.TabletHeight,
                    Position = new Point
                    {
                        X = Settings.TabletX,
                        Y = Settings.TabletY
                    }
                };
                Log.Write("Settings", $"Tablet area: {absoluteMode.Input}");

                absoluteMode.AreaClipping = Settings.EnableClipping;   
                Log.Write("Settings", $"Clipping: {(absoluteMode.AreaClipping ? "Enabled" : "Disabled")}");
            }

            if (Driver.OutputMode is IRelativeMode relativeMode)
            {
                relativeMode.XSensitivity = Settings.XSensitivity;
                Log.Write("Settings", $"Horizontal Sensitivity: {relativeMode.XSensitivity}");

                relativeMode.YSensitivity = Settings.YSensitivity;
                Log.Write("Settings", $"Vertical Sensitivity: {relativeMode.YSensitivity}");

                relativeMode.ResetTime = Settings.ResetTime;
                Log.Write("Settings", $"Reset time: {relativeMode.ResetTime}");
            }

            if (Driver.OutputMode is IBindingHandler<IBinding> bindingHandler)
            {
                bindingHandler.TipBinding = Tools.BindingTool.GetBinding(Settings.TipButton);
                bindingHandler.TipActivationPressure = Settings.TipActivationPressure;
                Log.Write("Settings", $"Tip Binding: '{bindingHandler.TipBinding?.Name ?? "None"}'@{bindingHandler.TipActivationPressure}%");

                if (Settings.PenButtons != null)
                {
                    for (int index = 0; index < Settings.PenButtons.Count; index++)
                        bindingHandler.PenButtonBindings[index] = Tools.BindingTool.GetBinding(Settings.PenButtons[index]);

                    Log.Write("Settings", $"Pen Bindings: " + string.Join(", ", bindingHandler.PenButtonBindings));
                }

                if (Settings.AuxButtons != null)
                {
                    for (int index = 0; index < Settings.AuxButtons.Count; index++)
                        bindingHandler.AuxButtonBindings[index] = Tools.BindingTool.GetBinding(Settings.AuxButtons[index]);

                    Log.Write("Settings", $"Express Key Bindings: " + string.Join(", ", bindingHandler.AuxButtonBindings));
                }
            }

            if (Settings.AutoHook)
            {
                DriverEnabled = true;
                Log.Write("Settings", "Driver is auto-enabled.");
            }

            Log.Write("Settings", "Applied all settings.");

            UpdateControlVisibility();
        }

        public void UpdateSettings()
        {
            CurrentFilter = PluginFilters?.FirstOrDefault(f => f.Path == Settings.ActiveFilterName);
        }

        public void UpdatePluginSettings()
        {
            var filterSettings = PluginTools.GetPluginSettings(FilterTemplate);
            foreach (var pair in filterSettings)
            {
                if (Settings.PluginSettings.ContainsKey(pair.Item1))
                    Settings.PluginSettings[pair.Item1] = pair.Item2;
                else
                    Settings.PluginSettings.Add(pair.Item1, pair.Item2);
            }
        }

        public void UpdateControlVisibility()
        {
            IsAbsolute = Driver.OutputMode is IAbsoluteMode;
            IsRelative = Driver.OutputMode is IRelativeMode;
            IsBindable = Driver.OutputMode is IBindingHandler<IBinding>;
            IsFilterable = Driver.OutputMode is IOutputMode;
            if (TabIndex == 0 && !IsAbsolute)
                TabIndex = 1;
            if (TabIndex == 1 && !IsRelative)
                TabIndex = 2;
            if (TabIndex == 2 && !IsBindable)
                TabIndex = 3;
            if (TabIndex == 3 && !IsFilterable)
                TabIndex = 4;
        }

        public bool UpdateTabletWidth(TabletProperties tablet)
        {
            if (Settings.TabletWidth == 0 || Settings.TabletHeight == 0)
            {
                Settings.TabletWidth = tablet.Width;
                Settings.TabletHeight = tablet.Height;
                Settings.TabletX = tablet.Width / 2;
                Settings.TabletY = tablet.Height / 2;
                return true;
            }
            else
            {
                return false;
            }
        }

        public void UpdateTheme(string name)
        {
            var theme = Themes.Parse(name);
            App.SetTheme(theme);
            Settings.Theme = name;
        }

        public async Task UpdateBinding(string source)
        {
            var binding = GetBinding(source);
            var bindingConfig = new BindingConfig(binding);
            await bindingConfig.ShowDialog(this.GetParentWindow());
            SetBinding(source, bindingConfig.Binding);
        }

        private string GetBinding(string source)
        {
            switch (source)
            {
                case "TipButton":
                    return Settings.TipButton;
                case "PenButtons[0]":
                case "PenButtons[1]":
                    var penIndex = source.Split('[', 2)[1].Trim(']').Convert<int>();
                    return Settings.PenButtons[penIndex];
                case "AuxButtons[0]":
                case "AuxButtons[1]":
                case "AuxButtons[2]":
                case "AuxButtons[3]":
                    var auxIndex = source.Split("[", 2)[1].Trim(']').Convert<int>();
                    return Settings.AuxButtons[auxIndex];
                default:
                    throw new ArgumentException("Invalid binding source");
            }
        }

        private void SetBinding(string source, string binding)
        {
            if (binding == ", ")
                binding = string.Empty;
            switch (source)
            {
                case "TipButton":
                    Settings.TipButton = binding;
                    return;
                case "PenButtons[0]":
                case "PenButtons[1]":
                    var penIndex = source.Split('[', 2)[1].Trim(']').Convert<int>();
                    Settings.PenButtons[penIndex] = binding;
                    return;
                case "AuxButtons[0]":
                case "AuxButtons[1]":
                case "AuxButtons[2]":
                case "AuxButtons[3]":
                    var auxIndex = source.Split("[", 2)[1].Trim(']').Convert<int>();
                    Settings.AuxButtons[auxIndex] = binding;
                    return;
                default:
                    throw new ArgumentException("Invalid binding source");
            }
        }

        #endregion

        #region Miscellaneous Buttons
            
        public async Task ShowAbout()
        {
            var window = new About();
            await window.ShowDialog(this.GetParentWindow());
        }

        public void ResetToDefaults()
        {
            ApplySettings(DefaultSettings);
            UpdateTheme(Settings.Theme);
        }

        public void ResetWindowSize()
        {
            Settings.WindowWidth = 1280;
            Settings.WindowHeight = 720;
        }

        public void ToggleDriverEnabled()
        {
            DriverEnabled = !DriverEnabled;
        }

        public async Task LoadSettingsDialog()
        {
            var fileDialog = FileDialogs.CreateOpenFileDialog("Load settings", "OpenTabletDriver Settings", "xml");
            var paths = await fileDialog.ShowAsync(this.GetParentWindow());
            if (paths?.Length > 0)
            {
                var path = paths[0];
                var file = new FileInfo(path);
                if (file.Exists)
                {
                    try
                    {
                        ApplySettings(Settings.Deserialize(file));
                        UpdateTheme(Settings.Theme);
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex);
                    }
                }
            }
        }

        public void SaveSettingsDefault()
        {
            var path = Path.Join(Program.SettingsDirectory.FullName, "settings.xml");
            var file = new FileInfo(path); 
            SaveSettings(file);
        }

        public async Task SaveSettingsDialog()
        {
            var fileDialog = FileDialogs.CreateSaveFileDialog("Save Settings", "OpenTabletDriver Settings", "xml");
            var result = await fileDialog.ShowAsync(this.GetParentWindow());
            if (!string.IsNullOrWhiteSpace(result))
            {
                var file = new FileInfo(result);
                SaveSettings(file);
            }
        }

        public async Task OpenTabletDebugger()
        {
            var tabletDebugger = new TabletDebugger(Driver.TabletReader, Driver.AuxReader);
            await tabletDebugger.ShowDialog(this.GetParentWindow());
        }

        public async Task OpenConfigurationManager()
        {
            var configManager = new ConfigurationManager
            {
                ViewModel = new ConfigurationManagerViewModel
                {
                    Configurations = Tablets,
                    Devices = new ObservableCollection<HidSharp.HidDevice>(Driver.Devices)
                }
            };
            await configManager.ShowDialog(this.GetParentWindow()); 
        }

        public async Task OpenTabletConfigurationFolder()
        {
            var fileDialog = FileDialogs.CreateOpenFolderDialog("Opening configuration folder");
            var result = await fileDialog.ShowAsync(this.GetParentWindow());
            if (result != null)
            {
                var dir = new DirectoryInfo(result);
                LoadTablets(dir);
            }
        }

        public void SelectDisplay(IDisplay display)
        {
            Settings.DisplayWidth = display.Width;
            Settings.DisplayHeight = display.Height;
            if (display is IVirtualScreen virtualScreen)
            {
                Settings.DisplayX = virtualScreen.Width / 2;
                Settings.DisplayY = virtualScreen.Height / 2;
            }
            else
            {
                Settings.DisplayX = display.Position.X + VirtualScreen.Position.X + (display.Width / 2);
                Settings.DisplayY = display.Position.Y + VirtualScreen.Position.Y + (display.Height / 2);
            }
        }

        #endregion
    }
}