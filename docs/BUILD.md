# Сборка / Build

## Русский

1. Используйте Windows x64 и установленный .NET 10 SDK, совместимый с global.json. SDK нужен только на машине сборки. На сервере и клиенте требуются runtime ветки 10.0 x64, минимум 10.0.14; для сервера также ASP.NET Core Runtime.
2. Распакуйте исходники в локальный каталог и запустите PUBLISH.bat. VERIFY.cmd собирает решение и запускает основные и сетевые спецификации; при ошибке публикация останавливается.
3. Формат `1` создаёт SysAdminAssistant_Client_Setup.exe и SysAdminAssistant_Server_Setup.exe в publish/Release_0.46.14/Installers; `2` оставляет Client и ServerSetup; `3` сохраняет оба варианта.
4. Для форматов 1 и 3 ответьте «Да» на вопрос «Включить WiX в установщик клиента?», чтобы вложить пакет WiX 4.0.6 из NuGet. Публикация его только скачивает. В Setup.exe появится выключенная по умолчанию галочка «Установить WiX для сборки MSI». Для выбранного компонента нужен .NET 10 SDK x64; для самого клиента достаточно Desktop Runtime.
5. Полный выпуск собирается за один запуск PUBLISH.bat. Если WiX 4.0.6 отсутствует, публикация подготавливает его в .tools внутри проекта. Системная установка и PATH не меняются. При ошибке подготовки публикация останавливается, не создавая неполный комплект без MSI рабочих станций.
6. На запрос загрузки .NET выберите Y или N. N не отменяет сборку: уже имеющиеся RuntimePackages используются, отсутствующие runtime устанавливаются на целевой машине отдельно. Пакеты скачиваются с Microsoft и проверяются; SDK в установщик не включается. Чтобы Windows-агенты могли сами обновить .NET, выберите Y или заранее подготовьте проверенный windowsdesktop-runtime в RuntimePackages: PUBLISH включит его в серверный AgentTemplate.
7. Переносите целиком выбранные папки либо отдельный нужный Setup.exe. Установщик предлагает русский и английский язык (начальный выбор соответствует Windows или предыдущему выбору) и соглашение. Серверный мастер сохраняет отдельные каталоги программы и данных.

При первом запуске PUBLISH создаёт закрытый ключ выпуска в `%LOCALAPPDATA%\SysAdminAssistantPublisher\release-private.xml`. Публичная часть включается в Core. Сделайте защищённую резервную копию закрытого ключа: без него нельзя подписать следующий выпуск для уже установленных клиентов. Не помещайте ключ в проект, Git, архив исходников или веб-каталог. Если публичный ключ в исходниках уже принадлежит другой установке издателя, скрипт откажется молча заменять его. Для отдельного форка создавайте собственную цепочку доверия; обновление подписанных чужим ключом установок не поддерживается.

Пути будущих выпусков можно задать **до** публикации:

```bat
set "SAA_RELEASE_BASE_URL=https://assistant.321.su/releases/0.46.14"
set "SAA_RELEASE_MIRROR_BASE_URL=https://github.com/OWNER/REPOSITORY/releases/download/v0.46.14"
PUBLISH.bat
```

Замените OWNER/REPOSITORY своими значениями; переменная зеркала необязательна. Подписанный release.json содержит адреса и SHA-256 обоих Setup.exe. Изменение JSON вручную нарушает подпись. Обновите версию в Directory.Build.props для нового релиза; не публикуйте разные бинарники под одной версией обслуживания.

## English

Build on Windows x64 with the .NET 10 SDK compatible with global.json. Run PUBLISH.bat. Verification must pass before publishing. Choose `1` for the two Setup.exe files, `2` for Client/ServerSetup folders, or `3` for both. Runtime bundling is optional; choosing N does not require downloading packages. Target machines need runtime 10.0.14 or a later 10.0 patch, not the SDK. Include the verified Desktop Runtime installer in the server package to enable unattended Windows agent runtime patching.

The first publish creates a private signing key under `%LOCALAPPDATA%\SysAdminAssistantPublisher`. Back it up privately and retain it for future releases. Only the public key belongs in source. A different existing public key blocks silent trust replacement. Forks must establish their own trust chain.

Set SAA_RELEASE_BASE_URL and optionally SAA_RELEASE_MIRROR_BASE_URL before publishing to generate signed package URLs for your hosting and GitHub Releases. Upload the exact generated installers and manifest; do not edit signed data. The Windows inbox .NET Framework compiler builds the bootstrap, so users can start setup before installing .NET 10.

## Структура исходников / Source layout

- `src/` — приложения и библиотеки C#.
- `tests/` — исполняемые спецификации, включая Windows UI.
- `packaging/` — исходники графических оболочек и WiX.
- `scripts/build/`, `scripts/release/`, `scripts/install/`, `scripts/diagnostics/` — вспомогательные сценарии.
- `scripts/legacy/` — прежние инструменты для ручного обслуживания.
- `docs/legal/` — RU/EN соглашение; `LICENSE.txt` — лицензия исходного кода.

В корне остаются BUILD.cmd, VERIFY.cmd и единственная пользовательская точка публикации PUBLISH.bat. Скрипт scripts/build/PUBLISH.cmd внутренний.

[Установка и удаление / Setup and removal](INSTALL_AND_REMOVE.md)
