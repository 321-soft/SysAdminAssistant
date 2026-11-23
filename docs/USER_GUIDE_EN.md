# SysAdmin Assistant

## Overview

The server service runs monitoring independently of the client. The dashboard shows devices, backups, switches and bills. Devices with problems appear above healthy devices. Gray means there is no confirmed data, not that a check succeeded.

Red indicates an active critical problem, amber a warning and green confirmed healthy status. The update icon is separate from device health. Open a row for the cause, check time and history. Starting the client or refreshing its list does not replace server monitoring.

F5 reloads the current list. New data should not rebuild an open edit form: refresh when editing is finished. Ctrl+K opens application search. F1 opens help.

Open “Customize workspace” at the bottom of the window. The 2×4 and 4×2 sizes specify width and height in cells. On narrow screens, cards fit the available columns. Select “Fill 100% of row width” to use the spare space on the right. “Fill 100% of available height” stretches the rows to the bottom of the card area. Scrolling remains available when the cards do not fit. The setting is saved per section and user.

## Connection and sessions

Enter the server HTTPS address, user name and password. Obtain the SHA-256 fingerprint from setup or server information. Verify it through an independent trusted channel: an open TCP port alone does not authenticate a server.

“Keep signed in until the application closes” retains the ordinary session while the client runs. A prolonged connection loss or permission revocation requires signing in again. Personal data and secrets require separate confirmation. Hiding the window in the tray does not exit the client; use Exit.

For connection failures, check the server service, route, HTTPS port, trusted networks and fingerprint. Do not disable certificate validation to work around TLS errors. The principal administrator can change trusted networks in server settings.

## Devices and connections

Devices stores equipment, availability addresses, sites, agent links and backup job links. Adding a device does not create a backup job; configure its schedule separately. Configure RouterOS equipment under Switches.

Device settings include LAN/WAN targets, agent policy, thresholds and Web, SSH and RDP shortcuts. Web uses the default browser, RDP uses the Windows client and SSH uses the installed protocol handler. Assistant does not execute commands through the monitoring agent.

Disk and memory bars reflect the latest agent report. Linux shows mount points; Windows shows volumes. Check the sample time, percentage, absolute free space and thresholds. OS updates have a separate icon and should not make an otherwise healthy device critical.

## Services and availability

Services are checked by the server using their configured protocol and address. These are HTTP/TCP checks, not the systemd or Windows service list; configure operating system services in a device's agent policy.

Availability can use several LAN and WAN targets. The default alert delay is two minutes: a brief outage that recovers before the delay expires should not generate an outage notification. This does not replace backup scheduling.

A password can link to a device and a service simultaneously without duplication. A link grants no additional access to the secret. Do not place passwords or tokens in HTTP check URLs.

## Backups

Create a job and select the device, backup system and expected completion times. The ±N minute tolerance covers early and late completion. Each expected run is evaluated separately: yesterday's successful report does not confirm today's backup.

Messages are first linked to a job, then analyzed using the source dictionary. “Report received” without a recognized result does not mean success. Dictionaries consider context and counters: Error 0 must not be treated as failure solely because it contains Error.

For missing reports, check IMAP diagnostics, actual receipt time, linking rules and the job window. Check mail now starts server-side collection. Mail delivery failure and missing backup confirmation are distinct; a missing report does not prove that the backup data itself was not copied.

## Mail, linking and dictionaries

Configure an IMAP mailbox in Settings and test the connection. TLS certificates are validated; normal reading does not change the read flag. Invoices and reports have separate collection settings, so the additional integration does not replace existing collection.

Open a message to see its sender, IMAP receipt time, sender date and body. External images and active content are not executed. Link a message using subject, body or both, several phrases and AND/OR matching. Preview matches first: similar emails can belong to different jobs.

Result dictionaries are applied after linking. Do not replace distinctive job identifiers with generic success or error words. Update notices can be linked separately; after installing a version, confirm it in the notice when email is the only source.

## Switches and MikroTik

Add a MikroTik under Switches and configure its API-SSL address, port, read + api account and certificate trust. Specify the router certificate SHA-256 when needed. Assistant HTTPS and RouterOS use different certificates.

Assistant monitors, reads logs and analyzes configuration. Suggested repair commands are for manual use and are not executed by the program. Analysis follows the packet path: ingress, NAT, INPUT or FORWARD, rule order and restrictions including IPsec. Review evidence and analysis limitations before changing a firewall.

