; Genere par StackR - ne pas modifier a la main (fichier en page de code ANSI : PHP charge ses extensions en ANSI).
[PHP]
expose_php = Off
display_errors = On
error_reporting = E_ALL
allow_url_include = Off
memory_limit = 512M
upload_max_filesize = 64M
post_max_size = 64M
date.timezone = UTC

extension_dir = "{{PhpDir}}/ext"
extension=pdo_mysql
extension=mysqli
extension=mbstring
extension=openssl
extension=intl
extension=curl
extension=zip
extension=gd
extension=fileinfo
extension=sodium

{{CaBundleLines}}

[Session]
session.cookie_httponly = 1
session.use_strict_mode = 1

[mail function]
SMTP = 127.0.0.1
smtp_port = {{MailpitSmtpPort}}
; Expediteur par defaut : sous Windows, mail() echoue sans lui quand le script ne fournit pas d en-tete From.
sendmail_from = "stackr@localhost.test"
