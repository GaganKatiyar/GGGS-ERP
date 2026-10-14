# Build image for the two PeopleOS hosts (ADR-12). The host is chosen at build time:
#   docker build -f infra/docker/Dockerfile --build-arg SERVICE_PATH=backend/Host/Gggs.Api --build-arg SERVICE_DLL=Gggs.Api.dll -t peopleos/api .
#   docker build -f infra/docker/Dockerfile --build-arg SERVICE_PATH=backend/Host/Gggs.Worker --build-arg SERVICE_DLL=Gggs.Worker.dll -t peopleos/worker .
#
# One Dockerfile for both, so a hardening change (non-root user, base image bump, ICU) lands in
# both images at once.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG SERVICE_PATH
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore first, on just the project graph, so a source-only change does not re-download packages.
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY backend/ backend/
RUN dotnet restore "${SERVICE_PATH}/$(basename ${SERVICE_PATH}).csproj"

RUN dotnet publish "${SERVICE_PATH}/$(basename ${SERVICE_PATH}).csproj" \
    --configuration ${BUILD_CONFIGURATION} \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# HR data is localized: dates, names and currency formatting all need real culture data, so the
# invariant-globalization shortcut is not available to us.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

# Runs as the non-root user the base image provides.
USER $APP_UID

COPY --from=build /app/publish .

EXPOSE 8080

# No HEALTHCHECK here on purpose. The runtime image ships no curl or wget, and adding one to probe
# our own endpoint would enlarge the attack surface for no gain: Container Apps and the compose
# files both probe /health/live and /health/ready over HTTP from outside the container.

ARG SERVICE_DLL
ENV SERVICE_DLL=${SERVICE_DLL}
ENTRYPOINT ["sh", "-c", "exec dotnet /app/${SERVICE_DLL}"]
