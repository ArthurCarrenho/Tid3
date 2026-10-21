# TidalUi3 - AI Agent Documentation

## Project Overview

TidalUi3 is a **Windows desktop music streaming client** for TIDAL, built with **WinUI 3** and the **Windows App SDK**. It provides a modern, fluent-design interface for browsing, searching, and playing music from the TIDAL streaming service.

### Key Features
- Device code OAuth authentication with TIDAL
- Browse personalized "For You" recommendations and the Explore pages (genres, moods, decades)
- Search across tracks, albums, artists, and playlists
- Playback with Hi-Res Lossless (MAX) quality support
- Queue management with shuffle and repeat modes
- Real-time synced lyrics display
- Library management (liked tracks, saved albums and playlists, followed artists)
- Playlist editing (create, add tracks, remove tracks) and track credits
- Output device selection and keyboard shortcuts (listed in Settings)
- Artist pages with biography, discography, and top tracks
- Session persistence and resume functionality

---

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8.0 |
| UI Framework | WinUI 3 (Windows App SDK 1.8) |
| Target Platform | Windows 10.0.19041.0 (Windows 10 20H1+) |
| Minimum Platform | Windows 10.0.17763.0 (Windows 10 1809) |
| Architecture | x86, x64, ARM64 |
| Media Playback | Windows MediaPlayer with AdaptiveMediaSource |
| HTTP Client | System.Net.HttpClient |
| JSON Serialization | System.Text.Json |

### NuGet Dependencies
- `Microsoft.WindowsAppSDK` (1.8.260209005)
- `Microsoft.Windows.SDK.BuildTools` (10.0.26100.7705)

---

## Project Structure

```
TidalUi3/
├── App.xaml              # Application resources and converters
├── App.xaml.cs           # Global services initialization
├── Program.cs            # Entry point: single instance (DISABLE_XAML_GENERATED_MAIN)
├── MainWindow.xaml       # Main window layout (NavigationView + PlaybackBar)
├── MainWindow.xaml.cs    # Navigation, search, playback coordination
├── Package.appxmanifest  # MSIX package manifest
├── TidalUi3.csproj       # Project file
├── TidalUi3.sln          # Solution file
│
├── Controls/             # Reusable UI controls (20 controls)
│   ├── PlaybackBarControl.xaml      # Bottom playback bar
│   ├── QueuePanelControl.xaml       # Right-side queue panel
│   ├── LyricsPanelControl.xaml      # Right-side lyrics panel
│   ├── TrackListItemControl.xaml    # Track row in lists
│   ├── CarouselSectionControl.xaml  # Horizontal scrolling content
│   ├── PageHeaderControl.xaml       # Search + back/forward header
│   ├── VolumeControl.xaml           # Volume slider
│   ├── TransportControls.xaml       # Play/pause/next/previous
│   └── ... (12 more)
│
├── Converters/           # XAML value converters
│   ├── BoolToVisibilityConverter.cs
│   └── ... (5 more)
│
├── Helpers/              # Utility helpers
│   └── QualityHelper.cs  # Audio quality badge formatting
│
├── Models/               # UI-facing data models
│   ├── Track.cs          # Track with INotifyPropertyChanged
│   ├── Playlist.cs
│   ├── Album.cs
│   ├── HomeSection.cs
│   └── SearchSuggestion.cs
│
├── Pages/                # Main application pages
│   ├── HomePage.xaml         # Personalized recommendations (pages/for_you)
│   ├── ExplorePage.xaml      # Genres, moods, decades (pages/explore)
│   ├── BrowsePage.xaml       # Any TIDAL page by apiPath (a genre, a mood)
│   ├── ItemsGridPage.xaml    # "See all" grid of cards, loaded page by page
│   ├── LibraryPage.xaml      # User's library
│   ├── PlaylistDetailPage.xaml # Album/playlist/mix details
│   ├── ArtistPage.xaml       # Artist profile
│   ├── SearchResultsPage.xaml
│   ├── LoginPage.xaml        # Device auth flow
│   └── SettingsPage.xaml
│
└── Services/             # Business logic and API
    ├── TidalApiClient.cs       # TIDAL API client
    ├── TidalApiModels.cs       # API response models
    ├── PlaybackService.cs      # Media playback control
    ├── QueueService.cs         # Playback queue management
    ├── FavoritesService.cs     # Liked/saved/followed state, toggled optimistically
    ├── PlaylistService.cs      # The user's own playlists and editing them
    ├── ImageCacheService.cs    # Async image caching
    ├── TokenStorageService.cs  # Secure token persistence
    ├── SessionStorageService.cs # Playback session persistence
    └── SettingsService.cs      # App settings
```

