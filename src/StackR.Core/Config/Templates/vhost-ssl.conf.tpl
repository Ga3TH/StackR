# Site {{ServerName}} (HTTPS) - généré par StackR.
<VirtualHost 127.0.0.1:{{HttpsPort}}>
    ServerName {{ServerName}}
    DocumentRoot "{{DocumentRoot}}"
    SSLEngine on
    SSLCertificateFile "{{CertFile}}"
    SSLCertificateKeyFile "{{KeyFile}}"
    <Directory "{{DocumentRoot}}">
        Options -Indexes +FollowSymLinks
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
