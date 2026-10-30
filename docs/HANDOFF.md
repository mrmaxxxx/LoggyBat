# LoggyBat — устройство, сборка, установка

## Как это работает

```
Logitech G HUB (lghub_agent, ws://localhost:9010)
        │  WebSocket, JSON, опрос каждые 10 с
        ▼
LoggyBat.exe (процесс виджета, WinUI 3) ──► WidBar рисует его на панели задач
```

- На панели: `🖱 92%   🎧 69%`, `⚡` при зарядке, `—` если свежих данных нет.
- Во flyout под каждым устройством: `Ответ G HUB: 21:42:22` (время **получения ответа**,
  G HUB не сообщает время измерения) или `Нет данных: <причина>`.

### Логика подключения ([GHubClient.cs](../src/LoggyBat/GHubClient.cs))

- Один управляющий цикл и одно соединение; один цикл чтения на соединение;
  отправки под блокировкой; ответы сопоставляются по `msgId`.
- Каждый опрос: `GET /devices/list` → устройства ищутся по `pid`
  (мышка 16537, наушники 2746) → `GET /battery/<id>/state` для каждого.
  Список читается каждый раз, поэтому смена временных `devNNNNNNNN` не страшна.
- Тайм-аут каждого запроса 5 с. Любая ошибка → соединение закрывается,
  переподключение через 2, 4, 8 … 30 с.
- Пауза больше 40 с между опросами (сон Windows, скрытый виджет) →
  принудительное переподключение, даже если сокет «выглядит открытым».
- Пока виджет скрыт и flyout закрыт, опроса нет.
- Заряд показывается, только если для этого устройства последний ответ
  успешный и ему меньше 40 с. Каждое устройство независимо.
- Подписка на события G HUB (`SUBSCRIBE`) не используется: не проверена.

### Лог

`loggybat.log`, пишется только при смене состояния, до 256 КБ (+ `.old`).
Внутри MSIX Windows перенаправляет его в папку пакета. Открыть:

```powershell
explorer "$env:LOCALAPPDATA\Packages\$((Get-AppxPackage Max.LoggyBat).PackageFamilyName)\LocalCache\Local\LoggyBat"
```

Если папки нет — посмотреть `%LOCALAPPDATA%\LoggyBat`.

## Структура

```
src/
  LoggyBat.sln
  LoggyBat/                 код виджета: MainPlugin.cs (UI), GHubClient.cs, GHubProtocol.cs, Log.cs
  LoggyBat.Package/         MSIX-упаковка, Package.appxmanifest
tests/GHubProtocolCheck/    проверка разбора ответов G HUB (запускается в CI)
scripts/New-SigningCertificate.ps1   создание постоянного сертификата
.github/workflows/build.yml          сборка, подпись, артефакт
wiki/                       справка по WidBar SDK из исходного шаблона
```

Проект основан на https://github.com/andelby/widbar-widget-template (файла лицензии там нет).

## Идентичность

| Что | Где | Значение |
|---|---|---|
| WidBar Plugin ID | `MainPlugin.Id`, `WidBarPluginId` в `LoggyBat.csproj`, `Package.appxmanifest` — должны совпадать | `com.mrmaxxxx.loggybat` |
| MSIX Identity | `Package.appxmanifest` | `Max.LoggyBat` |
| Publisher | `Package.appxmanifest`, должен совпадать с сертификатом | `CN=Max` |

Смена любого из них = для Windows/WidBar это новое приложение.

## Версия

`1.1.<номер запуска workflow>.0`. Номер запуска (`github.run_number`) растёт на 1 при
каждом запуске workflow «Build LoggyBat» — и вручную, и по push в `loggybat`.
CI записывает версию в `Package.appxmanifest` и в `WidBarPluginVersion` перед сборкой.
В репозитории стоит `1.1.0.0` — это только заглушка.

Новая версия ставится поверх старой, если совпадают Identity, Publisher
и сертификат. Сохраняется ли при этом размещение виджета в WidBar — ещё не проверено.

## Сертификат (один раз)

1. Создать сертификат на своём компьютере (обычный PowerShell, не от администратора):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\New-SigningCertificate.ps1
   ```
   Скрипт спросит пароль и создаст папку `%USERPROFILE%\LoggyBat-signing`:
   `LoggyBat-signing.pfx` (закрытый ключ — хранить, никому не отдавать, не класть
   в репозиторий) и `LoggyBat.cer` (публичный). Base64 ключа окажется в буфере обмена.
2. GitHub → репозиторий → Settings → Secrets and variables → Actions →
   New repository secret:
   - `SIGNING_PFX_BASE64` — вставить из буфера обмена;
   - `SIGNING_PFX_PASSWORD` — пароль из шага 1.
3. Доверять сертификату (PowerShell **от администратора**):
   ```powershell
   Import-Certificate -FilePath "$HOME\LoggyBat-signing\LoggyBat.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

Без секретов сборка падает на шаге «Sign package» с понятной ошибкой.
Если `.pfx` потерян, обновления поверх установленной версии станут невозможны:
понадобится новый сертификат и переустановка.

## Сборка

GitHub → Actions → «Build LoggyBat» → Run workflow → ветка `loggybat`
(или просто push в `loggybat`). Через ~5 минут скачать артефакт `LoggyBat-x64`
(хранится 7 дней) и распаковать.

## Установка и обновление

1. Закрыть LGSTrayBattery и убрать его из автозагрузки
   (Диспетчер задач → Автозагрузка → LGSTrayGUI → Отключить).
2. Первый раз: удалить старый «Logitech Battery» (только по договорённости):
   ```powershell
   Get-AppxPackage Max.LogitechBattery.Widget | Remove-AppxPackage
   ```
3. Установить: двойной щелчок по `LoggyBat.Package_1.1.N.0_x64.msix` в распакованной папке,
   либо
   ```powershell
   Add-AppxPackage -Path "<путь>\LoggyBat.Package_1.1.N.0_x64.msix"
   ```
4. WidBar → Layout → Refresh → добавить LoggyBat.

Обновление: шаги 3–4 с новой сборкой (шаг 4 — только если виджет пропал).

## Откат

- Вернуть старое приложение:
  ```powershell
  Get-AppxPackage Max.LoggyBat | Remove-AppxPackage
  Add-AppxPackage -Path "Z:\CLAUDE\LoggyBat (PC)\LogitechBattery-x64\WidBarWidget1 (Package)_1.0.0.0_x64_Test\WidBarWidget1 (Package)_1.0.0.0_x64.msix"
  ```
  Его сертификат `CN=Developer` уже стоит в «Доверенных лицах». Старой версии
  нужен запущенный LGSTrayBattery.
- Код: тег `before-loggybat` — состояние до перехода.
