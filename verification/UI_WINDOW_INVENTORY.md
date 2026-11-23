# Аудит точек открытия UI 0.27.0

Это список исходных точек открытия и маршрутизации, не результат запуска WPF на Windows.

| Файл | Формы / окна | Сообщения в общем оформлении |
|---|---:|---:|
| AgentCertificates.cs | 1 | 0 |
| AgentEventPages.cs | 5 | 2 |
| AgentPages.cs | 7 | 5 |
| App.xaml.cs | 0 | 1 |
| AppMessageBox.cs | 1 | 0 |
| BackupPages.cs | 7 | 3 |
| CatalogPages.cs | 5 | 4 |
| ConnectivityPage.cs | 1 | 0 |
| DailySummaryPages.cs | 2 | 0 |
| DiagnosticPages.cs | 2 | 2 |
| DmarcPages.cs | 0 | 1 |
| FormDialog.cs | 0 | 1 |
| InventoryPages.cs | 5 | 3 |
| InventoryPrinting.cs | 1 | 1 |
| ListInspector.cs | 1 | 0 |
| MailBindingPage.cs | 1 | 1 |
| MailSettingsPages.cs | 6 | 5 |
| MailUpdatePages.cs | 2 | 1 |
| MailViewer.cs | 2 | 1 |
| MainWindow.xaml.cs | 0 | 2 |
| NetworkRuleEditor.cs | 2 | 2 |
| OverviewPage.cs | 5 | 3 |
| ReportPages.cs | 1 | 1 |
| RouterAuditPages.cs | 1 | 1 |
| RouterPages.cs | 1 | 4 |
| SearchPage.cs | 1 | 0 |
| SettingsPages.cs | 10 | 3 |
| TraySupport.cs | 0 | 1 |
| UpdateIndicator.cs | 1 | 1 |
| UserPages.cs | 4 | 2 |
| VaultPages.cs | 3 | 0 |
| WatchdogPackagePage.cs | 4 | 0 |

Общие FormDialog/DialogWindow не зависят от глубины Owner. Все существующие сообщения клиента перенаправлены на AppMessageBox; системные окна выбора файлов/печати остаются Windows. В раннем запуске без ресурсов приложения предусмотрен безопасный штатный fallback.

Отдельные установщики: AgentSetup получил тот же ResourceDictionary и общий диалог сообщений; Setup/Update/Uninstall сервера получили рекурсивную WinForms-палитру и перенос кнопок. Сообщения WinForms также переведены на SetupMessageBox.

Ручной проход Windows: устройство → агент → роли → изменение службы → ошибка/подтверждение; устройство → пароль → показать/копировать; MikroTik → конфигурация → событие → исключение; отчёты → письмо → привязка → словарь → сохранение; счёт → история → отмена оплаты; сотрудник → имущество → акт → печать; настройки → почта → тест → ошибка; настройки → сертификаты → подробности; повторный вход из каждой формы. Проверить Tab/Enter/Esc, owner/фокус, прокрутку, DPI 100/125/150%.