Ignoring applies to a specific stable finding and survives polling. WAN/tunnel recovery resolves the problem based on current state. Repeated failed logins restart the quiet period from the latest attempt; no new entries alone do not prove a compromise.

## Windows and Linux agents

Agents report system health and logs. They do not accept administration commands. Create a package from the device settings; it contains server enrollment information and must be kept private. For Debian/Ubuntu, use the generated installation command instead of manually building with the SDK.

Server role profiles help select system services. Add and remove extra services in device settings. CPU, RAM, swap, network, disk space and inodes have separate thresholds. /boot and /boot/efi use specific policies: a healthy small partition does not need several gigabytes free.

A oneshot unit may legitimately become inactive/dead after successful execution. Timers require separate checks of schedule and job result. SSH can use socket activation without a permanently running ssh.service. Inspect the unit type, exit code, last execution and original record for errors.

Agent updates are signed and retain registration and keys. A running service may still fail to send data: check the latest report time, agent log, network route and server trust.

## Windows agent: installation, updates and re-enrollment

A PACKAGE BELONGS TO A DEVICE CARD
Open Devices → the intended device → Agent → Create Windows installer. The package enrolls the agent into that card. The computer name is not checked beforehand: running the package on another computer first can enroll that computer into the card. Create a separate installer for each computer and verify the machine name and key fingerprint before approving enrollment.

PACKAGE NAMES
Default filenames look like DC01_v0.42.10_Setup.exe and DC01_v0.42.10_Update.exe. DC01 is the card name; the version comes from the packaged agent. Invalid filename characters are replaced with underscores. Renaming the file does not change its association.

UPDATE OR REINSTALL
The Windows agent update package preserves the installed agent’s key and enrollment. It cannot enroll a new agent. If the agent has been removed completely, open the same card, revoke the old access if the agent is still listed, create a new Setup.exe, install it and approve the new key. Keep the device card.

VERSION 0.42.3 DOES NOT UPDATE
If version 0.42.3 reports that the expected agent service is missing, run sc.exe query SysAdminAssistantAgent on the monitored machine. If the service exists, run the signed update package from that device card once as administrator. The old updater fails before contacting the server, so updating the server alone cannot fix it. If sc.exe returns 1060, check the installation; do not remove agent data as a diagnostic step.

## Linux agent: updates and troubleshooting

After an Assistant server update, the approved Linux agent downloads the signed release after a successful server exchange. A systemd timer applies the downloaded package. The .exe package is for Windows; use the Linux agent update help in the device card. A working agent does not need to be enrolled again.

Check the timer: systemctl status sysadminassistant-agent-update.timer --no-pager
Read the log: sudo journalctl -u sysadminassistant-agent-update.service -n 30 --no-pager
Apply a package already downloaded: sudo systemctl start sysadminassistant-agent-update.service
Check the installed version: /opt/sysadminassistant-agent/agent --preflight

“No update downloaded” means there is no staged package to apply. Starting the update service does not download a release. Check that the agent has reported recently and that the published server package contains a Linux release.

Include .NET Runtime when publishing the server, or place the verified installer in RuntimePackages beforehand. The Windows agent scheduled updater retrieves .NET Desktop Runtime x64 from Assistant, verifying the signed manifest, file hash and Microsoft SHA-512. It installs the runtime before stopping or replacing the agent, with no automatic reboot. If the installer requests a reboot, the agent replacement waits for planned maintenance. An older working updater first installs the compatible 0.42.14 bridge and then patches .NET on its next check. If the old updater cannot start, use a signed update package for that device.

## Kaspersky Security Center: OpenAPI, account and event log

As an Assistant administrator, open Kaspersky in the sidebar, select Activate, and open the KSC connection settings. No Assistant device entry is needed. The Assistant server connects directly to the KSC Administration Server over HTTPS. Enter a URL such as https://ksc.example:13299 without /api/v1.0. Use the actual OpenAPI port configured in KSC; 13299 is a typical default. Assistant agents are not required on computers managed by KSC.

Create a dedicated internal KSC account such as saa_reader. In Security on the Administration Server itself, check its effective Basic functionality → Read permission. If your KSC version exposes a separate right to connect to the Administration Server, check it as well. Do not grant write, task execution, settings modification or event deletion for a reader. In Assistant, select Internal KSC account and leave the domain blank.

