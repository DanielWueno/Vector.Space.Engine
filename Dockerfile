# syntax=docker/dockerfile:1
#
# Imagen del servicio generado. Los cuatro parametros del generador
# (-n/--name, --base-path, --api-name, --permission-prefix) ya quedaron
# sustituidos en el contenido antes de construir esta imagen: nada de lo
# que aparece aqui se completa con `docker build --build-arg` salvo la
# URL del feed y la configuracion de build (DD6). El token usa BuildKit.

ARG BUILD_CONFIGURATION=Release
ARG CREDPROVIDER_VERSION=v1.4.0

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION
ARG CREDPROVIDER_VERSION
ARG FEED_URL

# Credential provider con version fijada (no "master", H14) para resolver
# el feed Gr-Api declarado en NuGet.Config durante la restauracion.
ENV AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_VERSION=${CREDPROVIDER_VERSION}
RUN curl -sSL https://aka.ms/install-artifacts-credprovider.sh | bash

WORKDIR /src
COPY ["NuGet.Config", "./"]
COPY ["src/", "src/"]

RUN --mount=type=secret,id=feed-token,env=FEED_ACCESSTOKEN,required=true \
    test -n "${FEED_URL}" && test -n "${FEED_ACCESSTOKEN}" \
    && VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\":[{\"endpoint\":\"${FEED_URL}\",\"password\":\"${FEED_ACCESSTOKEN}\"}]}" \
       NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED=false \
       NUGET_CREDENTIALPROVIDER_MSAL_FILECACHE_ENABLED=false \
       dotnet restore "src/Reyma.VectorSpace.Engine.Microservice.Api/Reyma.VectorSpace.Engine.Microservice.Api.csproj"
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
