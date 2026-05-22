# SFT Windows — Release Guidelines

## Stack
C# WPF, .NET 8, self-contained single-file exe (win-x64). Проект: `SFTracker/SFTracker.csproj`.

## Структура
```
SFTracker/
├── Services/     ApiService, AuthService, UpdateService
├── ViewModels/   MVVM: LoginViewModel, MainViewModel
├── Views/        MainWindow, LoginView, MainPage, UpdateWindow
├── Models/       World, SaveMetadata, UserInfo, VersionInfo
└── Converters/   BoolToVisibility, NotEmptyToVisibility
```

## Incremental Releases
- Версія живе в `<Version>` в `.csproj`.
- Кожен push тегу `v*` → GitHub Actions → `dotnet publish` → single exe → GitHub Release → нотифікація бекенду.
- Бекенд: `https://satisfactory.kaffka.tech/api`, ендпоінт версії: `GET /client/version`.

## Auto-Update Flow
App start → GET /client/version → якщо newer:
- `force_update=true` → тихо качає, запускає ps1 updater, закривається
- `force_update=false` → питає юзера

Updater: PowerShell скрипт у temp, замінює `SFTracker.exe` поки app закритий, перезапускає.

## UI Standards
- Dark glassmorphism, 460×700px, borderless, draggable titlebar.
- Кольори: accent `#FF6B35`, surface `#1A1A2E`, card `#16213E`.
