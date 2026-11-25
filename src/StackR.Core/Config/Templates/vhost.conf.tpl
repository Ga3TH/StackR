# Site {{ServerName}} - généré par StackR.
<VirtualHost 127.0.0.1:{{HttpPort}}>
    ServerName {{ServerName}}
    DocumentRoot "{{DocumentRoot}}"
    <Directory "{{DocumentRoot}}">
        Options -Indexes +FollowSymLinks
        AllowOverride All
        Require all granted
    </Directory>
</VirtualHost>
