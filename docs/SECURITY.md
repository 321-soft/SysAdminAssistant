# Безопасность / Security

Три разных механизма доверия не взаимозаменяемы:

- HTTPS подтверждает сервер мониторинга; применяются отпечатки и подписанные настройки доверия агентов.
- Подпись выпусков SysAdmin Assistant подтверждает манифест и точные байты пакета. SHA-256 без доверенной подписи не подтверждает издателя.
- Authenticode определяет доверенного издателя для Windows. У текущей публичной схемы без платного сертификата такой подписи нет. Сведения «321.su» в файле и установщике не убирают «Неизвестный издатель» или SmartScreen. Отключение защиты не предлагается.

Репозиторий должен содержать только исходники, публичные ключи, шаблоны и документацию. Не включайте server.json, settings.json, bootstrap.json, БД, резервные копии, журналы, токены ботов, персональные словари, приватные сертификаты и release-private.xml. .gitignore предотвращает типичные ошибки, но не очищает уже отслеживаемую историю Git. Перед первой публичной загрузкой выполните собственный просмотр состава и секретов.

Агенты предназначены для наблюдения, а не удалённого исполнения команд. Обновление подписанного агента — ограниченное обслуживание собственной программы. Быстрые подключения Web/SSH/RDP запускают клиентские приложения пользователя и не меняют полномочия агента. Сервисная связь пароля не предоставляет доступа к чужому хранилищу.

Критичные данные не должны попадать в диагностические сообщения и Telegram. Исходные письма и журналы отображаются как данные; перевод не должен менять их содержание и условия сопоставления. Отключение проверки сертификата, подписи или защиты Windows не является способом исправления ошибки связи.

## English

HTTPS server trust, signed release verification and Windows Authenticode are separate mechanisms. A server certificate cannot sign Windows software. Publisher metadata cannot suppress Unknown publisher or guarantee SmartScreen reputation. Update packages use a separate publisher signature and SHA-256; verification is not disabled.

Publish source, templates and public keys only. Exclude databases, settings, bootstrap/enrollment files, logs, backups, bot tokens, private certificates, personal dictionaries and private release keys. .gitignore does not remove already committed secrets from Git history. Review the complete package before making it public.

Agents report health and logs and do not execute administration commands. Web/SSH/RDP shortcuts launch the user's client applications. Linking a vault record to a service does not bypass ownership or permissions. Preserve original evidence and never include secrets in diagnostic notifications.

Security contact: support@321.su.