If Windows does not trust the OpenAPI certificate, select OpenAPI certificate → Copy command. Run it in an elevated PowerShell on the KSC server itself. It reads the local klserver.cer and prints a 64-character SHA-256 fingerprint. Paste it into Assistant and select Test connection. If KSC uses a different file, specify the certificate presented on the OpenAPI port. The fingerprint applies only to the configured host and port; the hostname and validity period are still checked. After rotation, verify the new fingerprint on KSC and apply it. The existing connection keeps its cursor and device bindings.

Select Test connection. When it succeeds, save the connection to start background collection of new events. Clear Activate to pause collection while retaining the new settings and cursor. To track problems, open Kaspersky → Kaspersky rules and add the built-in rules or import JSON. Kaspersky has separate Telegram options for critical alerts, warnings and recovery. Delivery uses the bot and chat configured in the general Telegram settings.

## Problems and history

An active problem is different from a retained log event. Repetitions of the same cause are grouped by device, source and signature, preserving first/last times and a counter. A changed cause or severity may create a separate event.

Open a problem for facts, thresholds, safe checks, recommendations and recovery conditions. Automatic resolution requires confirmed healthy state. History remains after recovery. Mark resolved records that an event was reviewed; it does not repair the service, network or disk.

Original-message actions apply only to mail events. Do not delete data, format storage or force a reboot based on a recommendation without your own review and a backup.

## Bills, email and recognition

Manual and recurring payments work independently of invoice recognition. Payments are green before the due-soon window, amber by default three working days before the deadline and red when overdue. Configure the company working week separately. Period history lets you undo an accidental paid mark.

Enable email invoice recognition separately in Settings: mailbox, allowed senders, authentication, supplier rules and learning. It extracts numbers, dates, amounts, currencies and payment details. Low confidence, conflicts and unknown suppliers require review. OCR can misread images; compare amounts and bank details with the original.

Download the PDF or original from the document row. Linking a recurring payment does not mark it paid and must not create another payment for the same period. Confirm supplier and period before enabling automatic creation.

## Passwords and links

The vault protects secrets and requires separate access confirmation. Double-click a record to open it: the password is hidden, with Show password and Copy password actions. The clipboard clears after the configured interval if its contents have not changed.

One record can link to a device and a service without duplicating the secret. Permission to view a linked service does not grant access to another user's vault. The principal administrator assigns section permissions.

Excel export may contain plaintext passwords; the application warns before exporting. Keep the file in a protected location and remove it after use. Do not send passwords to logs, Telegram, support messages or screenshots.

## Inventory and Excel

Asset records store asset numbers, specifications, prices, condition and assigned employees. Issue, return and write-off use certificates. A finalized certificate retains a snapshot of details and specifications at the time of the operation; later edits do not rewrite it.

Download the Excel template before importing. Complete the data sheet using its instructions and inspect the preview. Updating by ID is intended for previously exported records; do not replace IDs manually. Correct invalid employee or device references before saving.

Employee and certificate access requires permission and password confirmation. Terminating employment does not delete history; record asset returns first. Print to PDF using Microsoft Print to PDF.

## DMARC, SPF and DKIM

DMARC reports show sending sources and domain authentication results. Import XML and supported archives. These reports are not mailbox availability checks and do not prove every message is authentic.

For incoming notices, authserv-id identifies the receiving system in Authentication-Results, not each sender. Yandex uses multiple mxfront nodes; select its receiving-system profile and controlled trust rules instead of one incidental host from a header.

Trust results only when the receiving system removes forged headers using its own identifiers. SPF/DKIM and alignment with From are evaluated according to the selected mode. Do not add sender domains as trusted authserv-ids.

## Telegram and daily summary

Configure the bot token, Chat ID, severity levels and event groups. Categories let you exclude unwanted messages. Send a test. To pin the active problem list, the bot needs permission to pin messages in the group.

Ordinary alerts use red or amber icons; recovery uses green and, where possible, replies to the original alert. The server maintains the current active problem list in the pin. When no problems remain, it is unpinned; messages and history are not necessarily deleted.

