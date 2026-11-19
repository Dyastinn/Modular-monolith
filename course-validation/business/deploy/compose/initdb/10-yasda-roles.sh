#!/bin/sh
# Runs once, the first time the database volume is empty: the same create-roles.sql as any other install.
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
     -v db=yasda -v owner_password="$YASDA_OWNER_PASSWORD" -v app_password="$YASDA_APP_PASSWORD" \
     -f /yasda/create-roles.sql
