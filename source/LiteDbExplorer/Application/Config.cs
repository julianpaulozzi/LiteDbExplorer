using System.Configuration;
using System.IO;
using Caliburn.Micro;
using LiteDbExplorer.Wpf.Modules.Output;
using Serilog;
using Serilog.Events;

namespace LiteDbExplorer
{
    public static class Config
    {
        public static string UpdateDataUrl => ConfigurationManager.AppSettings["UpdateUrl"];

        public static string IssuesUrl => ConfigurationManager.AppSettings["IssuesUrl"];

        public static string HomepageUrl => ConfigurationManager.AppSettings["HomepageUrl"];

        public static string ReleasesUrl => ConfigurationManager.AppSettings["ReleasesUrl"];

        public static string PipeEndpoint => ConfigurationManager.AppSettings["PipeEndpoint"];

        public static string Version => ConfigurationManager.AppSettings["Version"];

        public static bool IsPortable => !File.Exists(Paths.UninstallerPath);

        public static void ConfigureLogger()
        {
            var log = new LoggerConfiguration()
                .MinimumLevel.Debug();

#if DEBUG
            log.WriteTo.Console();
#endif
            log.WriteTo.File(
                    Paths.ErrorLogsFileFullPath,
                    fileSizeLimitBytes: 4096000,
                    rollingInterval: RollingInterval.Month,
                    retainedFileCountLimit: 2,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error
            );

            // TODO: Lazy
            log.WriteTo.OutputModule(
                () => IoC.Get<IOutput>(),
                () =>
                {
                    var outputLogFilter = IoC.Get<IOutputLogFilter>();
#if DEBUG
                    outputLogFilter.MinLogLevel = LogEventLevel.Debug;
#endif
                    return outputLogFilter;
                });

            Log.Logger = log.CreateLogger();
        }
    }
}
