# Автосправочник предупреждений

Выпуск 0.2.6 Alpha. Источники проверены 09.10.2026. Каталог доступен без интернета; сеть используется только по нажатию ссылки на официальную статью.

## Как работает

Анализатор выбирает статьи по определённому продукту и ищет устойчивые сообщения/коды в теле письма. Тема, адрес отправителя, метаданные задания/узла, счётчики и строки цитат не используются как доказательство причины. Некоторые явно обозначенные старые/устранённые сообщения исключаются. Это ограниченные эвристики, не полноценное понимание произвольного текста: реальные форматы EML необходимо проверить отдельно.

Каждая найденная статья содержит объяснение, последствия, шаги проверки, точность, применимость, дату проверки и исходные строки. До трёх строк на причину; дубли объединяются. Несколько независимых причин показываются вместе. Совпадение статьи не доказывает, что объяснены все строки письма или что выполнен весь бэкап.

Справочник не меняет BackupResult, Severity, правило привязки или устройство; не выполняет восстановление, форматирование, SQL-команды и изменения настроек. Статусы и уровни определяются существующим анализом и редактируемыми словарями IMAP. Нормализованное описание сохраняется в БД и переносится существующим экспортом. Известные рекомендации появляются также у успешного отчёта, если в нём присутствует соответствующее сообщение.

При неизвестной причине сохраняются до 12 строк сообщений общей длиной до 4000 символов. Если нет распознаваемой строки, описание/основание остаются такими, какие извлёк существующий анализатор. Полное письмо остаётся только в очереди до распознавания. Уже обработанные старые записи нельзя полностью пересчитать без повторного импорта исходного EML.

Лимиты справочника: первые 500000 символов, первые 4000 строк, строка до 4000 символов, 100 мс на отдельное регулярное выражение. Достижение лимита сопровождается указанием проверить полный журнал. Справочник не распознаёт все языки и версии: начальное покрытие — проверенные английские формулировки, общие коды и отдельные русские метки кодов. При неизвестном продукте рекомендации не назначаются по чужому коду.

## Где искать

Настройки → Почта / IMAP → Справочник предупреждений. Фильтры: продукт и текст/код. В очереди и истории — «Разбор отчёта», в событиях — «Подробности». Официальные ссылки открываются только по нажатию. Ссылки, полученные из произвольного текста письма, автоматически не открываются.

## Покрытие и источники

