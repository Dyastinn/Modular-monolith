#!/bin/sh
# Every night at BACKUP_TIME (local, from TZ): one pg_dump file in /backups, keeping KEEP_DAYS days of them.
# /backups is a folder on the host. Copy it off this machine too: a backup that dies with the server is no backup.
set -e
while true; do
    target=$(date -d "today ${BACKUP_TIME:-01:00}" +%s)
    [ "$target" -le "$(date +%s)" ] && target=$(date -d "tomorrow ${BACKUP_TIME:-01:00}" +%s)
    sleep $((target - $(date +%s)))

    file="/backups/erp-$(date +%F).dump"
    if pg_dump --host db --username erp_owner --dbname erp --format=custom --file "$file.partial"; then
        mv "$file.partial" "$file"
        echo "$(date) backup written: $file"
    else
        rm -f "$file.partial"
        echo "$(date) BACKUP FAILED" >&2
    fi
    find /backups -name 'erp-*.dump' -mtime +"${KEEP_DAYS:-14}" -delete
done
