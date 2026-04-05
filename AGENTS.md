# TidalUi3 - AI Agent Documentation

## Project Overview

TidalUi3 is a **Windows desktop music streaming client** for TIDAL, built with **WinUI 3** and the **Windows App SDK**. It provides a modern, fluent-design interface for browsing, searching, and playing music from the TIDAL streaming service.

### Key Features
- Device code OAuth authentication with TIDAL
- Browse personalized "For You" recommendations
- Search across tracks, albums, artists, and playlists
- Playback with Hi-Res Lossless (MAX) quality support
- Queue management with shuffle and repeat modes
- Real-time synced lyrics display
- Library management (favorite tracks, albums, playlists)
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
├── App.xaml.cs           # App entry point, global services initialization
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
│   ├── QualityColorConverter.cs
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
│   ├── HomePage.xaml         # Personalized recommendations
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
    ├── ImageCacheService.cs    # Async image caching
    ├── TokenStorageService.cs  # Secure token persistence
    ├── SessionStorageService.cs # Playback session persistence
    └── SettingsService.cs      # App settings
```

---

## Build and Run Commands

### Prerequisites
- Windows 10 version 2004 (build 19041) or later
- Visual Studio 2022 with Windows App SDK workload
- .NET 8.0 SDK

### Build Commands

```powershell
# Restore packages
dotnet restore

# Build Debug (x64)
dotnet build --configuration Debug --platform x64

# Build Release (x64)
dotnet build --configuration Release --platform x64

# Build for all platforms
dotnet build --configuration Release

# Run the application
dotnet run --configuration Debug

# Create MSIX package (requires single-project packaging)
dotnet publish --configuration Release --platform x64
```

### Visual Studio
1. Open `TidalUi3.sln`
2. Set startup project to `TidalUi3`
3. Select target platform (x64 recommended)
4. Press F5 to debug

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
- `GetForYouPageAsync()` - Personalized home content
- `SearchAsync()` / `SearchTopHitsAsync()` - Search
- `GetPlaybackInfoAsync()` - Stream URLs (supports HI_RES_LOSSLESS)
- `GetTrackLyricsAsync()` - Lyrics via Triton proxy
- `GetFavoritePlaylistsAsync()` - User's playlists

### PlaybackService
Wraps `Windows.Media.Playback.MediaPlayer`.

- Supports DASH manifests for Hi-Res FLAC
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
- **TokenStorageService**: OAuth tokens in `LocalSettings`
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
| User | `/users/{id}/favorites/tracks`, `/users/{id}/playlists` |
| For You | `/pages/for_you` |
| Mixes | `/pages/mix` |
| Radio | `/tracks/{id}/radio` |
| Lyrics | `triton.squid.wtf/lyrics/` (third-party) |

---

## Error Handling

- Global exception logging to `Logs/exceptions.log`
- First-chance exception filter for Socket/IOException noise
- API errors written to `api_err.txt` and `json_err.txt` for debugging
- Graceful degradation (empty states, loading spinners)

---

## Security Considerations

- OAuth tokens stored in Windows `LocalSettings`
- Session state encrypted with `DataProtectionProvider`
- Client credentials embedded in App.xaml.cs (dev-only, should be externalized)
- TLS for all API communications

---

## Testing

No automated test projects currently exist. Testing is done manually through:
1. Debug builds with verbose logging
2. JSON dump files for API response inspection
3. Exception logs for error analysis

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
| Build errors about Windows SDK | Install Windows App SDK via VS Installer |
| Playback fails | Check `api_err.txt` for stream URL issues |
| Auth loops | Clear `LocalSettings` or delete session |
| UI not updating | Verify `DispatcherQueue.TryEnqueue()` usage |
| Lyrics not loading | Triton service may be unavailable |
