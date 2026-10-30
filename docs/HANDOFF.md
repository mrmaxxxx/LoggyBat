# LoggyBat — устройство, сборка, установка

## Как это работает

```
Logitech G HUB (lghub_agent, ws://localhost:9010)
        │  WebSocket, JSON
        ▼
LoggyBat (процесс виджета, WinUI 3) ──► WidBar рисует его на панели задач
```

На панели: `🖱 91%   🎧 100%`, `⚡` при зарядке, `—` если данных нет.
Во flyout: заряд и время последнего ответа G HUB (или причина, почему данных нет).

## Структура

```
templates/standalone/
  WidBarWidget1.sln
  WidBarWidget1.ExtensionApp/   код виджета (MainPlugin.cs)
  WidBarWidget1 (Package)/      MSIX-упаковка (Package.appxmanifest)
.github/workflows/build.yml     сборка и подпись MSIX
```

Структура досталась от шаблона https://github.com/andelby/widbar-widget-template
(в шаблоне нет файла лицензии).

## Идентичность

| Что | Где задаётся | Значение |
|---|---|---|
| WidBar Plugin ID | `MainPlugin.Id`, `WidBarPluginId` в csproj, `Package.appxmanifest` — должны совпадать | `com.example.mywidget` |
| MSIX Identity | `Package.appxmanifest` | `Max.LogitechBattery.Widget`, `CN=Developer` |

Смена Plugin ID или MSIX Identity = для Windows и WidBar это новое приложение:
старое придётся удалить, виджет заново добавить через Layout.

## Сборка

GitHub → Actions → «Build Logitech Battery» → Run workflow → выбрать ветку.
Результат — артефакт `LogitechBattery-x64` (MSIX + `.cer`).

## Откат

- Код: тег `before-loggybat` — состояние до перехода на LoggyBat.
- Приложение: удалить новый пакет, установить старый MSIX
  (`LogitechBattery-x64`, версия 1.0.0.0).
