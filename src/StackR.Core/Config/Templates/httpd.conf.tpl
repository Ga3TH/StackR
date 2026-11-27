# Généré par StackR - ne pas modifier à la main.
Define SRVROOT "{{ApacheDir}}"
ServerRoot "{{ApacheDir}}"

Listen 127.0.0.1:{{HttpPort}}
{{HttpsListen}}

ServerName localhost
ServerTokens Prod
ServerSignature Off
TraceEnable Off

LoadModule authz_core_module modules/mod_authz_core.so
LoadModule authz_host_module modules/mod_authz_host.so
LoadModule dir_module modules/mod_dir.so
LoadModule mime_module modules/mod_mime.so
LoadModule log_config_module modules/mod_log_config.so
LoadModule rewrite_module modules/mod_rewrite.so
LoadModule headers_module modules/mod_headers.so
LoadModule setenvif_module modules/mod_setenvif.so
LoadModule alias_module modules/mod_alias.so
LoadModule env_module modules/mod_env.so
{{SslModules}}

LoadModule php_module "{{PhpDir}}/php8apache2_4.dll"
PHPIniDir "{{PhpIniDir}}"
AddHandler application/x-httpd-php .php
DirectoryIndex index.php index.html

ErrorLog "{{LogsDir}}/apache-error.log"
LogLevel warn
<IfModule log_config_module>
    LogFormat "%h %l %u %t \"%r\" %>s %b" common
    CustomLog "{{LogsDir}}/apache-access.log" common
</IfModule>

TypesConfig conf/mime.types

<Directory />
    AllowOverride None
    Require all denied
    Options -Indexes
</Directory>

DocumentRoot "{{WwwDir}}"
<Directory "{{WwwDir}}">
    Options Indexes FollowSymLinks
    AllowOverride All
    Require local
</Directory>

{{AdminerConf}}

{{StackRSqlConf}}

IncludeOptional "{{EtcDir}}/sites/*.conf"