Configure daily summaries under Reports: a separate time for each weekday, new, resolved and remaining active problems. The schedule uses the server time zone. The principal administrator configures server notification language through the client, independently of interface language.

## Settings, certificates and watchdog

Settings are grouped by feature; search finds mailboxes, Telegram, certificates and thresholds. Only the principal administrator changes server trusted networks and notification language. Before changing CIDRs, ensure the new configuration retains a route from the administration computer.

The server uses HTTPS and signed trust settings for agents. Certificate renewal should not require reinstalling agents; check their trust-version acknowledgments. Notify users and configured Telegram destinations of the new fingerprint. Never copy private keys into a public repository.

Host the external PHP watchdog outside the monitored server's site. Download its package from Settings, select the hosting profile and follow START-HERE: private files outside public_html, heartbeat.php inside the public directory, and cron using the correct PHP version. Keep the shared secret private. Test delivery and missed heartbeats; the Telegram bot itself does not probe availability.

## Installation and updates

Building on Windows requires the .NET 10 SDK; target computers need the runtime, not the SDK. PUBLISH.bat offers two Setup.exe installers, the existing folder layout, or both. Choose whether to bundle runtime installers during publishing. Microsoft installers are signature-checked.

Client and server updates start only through the client's Updates button. Server updates require the principal administrator. Release metadata and packages are checked against the publisher key, signature and SHA-256. Changed license terms require interactive setup. Agent updates remain automatic according to their policy.

Server updates create a backup and restart the service; closing the waiting window does not cancel maintenance already started. Do not launch a second update in parallel. On failure, review maintenance status and the backup before retrying.

Without a trusted Authenticode signature, Windows may show Unknown publisher and SmartScreen warnings. Filling in 321.su metadata does not replace that signature. Our update signature verification is a separate mechanism; Windows protection is not disabled.

## Getting started: installation to first check

GOAL
Connect the client and obtain a confirmed first check. The client is the operator workstation; the server collects mail, performs availability checks and analyzes results. Closing the client does not stop the service.

STEPS
1. Run server setup as administrator on the Windows server. Set the HTTPS port, address, trusted private networks and existing/new database settings. Do not select cleanup during an upgrade.
2. Record the server URL and certificate fingerprint. For https://192.168.1.10:7443 test the configured port, not port 443.
3. Enter the HTTPS URL, user and password in the client. Compare the fingerprint with server information over an independent trusted channel.
4. Open Devices and add a name, address and type. Add RouterOS equipment under Switches, without creating a second copy under Devices.
5. Add a service and link it to the device when needed. Ping confirms address reachability, not application health.
6. Wait for a server check. Review the dashboard snapshot and check timestamps. For stale data inspect the server/agent connection in the card.

EXPECTED RESULT
The card contains a recent check. Green means confirmed healthy; gray means insufficient evidence. No errors before a check is not proof of health.

TROUBLESHOOTING
Check the server service, URL, port, Windows Firewall and trusted networks. An open TCP port does not prove a successful TLS handshake or login. Resolve certificate trust instead of disabling validation.

## Dashboard: states and right-click actions

HOW TO USE
The dashboard contains Devices, Backups, Bills and Switches. Each list scrolls separately. All → opens the full section. Left-click opens the card/history; right-click opens actions for that exact row. Keyboard users can use the context-menu key or Shift+F10 on a focused row.

DEVICES
Open the card, agent, problems, services, jobs, linked passwords, report history or configured Web/SSH/RDP connections. Editing, archiving, reanalysis and deletion require the relevant write permissions. Linked passwords still require normal vault authorization.

BACKUPS
History belongs to the selected job. Multiple expected runs share that job’s settings. Open its linked device, edit, reanalyze messages or delete the job. The server may reject deletion when history remains linked.

BILLS
Open the card/history, download the document, mark the current period paid, edit or delete the entry. Paid records a real server operation. Undo an accidental payment in payment history. A PDF can only be downloaded when a document is stored; inspect the card/source otherwise.

SWITCHES
Open health/events, interfaces, configuration audit, monitoring refresh, properties, rules and profile. These operate inside Assistant; RouterOS configuration commands are never applied automatically.

DISABLED ACTIONS
Actions respect section permissions. Changes need write access; passwords and personal data keep additional authorization. Ask an administrator for missing access rather than trying another route through the dashboard.

## Device and agent: metrics, services, thresholds