---

## Build and Run Commands

### Prerequisites
- Windows 10 version 2004 (build 19041) or later
- .NET 8.0 SDK (x64) - pinned by `global.json`
- Developer Mode enabled (required to register the app locally without signing)

**Visual Studio is not required.** The WinUI 3 toolchain is restored from NuGet:
`Microsoft.WindowsAppSDK` supplies the XAML compiler and MSIX build targets, and
`Microsoft.Windows.SDK.BuildTools` supplies `makeappx`, `makepri` and `signtool`.
No separate Windows SDK install is needed either.

### build.ps1 (preferred)

`build.ps1` at the repo root wraps every workflow. Add `-Platform x86|arm64` to target
another architecture (default `x64`).

```powershell
.\build.ps1 dev          # build Debug, register AppX layout, launch  (fast inner loop)
.\build.ps1 build        # build only
.\build.ps1 run          # launch the already-registered package
.\build.ps1 install      # Release copied to %LOCALAPPDATA%\Programs\Tid3 and registered from there
.\build.ps1 msix         # publish Release -> signed .msix in packages/
.\build.ps1 unregister   # remove the registered package (and its app data)
.\build.ps1 clean        # delete bin/, obj/, packages/
```

`dev` registers the loose build output via `Add-AppxPackage -Register`, which needs no
MSIX packing and no signing. This is the same mechanism behind Visual Studio's F5.

Before registering, `dev` syncs the output folder against `TidalUi3.build.appxrecipe`.
This is required: `dotnet build` does not copy `<Content>` items (everything under
`Assets/`) into the output folder, because MSIX packaging reads them straight from source
via `GetPackagingOutputs`. Since `-Register` treats the output folder as the package root,
skipping the sync installs the app **without any icons**. The recipe is the authoritative
list of package contents, so the sync also covers non-asset files such as
`Microsoft.Web.WebView2.Core.winmd`.

`msix` generates a self-signed dev certificate at `build/Tid3_Dev.pfx` on first run
(gitignored). Set `TID3_CERT_PFX` / `TID3_CERT_PASSWORD` to sign with a real certificate.

### Raw dotnet commands

```powershell
# Restore packages
dotnet restore

# Build Debug (x64)
dotnet build -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64

# Build Release (x64)
dotnet build -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64

# Produce a signed MSIX (same parameters the release workflow uses)
dotnet publish TidalUi3.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 `
  -p:PackageCertificateKeyFile=build/Tid3_Dev.pfx -p:PackageCertificatePassword=tid3build `
  -p:AppxPackageDir=packages/x64/ -p:UapAppxPackageBuildMode=SideloadOnly `
  -p:AppxBundle=Never -p:GenerateAppxPackageOnBuild=true
```

> The app depends on package identity (`ApplicationData.Current` in `SettingsService`,
> `TokenStorageService`, `SessionStorageService`, `ImageCacheService`; `Package.Current`
> and `StartupTask` in `SettingsPage`). Unpackaged builds (`WindowsPackageType=None`)
> compile but crash at runtime, so the packaged path is mandatory.

### Editors

- **VS Code**: `.vscode/tasks.json` exposes the build.ps1 tasks; `.vscode/launch.json`
  provides attach configurations. Debug by running the `dev` task, then attaching to
  `TidalUi3.exe` - the app must be shell-activated to keep package identity, so launching
  the `.exe` directly from the debugger does not work.
- **Visual Studio**: open `TidalUi3.sln`, set platform to x64, press F5. Only VS offers
  XAML Hot Reload and the visual XAML designer.

### Releases

`.github/workflows/release.yml` runs `dotnet publish` only - no Visual Studio steps.
Pushing a `v*.*.*` tag builds x64/x86/arm64 MSIX packages and opens a draft release.

---

## Architecture Patterns

### Service Locator Pattern
Global services are exposed as static properties on `App` class:
```csharp
public static TidalApiClient ApiClient { get; }
public static QueueService Queue { get; }
public static PlaybackService Playback { get; }
```

### Event-Driven Communication
Services communicate via events rather than direct coupling:
```csharp
// QueueService
public event Action<Track?>? CurrentTrackChanged;

