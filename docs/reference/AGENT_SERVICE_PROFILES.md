# Профили служб агента

Профили находятся в карточке устройства → Агент → Роли и службы → Роли сервера. Можно выбрать несколько. До сохранения виден список добавляемых и удаляемых служб; источник виден в таблице. Это настройка мониторинга, не установка/запуск/остановка служб.

| Роль | Основной контроль | Условные компоненты |
|---|---|---|
| AD DS | NTDS, Kdc, Netlogon, LanmanServer, RpcSs, SamSs, EventSystem, LanmanWorkstation, Dnscache, W32Time | DNS/ADWS если установлены и не Disabled; DFSR/NtFrs при Automatic или Running |
| DNS | DNS | не Dnscache |
| DHCP | DHCPServer | не клиентская Dhcp |
| SQL | MSSQLSERVER, MSSQL$INSTANCE из инвентаря | SQLSERVERAGENT/SQLAgent$INSTANCE и SQLBrowser при Automatic/Running |
| 1С | службы агентов сервера по системному/отображаемому имени | несколько установок; не процессы rmngr/rphost и не удалённая SQL |
| SMB | LanmanServer, RpcSs, SamSs | SrmSvc при Automatic/Running; DFS отдельно |
| DFS Namespace | Dfs, LanmanServer, RpcSs | только сервер пространства имён |
| DFS Replication | DFSR | только участник репликации |
| IIS | W3SVC, WAS | проверка сайтов отдельно |
| Hyper-V | vmms | только хост |
| RDS Session Host | TermService | SessionEnv/UmRdpService при Automatic/Running |
| AD CS CA | CertSvc | не штатно выключенный офлайн CA |

Фиксированная основная служба выбранной роли ожидается даже при отсутствии. Имена экземпляров SQL/1С берутся из отчёта агента; если не найдены, выдаётся предупреждение о необнаруженной основной службе. Disabled экземпляры при первом обнаружении не добавляются. Ранее обнаруженная служба после исчезновения сохраняется: её удаление не превращает аварию в зелёный статус. При недоступности инвентаря старые правила не вычищаются.

Важность: W32Time/ADWS/SQLBrowser — Warning; остальные — Critical. Задержка профиля 2 минуты; ожидание после загрузки — текущая настройка устройства. Значения можно переопределить вручную. Один профиль не должен навязывать универсальное Running всем вспомогательным службам: VSS, ручные/триггерные компоненты, Spooler и службы обновления не добавляются просто по наличию. На кластерах/пассивных узлах требуются ручные ожидания или исключения.

Удаление роли убирает только её источник. При нескольких владельцах строка остаётся. Ручная запись побеждает профиль. Изменение строки делает её ручной; приостановка создаёт ручное disabled-правило; удаление убирает строку и сохраняет исключение отдельно. Старые JSON-записи без происхождения неотличимы от ручных, поэтому сохраняются. Они явно помечены «Ручная / прежняя настройка» и могут быть удалены вручную. Новые автоматически обнаруженные записи помечаются источником, включая базовые Windows. После первого сохранения ролей автоматическое угадывание AD больше не возвращает снятую роль.

Лимит — 64 сохранённых правила. Если экземпляров слишком много, отчёт агента не отклоняется: сохраняются ручные и ранее отслеживаемые записи, об остальных выдаётся предупреждение. Выбрать нужные экземпляры вручную. Политика хранится на сервере, пересобирается при сохранении и поступлении отчёта; версия меняется только при изменениях. Агент получает плоский список, поэтому существующий агент уже умеет выполнять проверки; новые поля происхождения ему не нужны.

Источники сверены 23.10.2026 (официальная документация):
- Microsoft DCDiag, Services/SysVolCheck: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/dcdiag
- SQL services / instance naming: https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/start-stop-pause-resume-restart-sql-server-services
- SQL Browser conditional use: https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-browser-service-database-engine-and-ssas
- 1Ci service naming and server processes: https://kb.1ci.com/1C_Enterprise_Platform/FAQ/Administration/Server/1C_Enterprise_Server_is_not_working__Basic_troubleshooting/
- IIS WAS/W3SVC: https://learn.microsoft.com/en-us/iis/get-started/introduction-to-iis/introduction-to-iis-architecture
- FSRM SrmSvc: https://learn.microsoft.com/en-us/windows-server/storage/fsrm/fsrm-overview
- Services DNS/DHCP/DFS/SMB: https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/service-overview-and-network-port-requirements
- Hyper-V vmms: https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/manage/configure-non-uniform-memory-access
- CA CertSvc: https://learn.microsoft.com/en-us/troubleshoot/windows-server/certificates-and-public-key-infrastructure-pki/certificate-services-not-start

Каталог не утверждает полноту всех продуктов и редакций. Проверка служб не заменяет диагностик AD/DNS/репликации, запросов SQL, проверки сайта и проверки файловых ресурсов.