SETUP
1. Create a device with a stable descriptive name. Its address/type determine basic monitoring.
2. Open the card → Agent. Create its installation package or Linux command. Do not publish enrollment commands or secrets.
3. Install on the target host. Compare the computer name and key fingerprint when approving enrollment.
4. Open agent policy. Enable checks and set CPU, memory, disk and network thresholds appropriate to the workload, rather than one brief peak.
5. Add critical services by system name. Windows display names can differ from service names. On Linux use full systemd unit names such as nginx.service.
6. After saving, compare server policy version with the version applied in the report. Saving settings does not instantly produce another measurement.

READING METRICS
Disk bars show used/free space for a Windows volume or Linux mount point. /boot and /boot/efi have separate policy. Inodes are independent: free gigabytes do not rule out inode exhaustion. Memory shows total and usage; network measurements belong to a specific interface and interval, not lifetime traffic totals.

SERVICES AND TIMERS
A regular service should be running. A successful oneshot may be inactive/dead without an incident. A timer is evaluated by schedule activity and its related job’s result. Update-package failure is separate from a successful run with no staged package.

RECOVERY
Confirm that fresh healthy evidence arrived after the fix. Manual incidents need investigation and acknowledgment; automatic incidents close according to recovery conditions. Do not delete agent enrollment merely to change the status color.

## Backups: two daily runs and missing reports

EXAMPLE
A server mails reports twice daily, after morning and evening runs. Do not create duplicate devices.

1. Add a job under Backups. Set its title, backup system, linked device, weekdays and expected completion times, for example 09:00; 21:00.
2. Set the ± minute tolerance. It covers early/late completion around each time. Avoid overlapping windows that could match multiple runs.
3. Find a real message in mail, open it and choose linking. Use stable distinguishing subject/body strings: job name, NAS name or protected object. Choose AND/OR for multiple lines. AND requires all configured conditions; OR needs one.
4. Select the target job, save and review matched messages. Do not link everything solely by the word backup.
5. Select the correct result dictionary. Linking determines ownership; the dictionary determines success/failure. A received message with an unrecognized result does not confirm success.
6. Reanalyze messages after correcting the dictionary. A dictionary change is not a new report from the backup system.

MISSING TODAY’S REPORT
Check the expected run, tolerance and weekdays, then mailbox access and actual IMAP receipt time. Inspect unrecognized messages, linking and analysis. Yesterday’s success cannot satisfy today’s missing report. Broken mail delivery means confirmation is missing, not necessarily that backup data is damaged.

VALIDATION
Compare the current run with the latest report timestamp/result. Verify at least one real successful run and a safe test failure message.

## Bills: from incoming email to payment

TWO INDEPENDENT WORKFLOWS
Manual recurring payments work without mail recognition. Incoming-invoice processing supplements them rather than replacing them.

MANUAL BILL
1. Open Bills → Add. Set title, amount, due date and recurrence.
2. Review working-day policy. A future payment is normally green, an approaching deadline amber and an overdue one red; lead time and weekends are configurable.
3. After actual payment choose Paid. A recurring bill advances to the next period while keeping payment history.
4. Undo an accidental mark for the specific period in the card/history. Do not create a duplicate bill to correct one mistaken payment.

EMAIL OR FILE INVOICE
1. Configure invoice-specific mail collection and processing rules in Settings, then test the mailbox.
2. Open the incoming queue or Recognize invoice from file and select a supported document.
3. Compare recipient and payer. Payer tax IDs must not replace supplier details. Check number, issue date, due date, total, VAT and bank accounts against the original.
4. Image OCR can misread numbers and layout. Correct fields before saving; a clean-looking image does not guarantee accurate OCR.
5. If needed configure a rule/training example for this format, then verify another document from that sender.
6. Save and check the linked payment. The stored document is available from its card and Download PDF when retained by the server.

MISSING FIELDS
Inspect the original to distinguish absent data from recognition failure. Never invent bank details. Re-uploading the same file is not a substitute for correcting its existing entry.

## MikroTik: connection, rules and configuration audit