// PlaybackService
public event Action<TimeSpan, TimeSpan>? PositionChanged;
public event Action? PlaybackEnded;
```

### MVVM-Like Structure
- **Models**: Data classes with `INotifyPropertyChanged` where needed
- **Views**: XAML pages and controls
- **Code-Behind**: Handles UI events, delegates to services

---

## Key Services

### TidalApiClient
Central HTTP client for TIDAL API (`api.tidalhifi.com/v1/`).

**Authentication**: Device code flow (OAuth 2.0)
- `StartDeviceAuthAsync()` - Initiates device auth
- `PollDeviceTokenAsync()` - Polls for completion
- `RefreshAccessTokenAsync()` - Token refresh

**Key Methods**:
- `GetPageAsync(apiPath)` / `GetPageDataAsync()` - TIDAL pages (For You, Explore, genres) and their "show more" lists; parsed by `Helpers/PageModules.cs`, rendered by `Helpers/PageSectionsBuilder.cs`
- `SearchAsync()` / `SearchTopHitsAsync()` - Search
- `GetPlaybackInfoAsync()` - Stream URLs (supports HI_RES_LOSSLESS)
- `GetTrackLyricsAsync()` - TIDAL's own lyrics (refused for most client ids; `LyricsService` uses it as a last resort)
- `GetLibraryPlaylistsAsync()` / `GetOwnPlaylistsAsync()` - The library's playlists (own and saved) / only the user's own
- `GetFavoriteIdsAsync()`, `Add/RemoveFavorite*Async()` - Liked, saved and followed items
- `CreatePlaylistAsync()`, `AddTracksToPlaylistAsync()`, `RemovePlaylistItemAsync()` - Playlist editing (sends the playlist's ETag as `If-None-Match`)
- `GetTrackCreditsAsync()` - Track credits

### PlaybackService
Wraps `Windows.Media.Playback.MediaPlayer`.

- Supports DASH manifests for Hi-Res FLAC
- Output device: `SetOutputDeviceAsync()` sets `MediaPlayer.AudioDevice` and saves it; a `DeviceWatcher` plays through the system default while the chosen device is gone and switches back when it returns
- SMTC integration (Windows media overlay)
- Position tracking timer (250ms intervals)
- Quality detection and display

### QueueService
Manages playback queue state.

- ObservableCollection for UI binding
- Shuffle (random) and Repeat modes
- Play/next/previous/jump operations
- Auto-play radio when queue ends (configurable)

### Storage Services
- **TokenStorageService**: OAuth tokens encrypted per Windows user (`DataProtectionProvider`)
  in `LocalState\tidal_session.dat`; migrates the old plain-text `LocalSettings` keys once
- **SessionStorageService**: Encrypted playback state in `LocalCacheFolder`
- **SettingsService**: App preferences

---

## Code Style Guidelines

### Naming Conventions
- **Classes/Methods**: PascalCase
- **Private fields**: `_camelCase` with underscore prefix
- **Constants**: PascalCase
- **XAML resources**: PascalCase with suffix (e.g., `BodyTextBlockStyle`)

### C# Patterns
- Use `is not null` instead of `!= null`
- Use target-typed new: `new()` instead of `new Type()`
- Prefer file-scoped namespaces
- Use nullable reference types (`<Nullable>enable</Nullable>`)

### XAML Conventions
- Two-space indentation in XAML files
- Event handlers named: `ControlName_EventName` (e.g., `LoginButton_Click`)
- Custom controls in `Controls` namespace

### Comments
- Use decorative comment blocks for major sections:
```csharp
// ═════════════════════════════════════════════
//  Section Name
// ═════════════════════════════════════════════
```
- XML documentation for public APIs

---

## Audio Quality Handling

Quality badges are derived from `mediaMetadata.tags`:

| Tag | Badge | Color |
|-----|-------|-------|
| HIRES_LOSSLESS | MAX | Gold (#ffd432) |
| LOSSLESS | HIGH | Cyan (#21feec) |
| MQA | MQA | - |
| (other) | LOW | White |

DASH manifests (XML) are handled separately from JSON manifests for Hi-Res content.

---

## API Endpoints Used

| Feature | Endpoint |
|---------|----------|
| Auth | `auth.tidal.com/v1/oauth2/` |
| Search | `/search`, `/search/top-hits` |
| Tracks | `/tracks/{id}`, `/tracks/{id}/playbackinfo` |
| Albums | `/albums/{id}`, `/albums/{id}/tracks` |
| Artists | `/artists/{id}`, `/artists/{id}/toptracks`, `/artists/{id}/albums`, `/artists/{id}/bio` |
| Playlists | `/playlists/{uuid}`, `/playlists/{uuid}/items` |
| User | `/users/{id}/favorites/{tracks,albums,artists,playlists}`, `/users/{id}/favorites/ids`, `/users/{id}/playlists`, `/users/{id}/playlistsAndFavoritePlaylists` |
| Playlist editing | `POST /users/{id}/playlists`, `POST /playlists/{uuid}/items`, `DELETE /playlists/{uuid}/items/{index}` |
| Credits | `/tracks/{id}/credits` |
| For You | `/pages/for_you` |
| Explore | `/pages/explore`, `/pages/{genre}`, `/pages/single-module-page/...`, `/pages/data/{id}` |
| Mixes | `/pages/mix` |
| Radio | `/tracks/{id}/radio` |
| Lyrics | `LyricsService`: LRCLIB `lrclib.net/api/get`, then `/api/search` (by duration), then `/tracks/{id}/lyrics` |

---

## Error Handling

- All logging goes through `Helpers/AppLog` into `Logs/` next to the app (the build output
  when registered with `build.ps1 dev`), or `LocalState\Logs` when that folder is read-only
  (installed MSIX). Each file rolls over to `.old` past 5 MB.
- `exceptions.log`: unhandled and unobserved exceptions, playback errors. Debug builds also log
  every first-chance exception except cancellations and socket noise.
- `playtrace.log` (TEMP-DIAG): queue/gapless trace from `PlaybackService`, Debug builds only.
- A track that fails to play raises `PlaybackService.PlaybackFailed`; `MainWindow` skips it
  and shows an InfoBar, stopping after 3 failures in a row.
- Graceful degradation (empty states, loading spinners)

---

## Security Considerations

- OAuth tokens encrypted for the current Windows user in `LocalState\tidal_session.dat`
- Session state encrypted with `DataProtectionProvider`
- Client credentials embedded in App.xaml.cs (dev-only, should be externalized)
- TLS for all API communications

---

## Testing

`tests/TidalUi3.Tests` (xUnit) covers the logic that doesn't need the UI: the queue
(`QueueService`), the queue panel's row diff (`QueueRows`), playing lists that load page by
page (`LoadingTrackListPlayer`), LRC parsing, TIDAL page parsing (`PageModules`) and the
"see all" loaders (`PagedLoaders`). The app is a packaged WinUI executable and
can't be referenced, so the test project compiles those source files in directly; add a file
there when a new class is worth testing. The app project excludes `tests\**` from its items.

```powershell
dotnet test tests/TidalUi3.Tests
```

The release workflow runs them before packaging. Everything that touches WinUI or the media
pipeline is still tested by hand: Debug builds log to `Logs/` (see Error Handling).

---

## Deployment

The app uses **single-project MSIX packaging**:
- `EnableMsixTooling` enabled in `.csproj`
- Assets in `Assets/` directory for app icons
- `Package.appxmanifest` defines app identity and capabilities
- `runFullTrust` capability for full Windows access

---

## Development Notes

### Adding New Pages
1. Create XAML + code-behind in `Pages/`
2. Add to `.csproj` (remove from `Page Remove` if present)
3. Register navigation in `MainWindow.NavView_SelectionChanged`

### Adding New API Methods
1. Add model classes to `TidalApiModels.cs`
2. Add method to `TidalApiClient.cs`
3. Use `GetAsync<T>()` helper for automatic auth and retry

### Custom Controls
- Create in `Controls/` with XAML + code-behind
- Register in `App.xaml` resources if needed globally
- Use `DependencyProperty` for bindable properties

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Build errors about Windows SDK | Run `dotnet restore` - the SDK build tools come from the `Microsoft.Windows.SDK.BuildTools` NuGet package |
| `dotnet build` says no SDK / wrong SDK | Install the .NET 8 SDK (x64); `global.json` pins the build to 8.0.1xx+ |
| `Add-AppxPackage -Register` fails | Enable Developer Mode in Settings > System > For developers |
| Playback fails | Check `api_err.txt` for stream URL issues |
| Auth loops | Delete `LocalState\tidal_session.dat` (or sign out in Settings) |
| UI not updating | Verify `DispatcherQueue.TryEnqueue()` usage |
| Lyrics not loading | LRCLIB is community-run and sometimes answers 503; misses during an outage aren't cached, so the next play retries |
