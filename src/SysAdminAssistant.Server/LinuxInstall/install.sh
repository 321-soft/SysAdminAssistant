#!/bin/bash
set -euo pipefail
export PATH=/usr/sbin:/usr/bin:/sbin:/bin
[[ $(id -u) -eq 0 ]] || { echo 'Нужны права администратора.' >&2; exit 1; }
[[ -d /run/systemd/system ]] && command -v systemctl >/dev/null || { echo 'Нужна система с работающим systemd.' >&2; exit 1; }
[[ $(uname -m) == x86_64 ]] || { echo 'Поддерживается только x64.' >&2; exit 1; }
[[ -r /etc/os-release ]] || { echo 'Не удалось определить Linux.' >&2; exit 1; }
. /etc/os-release
case "${ID:-}" in debian|ubuntu) ;; *) echo 'Поддерживаются Debian и Ubuntu.' >&2; exit 1;; esac
bundle="${1:?Не указан пакет}"
[[ -f "$bundle/agent" && -f "$bundle/bootstrap.json" && ! -L "$bundle/agent" && ! -L "$bundle/bootstrap.json" ]] || { echo 'Пакет неполон.' >&2; exit 1; }
[[ ! -e /opt/sysadminassistant-agent && ! -e /var/lib/sysadminassistant-agent && ! -e /etc/systemd/system/sysadminassistant-agent.service && ! -e /etc/systemd/system/sysadminassistant-agent-update.service && ! -e /etc/systemd/system/sysadminassistant-agent-update.timer && ! -e /etc/sysadminassistant-agent ]] || { echo 'Агент уже установлен; обновление этим установщиком не поддерживается.' >&2; exit 1; }
if id saa-agent >/dev/null 2>&1; then echo 'Учётная запись saa-agent уже существует; установка отменена.' >&2; exit 1; fi
# Execute only the verified package's harmless runtime preflight before changing services.
chmod u+x "$bundle/agent"
if ! "$bundle/agent" --preflight; then
    echo 'Проверка нативных зависимостей .NET не прошла; попытка установить пакеты из репозиториев ОС.' >&2
    command -v apt-get >/dev/null || { echo 'apt-get отсутствует; установка отменена до создания службы.' >&2; exit 1; }
    apt-get update
    icu=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -n 1)
    ssl=$(apt-cache search --names-only '^libssl[0-9]+(t64)?$' | awk '{print $1}' | sort -V | tail -n 1)
    [[ "$icu" =~ ^libicu[0-9]+$ && "$ssl" =~ ^libssl[0-9]+(t64)?$ ]] || { echo 'Не найдены подходящие ICU/OpenSSL; установка отменена.' >&2; exit 1; }
    apt-get install -y "$icu" "$ssl" zlib1g libgssapi-krb5-2 ca-certificates
    "$bundle/agent" --preflight || { echo 'Агент не запускается на этой ОС после установки зависимостей; службы не создавались.' >&2; exit 1; }
fi
created_user=0
created_files=0
cleanup(){
    status=$?
    if [[ $status -ne 0 ]]; then
        echo 'Установка не завершена; удаляются только созданные ею файлы.' >&2
        if [[ $created_files -eq 1 ]]; then
            systemctl disable --now sysadminassistant-agent-update.timer sysadminassistant-agent.service >/dev/null 2>&1 || true
            rm -f /etc/systemd/system/sysadminassistant-agent.service /etc/systemd/system/sysadminassistant-agent-update.service /etc/systemd/system/sysadminassistant-agent-update.timer
            rm -rf /opt/sysadminassistant-agent /var/lib/sysadminassistant-agent /etc/sysadminassistant-agent
            systemctl daemon-reload >/dev/null 2>&1 || true
        fi
        if [[ $created_user -eq 1 ]]; then userdel saa-agent >/dev/null 2>&1 || true; fi
    fi
}
trap cleanup EXIT
useradd --system --home-dir /var/lib/sysadminassistant-agent --shell /usr/sbin/nologin saa-agent
created_user=1
if getent group systemd-journal >/dev/null; then usermod -a -G systemd-journal saa-agent; else echo "Группа systemd-journal отсутствует: чтение журнала может быть недоступно." >&2; fi
created_files=1
install -d -o root -g root -m 755 /opt/sysadminassistant-agent
install -o root -g root -m 755 "$bundle/agent" /opt/sysadminassistant-agent/agent
install -d -o saa-agent -g saa-agent -m 700 /var/lib/sysadminassistant-agent
install -o saa-agent -g saa-agent -m 600 "$bundle/bootstrap.json" /var/lib/sysadminassistant-agent/bootstrap.json
install -d -o root -g root -m 700 /etc/sysadminassistant-agent
install -o root -g root -m 600 "$bundle/bootstrap.json" /etc/sysadminassistant-agent/bootstrap.json
cat > /etc/systemd/system/sysadminassistant-agent.service <<'UNIT'
[Unit]
Description=SysAdmin Assistant Linux monitoring agent
After=network-online.target
Wants=network-online.target
[Service]
Type=simple
User=saa-agent
Group=saa-agent
ExecStart=/opt/sysadminassistant-agent/agent --data-dir /var/lib/sysadminassistant-agent
Restart=on-failure
RestartSec=30
NoNewPrivileges=true
PrivateTmp=true
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/sysadminassistant-agent
CapabilityBoundingSet=
RestrictSUIDSGID=true
[Install]
WantedBy=multi-user.target
UNIT
cat > /etc/systemd/system/sysadminassistant-agent-update.service <<'UNIT'
[Unit]
Description=Verify and apply signed SysAdmin Assistant agent updates
After=network-online.target
[Service]
Type=oneshot
User=root
ExecStart=/opt/sysadminassistant-agent/agent --apply-update
NoNewPrivileges=true
PrivateTmp=true
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/sysadminassistant-agent /var/lib/sysadminassistant-agent
UNIT
cat > /etc/systemd/system/sysadminassistant-agent-update.timer <<'UNIT'
[Unit]
Description=Check staged SysAdmin Assistant agent updates
[Timer]
OnBootSec=3min
OnUnitActiveSec=5min
Unit=sysadminassistant-agent-update.service
[Install]
WantedBy=timers.target
UNIT
chmod 644 /etc/systemd/system/sysadminassistant-agent.service /etc/systemd/system/sysadminassistant-agent-update.service /etc/systemd/system/sysadminassistant-agent-update.timer
systemctl daemon-reload
systemctl enable --now sysadminassistant-agent.service
sleep 2
systemctl is-active --quiet sysadminassistant-agent.service
systemctl enable --now sysadminassistant-agent-update.timer
trap - EXIT
echo 'Агент установлен. Дождитесь регистрации и сверьте отпечаток в карточке устройства.'
echo 'Посмотреть отпечаток: sudo cat /var/lib/sysadminassistant-agent/status.json'

# Enrollment runs in the service and never prints bootstrap tokens or the private identity.
for attempt in 1 2 3 4 5; do
    if [[ -r /var/lib/sysadminassistant-agent/status.json ]]; then cat /var/lib/sysadminassistant-agent/status.json; break; fi
    sleep 2
done
if [[ ! -r /var/lib/sysadminassistant-agent/status.json ]]; then echo 'Служба запущена; отчёт регистрации пока не получен. Проверьте журнал службы и HTTPS до сервера.' >&2; fi
