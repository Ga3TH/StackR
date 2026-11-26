# Genere par StackR - ne pas modifier a la main (fichier en page de code ANSI : MySQL ne lit pas l UTF-8).
[client]
port = {{MySqlPort}}
default-character-set = utf8mb4

[mysqld]
bind-address = 127.0.0.1
port = {{MySqlPort}}
mysqlx = OFF
basedir = "{{MySqlDir}}"
datadir = "{{MySqlData}}"
character-set-server = utf8mb4
collation-server = utf8mb4_unicode_ci
log-error = "{{ErrorLog}}"
