# Release APK for the phone scanner. The image build downloads the Android SDK, then a
# tiny runtime stage copies the apk onto the shared volume nginx serves.
#
# Needs outbound access to dl.google.com, services.gradle.org, repo.maven.apache.org
# and registry.npmjs.org. The first build is large.
FROM eclipse-temurin:21-jdk-noble AS build

ARG EXPO_PUBLIC_API_URL=
ARG EXPO_PUBLIC_ALLOW_HTTP_API=1
ARG NODE_VERSION=22.20.0
ARG ANDROID_CMDLINE_TOOLS=13114758

ENV EXPO_PUBLIC_API_URL=$EXPO_PUBLIC_API_URL \
    EXPO_PUBLIC_ALLOW_HTTP_API=$EXPO_PUBLIC_ALLOW_HTTP_API \
    EXPO_NO_TELEMETRY=1 \
    CI=1 \
    ANDROID_SDK_ROOT=/opt/android-sdk \
    ANDROID_HOME=/opt/android-sdk \
    GRADLE_OPTS="-Dorg.gradle.jvmargs=-Xmx3g -XX:MaxMetaspaceSize=512m -Dfile.encoding=UTF-8" \
    NODE_OPTIONS="--max-old-space-size=4096" \
    DEBIAN_FRONTEND=noninteractive

# archive.ubuntu.com on port 80 often resets. Prefer HTTPS, then two public mirrors.
RUN set -eu; \
    printf 'Acquire::Retries "1";\nAcquire::http::Timeout "20";\nAcquire::https::Timeout "20";\n' > /etc/apt/apt.conf.d/80-retries; \
    src=/etc/apt/sources.list.d/ubuntu.sources; \
    cp "$src" /tmp/apt-sources.orig; \
    ok=0; \
    for mirror in https://archive.ubuntu.com/ubuntu https://mirror.arvancloud.ir/ubuntu https://mirrors.edge.kernel.org/ubuntu; do \
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
    apt-get install -y --no-install-recommends ca-certificates curl unzip git python3 \
    && rm -rf /var/lib/apt/lists/* /tmp/apt-sources.orig

RUN curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.gz" \
    | tar -xz -C /usr/local --strip-components=1

RUN mkdir -p "${ANDROID_SDK_ROOT}/cmdline-tools" \
 && curl -fsSL -o /tmp/cmdtools.zip "https://dl.google.com/android/repository/commandlinetools-linux-${ANDROID_CMDLINE_TOOLS}_latest.zip" \
 && unzip -q /tmp/cmdtools.zip -d "${ANDROID_SDK_ROOT}/cmdline-tools" \
 && mv "${ANDROID_SDK_ROOT}/cmdline-tools/cmdline-tools" "${ANDROID_SDK_ROOT}/cmdline-tools/latest" \
 && rm /tmp/cmdtools.zip
ENV PATH="${PATH}:${ANDROID_SDK_ROOT}/cmdline-tools/latest/bin:${ANDROID_SDK_ROOT}/platform-tools"

RUN yes | sdkmanager --licenses >/dev/null \
 && sdkmanager --install \
      "platforms;android-36" \
      "build-tools;36.0.0" \
      "platform-tools" \
      "ndk;27.1.12297006" \
      "cmake;3.22.1"

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npx expo prebuild --platform android --no-install --clean \
 && cd android \
 && chmod +x ./gradlew \
 && ./gradlew assembleRelease --no-daemon -PreactNativeArchitectures=arm64-v8a,armeabi-v7a \
 && mkdir -p /dist \
 && cp app/build/outputs/apk/release/app-release.apk /dist/dms-scanner.apk

FROM alpine:3.21
RUN mkdir -p /apk /dist
COPY --from=build /dist/dms-scanner.apk /dist/dms-scanner.apk
CMD ["cp", "-f", "/dist/dms-scanner.apk", "/apk/dms-scanner.apk"]
