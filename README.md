# Satisfactory Session Tracker — Windows

Desktop companion для синхронізації сейвів Satisfactory через [Satisfactory Tracker Bot](https://t.me/SatisfactoryTrackerBot).

## Можливості

- **Smart Sync** — порівнює дати локального і хмарного сейву, завантажує новіший
- **Auto-detect** — знаходить папку сейвів автоматично (Steam, Epic, будь-який account ID)
- **Autosave fallback** — якщо звичайного сейву нема, підхоплює autosave по назві світу
- **Auto-update** — при запуску перевіряє нову версію і оновлюється без ручних дій
- **Зберігає токен** — повторний логін після оновлення не потрібен

## Як почати

1. Отримай токен у боті: `/connect`
2. Завантаж `SFT-Tracker-Setup.exe` з [Releases](../../releases/latest)
3. Запусти, введи токен — готово

## Стек

C# WPF · .NET 8 · self-contained single `.exe` (не потребує встановленого .NET)

## Build

Збирається автоматично через GitHub Actions при пуші тегу `v*`:

```
git tag v1.x.x && git push origin v1.x.x
```

Версія бампиться автоматично з тегу → `dotnet publish` → GitHub Release → бекенд нотифікується.
