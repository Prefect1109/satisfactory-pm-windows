# SFT Windows — Release Guidelines

## Stack
C# WPF, .NET 8, self-contained single-file exe (win-x64). Проект: `SFTracker/SFTracker.csproj`.

## Структура
```
SFTracker/
├── Services/
│   ├── ApiService.cs      — HTTP клієнт (BaseUrl, auth header, worlds, save up/download)
│   ├── AuthService.cs     — токен/last_world/skip_confirm у %AppData%\SFTracker\
│   ├── SaveParser.cs      — парсить .sav header (play time), FormatPlayTime helper
│   └── UpdateService.cs   — IsNewer(), DownloadUpdateAsync(), ApplyUpdateAndRestart()
├── ViewModels/
│   ├── ViewModelBase.cs   — INotifyPropertyChanged, Set<T>
│   ├── LoginViewModel.cs  — введення connect-token, обмін на JWT
│   └── MainViewModel.cs   — весь UI стан, smart sync, upload/download
├── Views/
│   ├── MainWindow          — shell, borderless, draggable titlebar
│   ├── LoginView           — connect-token форма
│   ├── MainPage            — основний екран: worlds list, local/cloud info, кнопки
│   └── UpdateWindow        — прогрес-бар оновлення
├── Models/
│   ├── World.cs            — id, name, owner_id, owner_name, member_count
│   ├── SaveMetadata.cs     — exists, filename, size, hash, session_name, play_time_sec, updated_at
│   ├── UserInfo.cs         — active (isPremium), until, username, storage_used, storage_limit
│   └── VersionInfo.cs      — version, url, force_update
└── Converters/
    └── Converters.cs       — BoolToVisibility, NotEmptyToVisibility
```

## Auth Flow
1. Юзер вводить connect-token з сайту → `POST /auth/exchange` → отримує JWT
2. JWT зберігається у `%AppData%\SFTracker\token.txt`
3. При старті `AuthService.LoadToken()` → `ApiService.SetToken()` → відразу йде в `MainViewModel.LoadAsync()`
4. Logout: видаляє файл токена

## Smart Sync Logic (`MainViewModel.ComparePlayTime`)
```
1. local.PlayTimeSec > 0 && cloud.PlayTimeSec > 0  → порівнюємо в секундах
2. Інакше (dedicated server = PlayTimeSec 0)        → fallback на дату файлу vs UpdatedAt
```
Результат: `(1=↑Upload, -1=↓Download, 0=✓Sync)` + текстова причина для confirm-діалогу.

## FindBestLocalSave Priority
```
%LocalAppData%\FactoryGame\Saved\SaveGames\<userId>\*.sav
1. world-name match, не autosave  (найновіший)
2. world-name match, autosave     (найновіший)
3. будь-який, не autosave         (найновіший)
4. будь-який autosave             (найновіший)
```

## Incremental Releases
- Версія живе в `<Version>` в `.csproj`.
- Кожен push тегу `v*` → GitHub Actions → тести → `dotnet publish` → single exe → GitHub Release → нотифікація бекенду.
- Бекенд: `https://satisfactory.kaffka.tech/api`, ендпоінт версії: `GET /client/version`.

## Auto-Update Flow
App start → GET /client/version → якщо newer:
- `force_update=true` → тихо качає, запускає ps1 updater, закривається
- `force_update=false` → питає юзера

Updater: PowerShell скрипт у temp, замінює `SFTracker.exe` поки app закрита, перезапускає.

## Як тестувати Auto-Update

1. Скачай попередній реліз з GitHub (наприклад `v1.3.5`) — `SFT-Tracker-Setup.exe`
2. Запусти — він побачить що на бекенді вже `v1.3.6` і запропонує оновитись
3. Погодься → має скачати новий exe, закритись, PowerShell замінить файл, перезапуститись

Якщо треба протестити `force_update`:
```bash
curl -X POST https://satisfactory.kaffka.tech/api/admin/client/version \
  -H "x-admin-token: <ADMIN_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"version":"1.3.6","url":"https://github.com/Prefect1109/satisfactory-pm-windows/releases/download/v1.3.6/SFT-Tracker-Setup.exe","force_update":true}'
```
`ADMIN_TOKEN` — запитай у Боді. Після тесту скинути `force_update` назад на `false`.

## Tests

```
SFTracker.Tests/
├── SaveParserTests.cs     — ReadPlayTimeSec (crafted binary), FormatPlayTime
├── UpdateServiceTests.cs  — IsNewer version comparison
└── MainViewModelTests.cs  — ComparePlayTime (local/cloud/fallback)
```

Запустити локально (потрібен Windows):
```
dotnet test SFTracker.Tests/SFTracker.Tests.csproj
```

CI: тести йдуть на кожен push до `main`/`dev` і PR. Build запускається тільки після тестів і тільки на теги `v*`.

## UI Standards
- Dark glassmorphism, 460×700px, borderless, draggable titlebar.
- Кольори: accent `#FF6B35`, surface `#1A1A2E`, card `#16213E`.
