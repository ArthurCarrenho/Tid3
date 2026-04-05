using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using TidalUi3.Services;

namespace TidalUi3
{
    public partial class App : Application
    {
        public static MainWindow? MainWindow { get; private set; }
        private static readonly object _logLock = new();

        private const string TidalClientId = "fX2JxdmntZWK0ixT";
        private const string TidalClientSecret = "1Nm5AfDAjxrgJFJbKNWLeAyKGVGmINuXPPLHVXAvxAg=";

        public static TidalApiClient ApiClient { get; } = new(TidalClientId, TidalClientSecret);
        public static QueueService Queue { get; } = new();
        public static PlaybackService Playback { get; private set; } = null!;
        public static DiscordRpcService DiscordRpc { get; } = new();

        public App()
        {
            InitializeComponent();

            System.AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                // Skip noisy internal .NET exceptions (HTTP connection cleanup, SSL teardown)
                var ex = e.Exception;
                if (ex is System.Net.Sockets.SocketException
                    || ex is System.IO.IOException ioEx && ioEx.InnerException is System.Net.Sockets.SocketException
                    || ex is System.IO.IOException && ex.Message.Contains("exceptions.log"))
                    return;

                LogException("FirstChanceException", ex.ToString());
            };
            this.UnhandledException += (s, e) => LogException("UnhandledException", e.Exception.ToString());
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => LogException("UnobservedTaskException", e.Exception.ToString());

            ApiClient.TokenChanged += OnTokenChanged;
            Playback = new PlaybackService(ApiClient);
            try { AppNotificationManager.Default.Register(); } catch { }
        }

        private static void LogException(string type, string details)
        {
            try
            {
                lock (_logLock)
                {
                    var logDir = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    System.IO.Directory.CreateDirectory(logDir);
                    var logPath = System.IO.Path.Combine(logDir, "exceptions.log");
                    using var fs = new System.IO.FileStream(logPath,
                        System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                    using var sw = new System.IO.StreamWriter(fs);
                    sw.Write($"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] {type}\n{details}\n\n----------------------------------------\n\n");
                }
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            TryRestoreSession();
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }

        private static void TryRestoreSession()
        {
            var session = TokenStorageService.Load();
            if (session is not null)
            {
                ApiClient.SetToken(session.AccessToken, session.RefreshToken,
                    session.UserId, session.CountryCode);
            }
        }

        private static void OnTokenChanged(string accessToken, string? refreshToken,
            int userId, string countryCode)
        {
            TokenStorageService.Save(accessToken, refreshToken, userId, countryCode);
        }
    }
}
