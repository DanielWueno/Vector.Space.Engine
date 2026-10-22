# certs/

`corp-ca-placeholder.crt` es un archivo vacío versionado a propósito: es el valor por
defecto de `CORP_CA_CERT_PATH` en `docker-compose.api.yml`. Mientras esté vacío, el
`Dockerfile` no instala ningún certificado y el build se comporta igual que sin esta
sección (red sin inspección TLS).

Si tu build local necesita el certificado CA corporativo, exporta `CORP_CA_CERT_PATH`
apuntando a tu copia local del `.crt`/`.pem` **antes de construir**. No se commitea ningún
certificado real en este directorio.

En este entorno es **Netskope** (confirmado en el item 1.5): intercepta TLS hacia
`pkgs.dev.azure.com` y `aka.ms` dentro de contenedores Linux, aunque el host Windows ya
confíe en su certificado (el contenedor no hereda el almacén de Windows). Para exportarlo:

```powershell
Get-ChildItem Cert:\LocalMachine\Root, Cert:\LocalMachine\CA |
  Where-Object { $_.Subject -match "Netskope" -or $_.Issuer -match "Netskope" }
```

Exporta el **root** (`Cert:\LocalMachine\Root`) y el **intermedio** propio de la empresa
(`Cert:\LocalMachine\CA`, emitido por Netskope) como Base-64 X.509 (`.pem`), concaténalos en
un solo archivo, y apunta `CORP_CA_CERT_PATH` ahí. Un certificado raíz es público por diseño;
no es un secreto, pero igual no se commitea aquí para no acoplar este repo a un certificado
que puede rotar.