| Код статьи | Продукт | Сценарий | Источник |
|---|---|---|---|
| `veeam.config.encryption` | Veeam | Конфигурация сохранена не полностью: отключено шифрование | [Официальная статья](https://helpcenter.veeam.com/docs/vbr/userguide/config_backup_encrypted.html) |
| `veeam.copy.blocksize` | Veeam | Размер блоков исходной копии не подходит для Backup Copy | [Официальная статья](https://www.veeam.com/kb1835) |
| `veeam.copy.incomplete` | Veeam | Первичное копирование не завершено | [Официальная статья](https://www.veeam.com/kb1835) |
| `veeam.hyperv.cbt` | Veeam | Не удалось сохранить данные отслеживания изменений | [Официальная статья](https://www.veeam.com/kb1934) |
| `veeam.vmware.snapshots` | Veeam | Существующий snapshot мешает включить CBT | [Официальная статья](https://www.veeam.com/kb3169) |
| `sql.backup.permissions` | SQLBackupAndFTP | SQL Server не может записать файл копии | [Официальная статья](https://sqlbackupandftp.com/blog/cannot-open-backup-device-operating-system-error-5access-is-denied/) |
| `sql.login.denied` | SQLBackupAndFTP | Сервер базы отклонил доступ | [Официальная статья](https://sqlbackupandftp.com/blog/access-denied-for-user/) |
| `sql.certificate` | SQLBackupAndFTP | Сертификат SQL Server не прошёл проверку доверия | [Официальная статья](https://sqlbackupandftp.com/docs/docs/troubleshooting-and-support/self-troubleshooting/failed-to-connect-to-database/) |
| `sql.pgdump.version` | SQLBackupAndFTP | Версия pg_dump не совместима с PostgreSQL | [Официальная статья](https://sqlbackupandftp.com/docs/docs/troubleshooting-and-support/self-troubleshooting/failed-to-connect-to-database/) |
| `sql.chain` | SQLBackupAndFTP | Нарушена ожидаемая цепочка копий | [Официальная статья](https://sqlbackupandftp.com/docs/docs/job-settings/backup-job/backup-job-options/sql-server/) |
| `sql.settings.lock` | SQLBackupAndFTP | Заблокирована служебная база SQLBackupAndFTP | [Официальная статья](https://sqlbackupandftp.com/docs/docs/troubleshooting-and-support/self-troubleshooting/timeouts-and-limits/) |
| `sql.ag.replica` | SQLBackupAndFTP | Реплика Availability Group недоступна для этой операции | [Официальная статья](https://sqlbackupandftp.com/docs/docs/troubleshooting-and-support/self-troubleshooting/timeouts-and-limits/) |
| `sql.service.timeout` | SQLBackupAndFTP | Истекло ожидание локальной службы | [Официальная статья](https://sqlbackupandftp.com/docs/docs/troubleshooting-and-support/self-troubleshooting/timeouts-and-limits/) |
| `abb.disk.23` | Active Backup for Business | Ошибка чтения диска: код 23 | [Официальная статья](https://kb.synology.com/en-us/DSM/tutorial/How_to_fix_Active_Backup_for_Business_error_code_23) |
| `abb.io.1117` | Active Backup for Business | Сбой ввода-вывода при доступе к снимку | [Официальная статья](https://kb.synology.com/en-us/DSM/tutorial/How_to_fix_Active_Backup_for_Business_error_code_1117) |
| `abb.vss.missing` | Active Backup for Business | Агент не нашёл снимок VSS | [Официальная статья](https://kb.synology.com/en-us/DSM/tutorial/How_to_fix_Active_Backup_for_Business_error_code_0x80042308) |
| `abb.vss.provider` | Active Backup for Business | Ошибка поставщика VSS | [Официальная статья](https://kb.synology.com/hu-hu/DSM/tutorial/How_to_fix_Active_Backup_for_Business_error_code_0x8004230f) |
| `abb.vss.volume` | Active Backup for Business | Не удалось создать снимок тома | [Официальная статья](https://kb.synology.com/hu-hu/DSM/tutorial/How_to_fix_Active_Backup_for_Business_error_code_0x8004230c) |
| `dsm.raid.degraded` | DSM | Пул хранения потерял часть отказоустойчивости | [Официальная статья](https://kb.synology.com/en-us/DSM/help/DSM/StorageManager/storage_pool_repair?version=7) |
| `dsm.space` | DSM | Недостаточно свободного места на томе | [Официальная статья](https://kb.synology.com/index.php/da-dk/DSM/help/DSM/StorageManager/volume_view_usage?version=7) |
| `hbs.folders` | QNAP, QNAP HBS | Пара папок недоступна или некорректна | [Официальная статья](https://www.qnap.com/en-us/how-to/faq/article/why-did-my-hbs-job-fail-with-a-folder-pairs-are-invalid-or-inaccessible-error-when-backing-up-or-syncing-to-an-external-device) |
| `hbs.versions` | QNAP, QNAP HBS | Не удалось создать несколько версий копии | [Официальная статья](https://www.qnap.com/en/how-to/faq/article/why-hbs-prompt-warning-failed-to-create-multiple-versions-when-creating-backup-job-to-external-drive) |
| `hbs.attributes` | QNAP, QNAP HBS | Не все расширенные атрибуты перенесены | [Официальная статья](https://www.qnap.com/en/how-to/faq/article/how-can-i-deal-with-the-warning-not-all-extended-attributes-are-copied-in-hbs) |
| `qnap.pool.degraded` | QNAP, QNAP HBS | Пул QNAP работает с деградировавшим RAID | [Официальная статья](https://docs.qnap.com/operating-system/qts/5.2.x/en-us/storage-pool-status-4469DFB5.html) |
| `qnap.pool.rebuild` | QNAP, QNAP HBS | Идёт восстановление RAID | [Официальная статья](https://docs.qnap.com/operating-system/qts/5.2.x/en-us/storage-pool-status-4469DFB5.html) |
| `qnap.pool.readonly` | QNAP, QNAP HBS | Пул недоступен для обычной записи | [Официальная статья](https://docs.qnap.com/operating-system/qts/5.2.x/en-us/storage-pool-status-4469DFB5.html) |
| `qnap.pool.threshold` | QNAP, QNAP HBS | Использование пула достигло порога | [Официальная статья](https://docs.qnap.com/operating-system/qts/5.2.x/en-us/storage-pool-status-4469DFB5.html) |
| `qnap.pool.space` | QNAP, QNAP HBS | В пуле осталось мало места | [Официальная статья](https://docs.qnap.com/operating-system/qts/5.2.x/en-us/storage-pool-status-4469DFB5.html) |
| `windows.vss.space` | Veeam, Active Backup for Business | Не хватает места для теневого снимка | [Официальная статья](https://learn.microsoft.com/en-us/windows/win32/api/vsprov/nf-vsprov-ivssprovidercreatesnapshotset-endpreparesnapshots) |
| `windows.vss.timeout` | Veeam, Active Backup for Business | Компонент VSS не уложился в время | [Официальная статья](https://learn.microsoft.com/en-us/windows/win32/vss/writer-errors-and-vetoes) |
| `windows.vss.retry` | Veeam, Active Backup for Business | Временная ошибка writer VSS | [Официальная статья](https://learn.microsoft.com/en-us/windows/win32/vss/writer-errors-and-vetoes) |
| `windows.vss.nonretry` | Veeam, Active Backup for Business | Серьёзная ошибка writer VSS | [Официальная статья](https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/backup-fails-vss-writer) |
| `sql.log.no-full` | SQLBackupAndFTP | Нельзя создать копию журнала без начальной копии базы | [Официальная статья](https://sqlbackupandftp.com/blog/backup-log-cannot-be-performed-because-there-is-no-current-database-backup/) |

## Свои статьи

В окне справочника нажмите «Добавить статью». Выберите продукт (или «Любой» для общей статьи) и задайте фразу из тела предупреждения, объяснение, последствия, шаги и при необходимости HTTPS-источник. Фраза ищется как обычный текст без учёта регистра, не как введённое регулярное выражение. Метаданные задания и цитаты исключаются теми же эвристиками, что и во встроенном анализе. Свои статьи можно изменять, отключать и удалять; встроенные остаются отдельно.

Свои статьи сохраняются в settings.json на этом компьютере. Новый текст применяется к следующим письмам и повторному анализу нераспознанной очереди. Уже сохранённые объяснения остаются снимком прежней статьи. Worker получает обновлённые настройки на следующем цикле. Собственные статьи не считаются проверенной документацией производителя и помечены «Рекомендация пользователя».

## Расширение

Каталог `src/SysAdminAssistant.Core/Diagnostics/catalog.json` встроен в Core. Для новой статьи нужны отдельный стабильный ID, продукт, узкое выражение/код, объяснение с границами уверенности, последствия, безопасные проверки, официальный URL, версия/область применения и дата проверки. Добавляйте положительные и отрицательные примеры в `verification/diagnostic-fixtures.json`, затем запускай Python-проверки и `VERIFY.cmd` на Windows. Обновление справочника требует пересборки Core и обновления Desktop/Worker вместе.

Реальные названия устройств, заданий, адреса и доменные имена не являются встроенными правилами. Пользователь выбирает привязку отдельно. Собранные EML позволят проверить форматирование, различие текущих ошибок и истории, локализацию и неизвестные сообщения; их содержимое не следует добавлять в публичные фикстуры без обезличивания.
