# LoggyBat — Terms of Use / Условия использования

Terms version: 2 (2026-11-02)

The English text is the primary version. The Russian text is a translation; if they differ, the
English text prevails. / Основной текст — английский. Русский текст — перевод; при расхождении
действует английский текст.

---

## English

Copyright (c) 2026 mrmaxxxx (LoggyBat's own code).

**1. Acceptance.** LoggyBat's installation scripts show these terms and ask for your consent before
they change anything on your computer. By accepting them, or by installing or using LoggyBat, you agree
to these terms. If you do not agree, do not install or use LoggyBat.

**2. What LoggyBat consists of.** LoggyBat is a widget for WidBar. It consists of (a) LoggyBat's own
code, (b) Microsoft components distributed under Microsoft license terms, and (c) components under open
source licenses. The list of components and the full texts of their licenses and notices are in the
`Notices` folder of the installed package and of the download kits.

**3. LoggyBat's own code.** LoggyBat's own code is licensed to you under the MIT License (file
`LICENSE`), which also permits commercial use, modification and distribution. The MIT License applies
only to LoggyBat's own code, not to the components described in sections 4 and 5. As stated in the
MIT License, LoggyBat's own code is provided "as is", without warranty of any kind.

**4. Microsoft components.** The following components are licensed, not sold, under Microsoft license
terms: Windows App SDK (including WinUI), Windows ML Runtime, the Windows SDK projection for .NET
(`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) and, in the first-install kit only, the Windows
App Runtime packages. For these components you may not:
- reverse engineer, decompile or disassemble them, except to the extent permitted by applicable law or
  by the license terms of open source components included in them;
- remove, minimize, block or modify any notices of Microsoft or its suppliers in them;
- extract them from LoggyBat and distribute them separately.

Microsoft gives no warranties for these components to you and, to the extent permitted by applicable
law, is not liable to you in connection with them.

**5. Open source components.** The .NET runtime, System.Numerics.Tensors, the WebView2 wrapper,
WidBar.SDK and the parts of LoggyBat derived from the WidBar widget template are provided under their own
open source licenses, whose texts are in the `Notices` folder. Your rights and obligations for these
components are determined by those licenses. Nothing in these terms limits any right granted to you by
the license of a third-party component. Third-party materials included in Windows ML are subject to
their own license terms (see the Windows ML `ThirdPartyNotices.txt` in the `Notices` folder).

**6. Data and network connections.** Based on a review of its source code, LoggyBat's own code:
- connects to Logitech G HUB on the same computer (`ws://localhost:9010`) to read the battery levels;
- checks for a new version of LoggyBat by sending a request to GitHub
  (`https://api.github.com/repos/mrmaxxxx/LoggyBat/releases/latest`): automatically, unless you switch
  this off — after a successful check, at most once a day; after a failed attempt, the next attempt no
  earlier than one hour later — and whenever you click "Проверить сейчас" (Check now) in the widget's
  settings, regardless of these intervals. GitHub receives the usual data of such a request, including your computer's IP address, and
  the User-Agent `LoggyBat/<version>`; no information about your devices is sent. LoggyBat only reads the
  version and the page address of the latest release; it does not download or install anything.
  Automatic checks are on by default and can be switched off in the widget's settings ("Проверять
  обновления автоматически", check for updates automatically); while they are off, LoggyBat sends no
  requests to GitHub unless you click "Check now". GitHub's handling of the request is governed by the
  GitHub General Privacy Statement:
  https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement;
- writes its log file and the result of the last update check on the same computer. The widget's
  settings are stored by WidBar.

The behavior
of the third-party components and libraries included in LoggyBat has not been reviewed by the author of
LoggyBat; it is governed by their providers' terms. For Microsoft components, see the Microsoft privacy
statement: https://aka.ms/privacy.

**7. Trademarks.** LoggyBat is not affiliated with, sponsored or endorsed by Microsoft, Logitech or
WidBar. Product names belong to their respective owners.

**8. Contact.** Questions and problem reports: https://github.com/mrmaxxxx/LoggyBat/issues

---

## Русский (перевод)

Copyright (c) 2026 mrmaxxxx (собственный код LoggyBat).

**1. Принятие.** Установочные скрипты LoggyBat показывают эти условия и запрашивают ваше согласие до
каких-либо изменений на компьютере. Принимая условия, устанавливая или используя LoggyBat, вы
соглашаетесь с ними. Если вы не согласны — не устанавливайте и не используйте LoggyBat.

**2. Из чего состоит LoggyBat.** LoggyBat — виджет для WidBar. Он состоит из (а) собственного кода
LoggyBat, (б) компонентов Microsoft, распространяемых по лицензиям Microsoft, и (в) компонентов под
открытыми лицензиями. Перечень компонентов и полные тексты их лицензий и уведомлений — в папке `Notices`
установленного пакета и комплектов загрузки.

**3. Собственный код LoggyBat.** Собственный код LoggyBat предоставляется вам по лицензии MIT
(файл `LICENSE`), которая разрешает в том числе коммерческое использование, изменение и
распространение. Лицензия MIT относится только к собственному коду LoggyBat, а не к компонентам из
разделов 4 и 5. Как сказано в лицензии MIT, собственный код LoggyBat предоставляется «как есть», без
каких-либо гарантий.

**4. Компоненты Microsoft.** Следующие компоненты предоставляются по лицензиям Microsoft, а не
продаются: Windows App SDK (включая WinUI), Windows ML Runtime, проекция Windows SDK для .NET
(`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) и — только в комплекте первой установки — пакеты
Windows App Runtime. В отношении этих компонентов вы не вправе:
- восстанавливать их исходный код, декомпилировать или дизассемблировать их, кроме случаев, разрешённых
  применимым законом или лицензиями входящих в них компонентов с открытым исходным кодом;
- удалять, скрывать, блокировать или изменять уведомления Microsoft и её поставщиков в них;
- извлекать их из LoggyBat и распространять отдельно.

Microsoft не даёт вам гарантий по этим компонентам и, в пределах, допустимых применимым законом, не
несёт перед вами ответственности в связи с ними.

**5. Компоненты под открытыми лицензиями.** Среда выполнения .NET, System.Numerics.Tensors, обёртка
WebView2, WidBar.SDK и части LoggyBat, взятые из шаблона виджета WidBar, предоставляются по их
собственным открытым лицензиям, тексты которых находятся в папке `Notices`. Ваши права и обязанности
в отношении этих компонентов определяются этими лицензиями. Ничто в этих условиях не ограничивает права,
предоставленные вам лицензией стороннего компонента. Сторонние материалы в составе Windows ML
подчиняются своим лицензиям (см. `ThirdPartyNotices.txt` Windows ML в папке `Notices`).

**6. Данные и сетевые подключения.** По результатам чтения исходного кода собственный код LoggyBat:
- соединяется с Logitech G HUB на этом же компьютере (`ws://localhost:9010`), чтобы получать заряд
  устройств;
- проверяет наличие новой версии LoggyBat, отправляя запрос в GitHub
  (`https://api.github.com/repos/mrmaxxxx/LoggyBat/releases/latest`): автоматически, если вы это не
  выключили, — после успешной проверки не чаще раза в сутки, после неудачной попытки следующая не
  раньше чем через час, — и каждый раз, когда вы нажимаете «Проверить сейчас» в настройках виджета,
  независимо от этих интервалов. GitHub получает обычные данные такого запроса, в том числе IP-адрес вашего компьютера, и
  User-Agent `LoggyBat/<версия>`; сведения о ваших устройствах не передаются. LoggyBat только читает
  номер версии и адрес страницы последнего релиза; он ничего не скачивает и не устанавливает.
  Автоматическая проверка включена по умолчанию и выключается в настройках виджета («Проверять
  обновления автоматически»); пока она выключена, LoggyBat не отправляет запросов в GitHub, если вы не
  нажмёте «Проверить сейчас». Обработка запроса GitHub регулируется заявлением GitHub о
  конфиденциальности (GitHub General Privacy Statement):
  https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement;
- пишет файл журнала и результат последней проверки обновлений на этом же компьютере. Настройки
  виджета хранит WidBar.

Поведение сторонних компонентов и библиотек в составе LoggyBat автором LoggyBat не проверялось; оно
определяется условиями их поставщиков. Для компонентов Microsoft см. заявление Microsoft
о конфиденциальности: https://aka.ms/privacy.

**7. Товарные знаки.** LoggyBat не связан с Microsoft, Logitech или WidBar, не спонсируется и не
одобряется ими. Названия продуктов принадлежат их владельцам.

**8. Контакт.** Вопросы и сообщения о проблемах: https://github.com/mrmaxxxx/LoggyBat/issues
