# Установка и удаление / Setup and removal

## Русский

Выпуск 0.46.14: сервер и клиент устанавливаются через соответствующий Setup.exe. Язык выбирается до запуска мастера. Агент устройств использует графическую оболочку пакета. В разделе «Рабочие станции» сохранение установщика создаёт общий MSI и соседний `.Setup.exe`; для ручной установки достаточно одного `.Setup.exe`: MSI включён внутрь. Для GPO и KSC используется MSI без оболочки.

Графические утилиты клиента, сервера и агента устройств используют уже установленный для этих компонентов .NET Desktop Runtime. Установщик и деинсталлятор рабочих станций поставляются автономно с runtime внутри и не требуют его отдельной установки. MSI для GPO/KSC по-прежнему содержит автономную службу.

Клиент и сервер регистрируют удаление в «Установленных приложениях» Windows. У сервера также есть пункт удаления в «Пуск» и в окне управления сервером. MSI агента рабочих станций регистрируется самим Windows Installer. Удаление агента устройств доступно в его установщике и приложениях Windows.

По умолчанию локальные данные и серверная база сохраняются. Удаление базы и служебной учётной записи включаются отдельно. Перед удалением проверяются служба, выбранный каталог, принадлежность базы и отсутствие файловых ссылок. Системные базы SQL Server не удаляются. Если запущенный клиент не удалось закрыть, необходимо выбрать «Выход» в трее и повторить удаление. Серверная база, используемая другой известной установкой, не удаляется.

Удаление программы не отзывает регистрацию агента на сервере: это отдельное действие администратора. Утилиты для старых ручных сценариев сохранены в scripts/legacy, в публикуемые меню удаления они не включены.

## English

Server and client Setup.exe packages offer RU/EN before opening the wizard. Saving a workstation installer creates a shared MSI and a sibling `.Setup.exe`; the `.Setup.exe` embeds the MSI and can be distributed by itself. GPO/KSC deployment uses the MSI directly.

Server, client and device-agent removal tools use their existing Desktop Runtime. Workstation setup and removal tools include their own runtime and need no separate .NET installation. GPO/KSC uses the self-contained MSI.

Windows Installed Apps exposes removal for installed components. The server also exposes it in Start and its management window. Data is retained by default. Database and managed-account removal require explicit selections and confirmation. Validation refuses system SQL databases, filesystem links and known shared databases. Removing an agent does not revoke its server registration.

Windows UI execution, clean-install/upgrade/removal and GPO deployment must be verified on Windows before distributing a release. See verification/RELEASE_0.46.14.md.


Каждый установщик копирует Uninstall.exe в папку установленной программы. Отдельный соседний деинсталлятор при переносе Setup.exe не требуется. PUBLISH проверяет это до удаления временных сборочных каталогов.

Each installer deploys Uninstall.exe inside its component directory. There is no separate uninstaller to carry beside Setup.exe. PUBLISH verifies embedded payloads and the MSI File table.
