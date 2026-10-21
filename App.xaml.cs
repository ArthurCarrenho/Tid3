using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using TidalUi3.Helpers;
using TidalUi3.Services;

namespace TidalUi3
{
    public partial class App : Application
    {
        public static MainWindow? MainWindow { get; private set; }

        private const string TidalClientId = "fX2JxdmntZWK0ixT";
        private const string TidalClientSecret = "1Nm5AfDAjxrgJFJbKNWLeAyKGVGmINuXPPLHVXAvxAg=";

        public static TidalApiClient ApiClient { get; } = new(TidalClientId, TidalClientSecret);
        public static LyricsService Lyrics { get; } = new(ApiClient);
        public static QueueService Queue { get; } = new();
        public static PlaybackService Playback { get; private set; } = null!;
        public static DiscordRpcService DiscordRpc { get; } = new();

        public App()
        {
            InitializeComponent();

#if DEBUG
            // Every exception, caught or not, as it is thrown. Useful while developing, but it
            // writes a stack trace per throw, so release builds only log what goes unhandled.
            System.AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                // Skip expected noise: cancelled requests and searches (every skip and
                // keystroke cancels one), and HTTP connection cleanup / SSL teardown.
                var ex = e.Exception;
                if (AppLog.IsWriting
                    || ex is System.OperationCanceledException
                    || ex is System.Net.Sockets.SocketException
                    || ex is System.IO.IOException ioEx && ioEx.InnerException is System.Net.Sockets.SocketException)
                    return;

                AppLog.Exception("FirstChanceException", ex);
            };
#endif
            this.UnhandledException += (s, e) => AppLog.Exception("UnhandledException", e.Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => AppLog.Exception("UnobservedTaskException", e.Exception);

            ApiClient.TokenChanged += OnTokenChanged;
            Playback = new PlaybackService(ApiClient);
            try { AppNotificationManager.Default.Register(); } catch { }
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
