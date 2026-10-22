# syntax=docker/dockerfile:1
#
# Imagen del servicio generado. Los cuatro parametros del generador
# (-n/--name, --base-path, --api-name, --permission-prefix) ya quedaron
# sustituidos en el contenido antes de construir esta imagen: nada de lo
# que aparece aqui se completa con `docker build --build-arg` (DD6). Las
# credenciales del feed (usuario/contrasena, item 1.5 -- la empresa no usa
# PAT por su vencimiento anual en muchos proyectos) usan BuildKit secrets y
# se resuelven via los placeholders %FEED_USERNAME%/%FEED_PASSWORD% de
# NuGet.Config (packageSourceCredentials); no hace falta el credential
# provider de Azure Artifacts para auth basica.

ARG BUILD_CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION

# Certificado CA corporativo opcional (item 1.5): solo hace falta cuando la
# red donde se construye intercepta TLS (p. ej. Netskope en desarrollo local)
# y por eso rompe la conexion a pkgs.dev.azure.com. No es secreto en si (es
# un certificado raiz, publico por diseno): si no se pasa
# --secret id=corp-ca-cert,src=<ruta-local>, este paso no monta nada y el
# build sigue igual que antes.
RUN --mount=type=secret,id=corp-ca-cert,target=/usr/local/share/ca-certificates/corp-ca.crt \
    if [ -s /usr/local/share/ca-certificates/corp-ca.crt ]; then update-ca-certificates; fi

WORKDIR /src
COPY ["NuGet.Config", "./"]
COPY ["src/", "src/"]

RUN --mount=type=secret,id=feed-username,env=FEED_USERNAME,required=true \
    --mount=type=secret,id=feed-password,env=FEED_PASSWORD,required=true \
    test -n "${FEED_USERNAME}" && test -n "${FEED_PASSWORD}" \
    && dotnet restore "src/Reyma.VectorSpace.Engine.Microservice.Api/Reyma.VectorSpace.Engine.Microservice.Api.csproj"
RUN dotnet publish "src/Reyma.VectorSpace.Engine.Microservice.Api/Reyma.VectorSpace.Engine.Microservice.Api.csproj" \
    -c "${BUILD_CONFIGURATION}" \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Zona horaria del servicio (PLAN SS12) y entorno estandar que ASP.NET
# reconoce como productivo (H5: "Release" no activa IsProduction()).
ENV TZ=America/Mexico_City \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_BASEPATH=/reyma/vectorspace/engine

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080

# Puerto 8080 (no privilegiado): no hace falta root ni setcap (DD10).
USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Reyma.VectorSpace.Engine.Microservice.Api.dll"]