CONNECTION
1. Add a MikroTik and its REST/API address under Switches. Use a dedicated read-only account.
2. For a locally untrusted certificate, independently verify its SHA-256 fingerprint before entering trust. A CA certificate and the server certificate are different objects; the pin must match the certificate being checked.
3. Verify model, version, interfaces and fresh logs. Ping reachability does not prove HTTPS API health.
4. Define important WAN, uplink and corporate tunnels in rules. An ordinary user VPN disconnect is not an inter-office outage.

EVENTS
In the MikroTik device card, open Monitoring → Configure device rules. Select a row to edit or toggle it. The rule type selector only filters the list. To replace the whole set, select a replacement profile and confirm the replacement, then select Apply and save the device card.

Rules control severity, delay and Telegram delivery. A new failed login restarts the quiet period from the latest attempt instead of adding another hour. Confirmed recovery of an important interface/tunnel closes its corresponding problem. Missing fresh logs do not confirm security.

CONFIGURATION AUDIT
Review the packet-path conclusion, sources, ingress interface, IP family and examined rules. INPUT protects the router; FORWARD covers transit. DST-NAT changes the later path; IPsec matches apply only to IPsec traffic. One accept rule does not prove public access if another applicable stage blocks the path.

Needs review means incomplete modeling or an unsupported condition, not a confirmed exposed port. Ignore stores an Assistant exception; it does not repair the firewall. Apply suggested commands manually only after reviewing their purpose and rule order. Assistant does not automatically modify RouterOS.

AFTER FIREWALL CHANGES
Run another audit and verify the snapshot time. Where necessary test exposure externally from an authorized test host. Do not disable general WAN protection to test one port.

## Telegram: incidents, recovery, pins and summaries

INITIAL SETUP
1. Create a bot and add it to the intended chat. Enter token and chat ID in notification settings. Keep tokens out of conversations and diagnostic archives.
2. Allow message sending. Group pinning requires administrator permission to pin messages.
3. Send a test notification and verify the destination chat.
4. Select event groups and severity levels. Configure availability delay separately from backup reports: a network outage grace period must not delay missing-backup checks.

READING MESSAGES
Check object name, source, occurrence time, severity and details. Red/amber indicates an active issue; green indicates recovery. Evaluate repeated instances together using occurrence count and latest time.

PINNED STATUS
The pin represents current active problems, not full history. If pinning fails check bot permissions. Removal from the pin does not delete application history. A past event and a currently active incident are different records.

DAILY SUMMARY
Set times by weekday in Reports. Scheduling uses server time zone. The summary covers opened/resolved and remaining active problems; it does not replace urgent critical notifications.

MISSING NOTIFICATION
Check category, severity, delay, server Telegram access and test delivery. Confirm that the event is new/active and not ignored. Do not create hundreds of artificial incidents on production systems for testing.

## External watchdog: PHP hosting and outage test

PURPOSE
A monitoring server that loses connectivity cannot send its own Telegram alert. An external PHP worker receives periodic heartbeats and independently detects when they stop.

DEPLOYMENT
1. Open Settings → External server monitoring → Create hosting ZIP. Select a deployment option and fill hosting paths. Download the generated archive/instructions.
2. Put the public receiver in the selected site’s public_html. Keep config, worker and state outside the public directory. On shared hosting the home directory and site root differ.
3. Use an absolute state path writable by PHP. The directory must exist or be creatable with allowed permissions. Do not choose another site’s or system directory.
4. Use the PHP executable configured by the host. If cron uses /usr/local/bin/php8.4, use that same executable for init/test. Copy the full commands from the generated guide.
5. Initialize and test Telegram delivery, then configure cron every five minutes. Uploading files without cron is incomplete.
6. Configure the receiver HTTPS URL and shared secret in Assistant. Keep the secret out of URLs and messages.

CONTROLLED TEST
Verify heartbeat reception. Stop sending temporarily on a test server and wait for an external alert. Resume and verify recovery. Do not disconnect a production data center for testing.

CRON EXIT CODE 255
Check PHP version, full worker path, config syntax and state-directory permissions. Run the exact cron command manually and retain the error text without secrets.

## Permissions, passwords, personal data and safety

SECTION PERMISSIONS
The principal administrator assigns each section’s access. Read allows viewing; write allows changes. A missing navigation item usually means no access. Dashboard context menus do not grant additional permissions.

PASSWORDS
Use one record per secret. Link it to both a device and service without duplicating the password. Viewing/copying retains normal vault authorization. Double-click opens the record; reveal/copy are explicit actions. Never place secrets in device names, URLs, event notes or launch commands.

