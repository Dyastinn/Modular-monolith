#!/bin/sh
# Brings every module's schema up to date, as yasda_owner, then exits. Safe to run on every deploy: each bundle
# applies only the migrations that database hasn't had yet.
set -e
for module in Identity Tickets Sla; do
    echo "== $module"
    "/migrations/migrate-$module" --connection "$YASDA_OWNER_CONNECTION"
done
echo "All schemas are up to date."
