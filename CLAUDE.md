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

## Як тестувати Auto-Update

1. Скачай попередній реліз з GitHub (наприклад `v1.3.5`) — `SFT-Tracker-Setup.exe`
2. Запусти — він побачить що на бекенді вже `v1.3.6` і запропонує оновитись
3. Погодься → має скачати новий exe, закритись, PowerShell замінить файл, перезапуститись

Якщо треба протестити `force_update`:
```bash
# Виставити force на бекенді вручну через адмін ендпоінт:
curl -X POST https://satisfactory.kaffka.tech/api/admin/client/version \
  -H "x-admin-token: <ADMIN_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"version":"1.3.6","url":"https://github.com/Prefect1109/satisfactory-pm-windows/releases/download/v1.3.6/SFT-Tracker-Setup.exe","force_update":true}'
```
`ADMIN_TOKEN` — запитай у Боді

Після тесту скинути `force_update` назад на `false`.

## UI Standards
- Dark glassmorphism, 460×700px, borderless, draggable titlebar.
- Кольори: accent `#FF6B35`, surface `#1A1A2E`, card `#16213E`.