EMPLOYEES AND ASSETS
Personal data requires additional confirmation. Acts preserve information as it existed when issued; later equipment edits must not rewrite old acts. Download the relevant Excel template before importing. Verify units, required fields and links rather than repeatedly importing the same list to fix individual rows.

SESSION
Remembered login is limited to the defined session. Reauthentication after connection loss, access changes or expiry protects access; it does not disable vault protection. Sign out before switching users.

DIAGNOSTICS
Remove passwords, tokens, keys and personal data before sharing logs. Preserve timestamps, host, source, event ID and error text. Do not publish a complete production database as an ordinary diagnostic file.

## Troubleshooting: a practical checklist

NO SERVER CONNECTION
Check the URL/port and server service, then TCP reachability, trusted networks, firewall, certificate and credentials in that order. Ping/TCP do not replace HTTPS validation. After an upgrade retain installer logs and exact client/server versions.

STALE AGENT DATA
Check last contact, local service and updater status. Compare the server template and installed agent versions. A successful update task with no downloaded package is not a new installation. Preserve bootstrap and enrollment keys.

MAIL EXISTS BUT NO RESULT
Check mailbox, actual IMAP timestamp, unrecognized messages, linking and dictionary as separate stages. Inspect the original: Error with a zero counter need not indicate failure. Reanalyze after correcting rules.

RED AGAIN AFTER RESOLVED
Identify the exact active incident. It may be a repeat failure, another issue on that device, or missing fresh recovery evidence. Do not erase history to obtain green. Ignore and Resolved serve different purposes.

MISSING TELEGRAM
Test delivery, then check chat ID, bot permissions, category/severity filters, delay and outbound server access. Pinning needs its own permission.

INCORRECT INVOICE
Compare the original and correct fields before accepting. PNG/scanned OCR may misread characters; totals, invoice numbers and bank details need human verification.

SUPPORT REQUEST
Include product/agent version, OS, timestamp/time zone, preceding action, exact error, expected result and sanitized logs. Never include passwords, tokens or private keys. Contact: support@321.su.

## Setup, shortcuts and server management

CLIENT SETUP
Choose language and accept the agreement. Start menu and desktop shortcuts are enabled by default. Autostart is off; enabling it adds a shortcut to the common Startup folder, applying to all users at sign-in. Choices are retained for later setup runs. Uninstall removes these shortcuts while preserving personal preferences and server data.

SERVER SETUP
For a new installation select directories, network and database. Choose management shortcuts and whether to open management afterwards. Use the separate upgrade wizard for an existing service; it detects current paths/enrollment. The service starts automatically regardless of shortcuts or an open management window.

MANAGEMENT WINDOW
The SysAdmin Assistant Server shortcut opens local management with Windows administrator approval. It displays service state, uptime, version, HTTPS URL, database availability, trusted networks, certificate fingerprint and expiry. Refresh status reads current data. Database availability is checked separately: a running service alone does not prove database health.

SERVICE CONTROL
Start, Stop and Restart affect only the Assistant service. Monitoring stops while the service is stopped. Concurrent control is blocked during active maintenance. Closing management does not stop the service.

DATABASE BACKUP
For SQLite choose a new .db filename. A consistent online copy is produced through the SQLite Backup API, not a direct copy of the live file. Existing files are not overwritten.
For SQL Server a COPY_ONLY backup with CHECKSUM is created in SQL Server’s default backup directory. This is a path on the SQL host, which may be a different computer. The completed operation shows the full path; transfer the .bak to separate storage independently. The SQL account needs backup permissions. A database backup does not include server configuration or certificates; preserve them separately in protected storage.

DIAGNOSTICS AND SETTINGS
The diagnostic ZIP contains only allowlisted health/service information. It excludes databases, secret configuration, keys, mail and raw journal text. Open the data/log directory for local investigation. Change network and notification settings through the client as principal administrator. Never delete a production database to repair a startup problem.


Mail sources are shared by all devices. Go to Settings → Mail and reports → Mail sources to create an event rule, test a sample or import/export JSON. Use the unrecognized mail queue to create a source from a selected message and recheck that message only.

In Kaspersky, select Recent KSC events to open the event list in a separate window. Double-click an event to see its original description and other available details.
