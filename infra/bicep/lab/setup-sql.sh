#!/bin/bash
# First-boot setup for a lab VM (cloud-init runs this once): installs SQL Server 2022 Developer Edition from
# Microsoft's apt repository, sets a generated SA password, creates the worker's SQL login, and disables sa.
# Placeholders are replaced by the Bicep template from secure parameters; output never echoes passwords.
set -euo pipefail
exec >/var/log/sqllab-setup.log 2>&1
export DEBIAN_FRONTEND=noninteractive

SA_PASSWORD='__SA_PASSWORD__'
LAB_LOGIN='__SQL_LOGIN__'
LAB_PASSWORD='__SQL_PASSWORD__'

curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list -o /etc/apt/sources.list.d/mssql-server-2022.list
curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/prod.list -o /etc/apt/sources.list.d/mssql-release.list

for _ in 1 2 3; do
  if apt-get update; then break; fi
  sleep 20
done
apt-get install -y mssql-server
ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev

MSSQL_SA_PASSWORD="$SA_PASSWORD" MSSQL_PID=Developer ACCEPT_EULA=Y /opt/mssql/bin/mssql-conf -n setup
systemctl enable --now mssql-server

sql() { /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SA_PASSWORD" -Q "$1"; }
for _ in $(seq 1 60); do
  if sql "SELECT 1" >/dev/null; then break; fi
  sleep 5
done

sql "IF SUSER_ID(N'$LAB_LOGIN') IS NULL CREATE LOGIN [$LAB_LOGIN] WITH PASSWORD = N'$LAB_PASSWORD', CHECK_POLICY = ON;
     ALTER SERVER ROLE sysadmin ADD MEMBER [$LAB_LOGIN];"
# Normal operations never use sa.
/opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U "$LAB_LOGIN" -P "$LAB_PASSWORD" -Q "ALTER LOGIN sa DISABLE;"
echo "sqllab setup complete"
