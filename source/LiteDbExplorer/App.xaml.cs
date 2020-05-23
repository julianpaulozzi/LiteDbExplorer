using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using HL.Manager;
using LiteDbExplorer.Controls;
using LiteDbExplorer.Framework.Windows;
using LiteDbExplorer.Presentation;
using Microsoft.WindowsAPICodePack.Taskbar;
using Serilog;
using JumpList = System.Windows.Shell.JumpList;

namespace LiteDbExplorer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly string _instanceMutex = @"LiteDBExplorerInstanceMutex";
        private Mutex _appMutex;
        private bool _errorNotified;

        public bool OriginalInstance { get; private set; }

        public static Settings Settings => Settings.Current;

        public App()
        {
            Config.ConfigureLogger();

            var appCultureInfo = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = appCultureInfo;
            Thread.CurrentThread.CurrentUICulture = appCultureInfo;

            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args?.Any() == true)
            {
                Properties[@"ArbitraryArgName"] = e.Args[0];
            }

            // For now we want to allow multiple instances if app is started without args
            if (Mutex.TryOpenExisting(_instanceMutex, out var mutex))
            {
                var client = new PipeClient(Config.PipeEndpoint);

                if (e.Args.Any())
                {
                    client.InvokeCommand(AppConstants.CmdlineCommands.Open, e.Args[0]);
                    Shutdown();
                    return;
                }
            }
            else
            {
                _appMutex = new Mutex(true, _instanceMutex);
                OriginalInstance = true;
            }

            if (Resources[@"bootstrapper"] == null)
            {
                StartupUri = new System.Uri(@"Windows\MainWindow.xaml", System.UriKind.Relative);
            }
            else
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }

            base.OnStartup(e);
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            SetColorTheme();

            CreateJumpList();

            Settings.PropertyChanged -= Settings_PropertyChanged;
            Settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.ColorTheme):
                {
                    SetColorTheme();
                    break;
                }
            }
        }

        private void SetColorTheme()
        {
            ThemedHighlightingManager.Instance.SetCurrentTheme(Settings.ColorTheme == ColorTheme.Light ? "Light" : "VS2019_Dark");
            ThemeManager.SetColorTheme(Settings.ColorTheme);
        }

        private void CreateJumpList()
        {
            if (!TaskbarManager.IsPlatformSupported)
            {
                return;
            }

            var applicationPath = Assembly.GetEntryAssembly().Location;

            var jumpList = new JumpList();

            JumpList.SetJumpList(Application.Current, jumpList);

            var openDatabaseTask = new JumpTask
            {
                Title = "Open database",
                Description = $"Open LiteDB database file, version {Config.Version}",
                ApplicationPath = applicationPath,
                Arguments = @"open"
            };
            jumpList.JumpItems.Add(openDatabaseTask);

            var newDatabaseTask = new JumpTask
            {
                Title = "New database",
                Description = $"Create and open new LiteDB database, version {Config.Version}",
                ApplicationPath = applicationPath,
                Arguments = @"new"
            };
            jumpList.JumpItems.Add(newDatabaseTask);

            jumpList.Apply();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Settings.SaveSettings();

            _appMutex?.ReleaseMutex();
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error((Exception) e.ExceptionObject, "Unhandled exception: ");

            var message = "Unhandled exception occured.\n";
            
            var lastErrorLogPath = Paths.GetLastErrorLogPath();
            if (lastErrorLogPath.HasValue)
            {
                message += $"\nAdditional information written into: {lastErrorLogPath.Value}.\n";
            }

            if (e.IsTerminating)
            {
                message += "\nApplication will shutdown.\n";
            }

            if (!_errorNotified)
            {
                ShowError((Exception) e.ExceptionObject, message, "Unhandled Exception");
            }

            if (e.IsTerminating)
            {
                _errorNotified = true;
            }

            if (e.IsTerminating)
            {
                Log.CloseAndFlush();
                Process.GetCurrentProcess().Kill();
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "An unhandled exception occurred");

            var message = "Unhandled exception occured.\n";

            var lastErrorLogPath = Paths.GetLastErrorLogPath();
            if (lastErrorLogPath.HasValue)
            {
                message += $"\nAdditional information written into: {lastErrorLogPath.Value}.\n";
            }

            if (!e.Handled)
            {
                message += "\nApplication will shutdown.\n";
            }

            if (!_errorNotified)
            {
                ShowError(e.Exception, message, "Unhandled Exception");
            }

            if (!e.Handled)
            {
                _errorNotified = true;
            }
        }

        public static void ShowError(Exception exception, string message, string caption = "")
        {
            var exceptionViewer = new ExceptionViewer(message, exception);
            var baseDialogWindow = new BaseDialogWindow
            {
                Title = string.IsNullOrEmpty(caption) ? "Error" : caption,
                Content = exceptionViewer,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                MinHeight = 400,
                MinWidth = 500,
                ShowMinButton = false,
                ShowMaxRestoreButton = false,
                ShowInTaskbar = false
            };
            baseDialogWindow.ShowDialog();

            // MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}