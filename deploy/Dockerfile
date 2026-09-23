# Build once, ship both entrypoints: the API host and the migrator run from the same image, so a
# deployment can never apply migrations from a different build than the one it runs.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files, so the layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props DocumentManagement.slnx ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore DocumentManagement.slnx

RUN dotnet publish src/Dms.Host/Dms.Host.csproj -c Release -o /app/api --no-restore \
 && dotnet publish src/Dms.Migrator/Dms.Migrator.csproj -c Release -o /app/migrator --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Never run as root. The object directory is created here, owned by dms, so a fresh named volume
# mounted on it inherits that ownership.
RUN useradd --uid 64198 --create-home --shell /usr/sbin/nologin dms \
 && mkdir -p /var/lib/dms/objects && chown dms:dms /var/lib/dms/objects
COPY --from=build --chown=dms:dms /app/ /app/
USER dms

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8080

ENTRYPOINT ["dotnet", "/app/api/Dms.Host.dll"]
