#!/bin/sh
# Runs once, the first time the database volume is empty: the same create-roles.sql as any other install.
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
     -v db=erp -v owner_password="$ERP_OWNER_PASSWORD" -v app_password="$ERP_APP_PASSWORD" \
     -f /erp/create-roles.sql
