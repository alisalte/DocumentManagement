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

# Skia (page images, watermarks) needs fontconfig and a font with Latin and Persian glyphs;
# PDFium ships inside the PDFtoImage package. Npgsql probes for the Kerberos library at startup
# and logs an error without it, although GSS is never used.
# Tesseract fas+eng + poppler: local OCR when Tika is not configured (worker role).
# archive.ubuntu.com on port 80 often resets (the "Connection failed [IP: … 80]" build error).
# Rewrite the Ubuntu sources to HTTPS and, unless APT_MIRROR is set, try the next mirror.
ARG APT_MIRROR=
RUN set -eu; \
    printf 'Acquire::Retries "1";\nAcquire::http::Timeout "20";\nAcquire::https::Timeout "20";\n' > /etc/apt/apt.conf.d/80-retries; \
    if [ -f /etc/apt/sources.list.d/ubuntu.sources ]; then \
      src=/etc/apt/sources.list.d/ubuntu.sources; \
    else \
      src=/etc/apt/sources.list; \
    fi; \
    cp "$src" /tmp/apt-sources.orig; \
    if [ -n "$APT_MIRROR" ]; then \
      mirrors="$APT_MIRROR"; \
    else \
      mirrors="https://archive.ubuntu.com/ubuntu https://mirror.arvancloud.ir/ubuntu https://mirrors.edge.kernel.org/ubuntu"; \
    fi; \
    ok=0; \
    for mirror in $mirrors; do \
      sed \
        -e "s|http://archive.ubuntu.com/ubuntu|${mirror}|g" \
        -e "s|http://security.ubuntu.com/ubuntu|${mirror}|g" \
        -e "s|https://archive.ubuntu.com/ubuntu|${mirror}|g" \
        -e "s|https://security.ubuntu.com/ubuntu|${mirror}|g" \
        /tmp/apt-sources.orig > "$src"; \
      echo "apt: trying ${mirror}"; \
      if apt-get update; then ok=1; break; fi; \
      echo "apt: ${mirror} failed"; \
    done; \
    test "$ok" = 1; \
    apt-get install -y --no-install-recommends \
      libfontconfig1 fonts-dejavu-core libgssapi-krb5-2 \
      tesseract-ocr tesseract-ocr-fas tesseract-ocr-eng \
      poppler-utils \
    && rm -rf /var/lib/apt/lists/* /tmp/apt-sources.orig

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
