# Release APK for the phone scanner. The image build downloads the Android SDK, then a
# tiny runtime stage copies the apk onto the shared volume nginx serves.
#
# SDK zips are taken from dl.google.com, then from mirrors.cloud.tencent.com when that
# host answers 404. Gradle also tries the Aliyun Maven mirror before Google's Maven.
# The first build is large (the NDK alone is about 700 MB).
FROM eclipse-temurin:21-jdk-noble AS build

ARG EXPO_PUBLIC_API_URL=
ARG EXPO_PUBLIC_ALLOW_HTTP_API=1
ARG NODE_VERSION=22.20.0

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

# sdkmanager always downloads from dl.google.com, which returns 404 on some networks.
# Fetch the same packages directly and fall through to a mirror.
ENV PATH="${PATH}:${ANDROID_SDK_ROOT}/platform-tools"
RUN set -eu; \
    fetch() { \
      name="$1"; dest="$2"; \
      for base in https://dl.google.com/android/repository https://mirrors.cloud.tencent.com/AndroidSDK; do \
        echo "sdk: ${base}/${name}"; \
        if curl -fL --retry 2 --retry-delay 2 --connect-timeout 20 -o "$dest" "${base}/${name}"; then \
          return 0; \
        fi; \
        echo "sdk: failed ${base}/${name}"; \
        rm -f "$dest"; \
      done; \
      return 1; \
    }; \
    mkdir -p "${ANDROID_SDK_ROOT}/platforms" "${ANDROID_SDK_ROOT}/build-tools" "${ANDROID_SDK_ROOT}/ndk" "${ANDROID_SDK_ROOT}/licenses"; \
    fetch platform-36_r02.zip /tmp/platform.zip; \
    unzip -q /tmp/platform.zip -d "${ANDROID_SDK_ROOT}/platforms"; \
    rm /tmp/platform.zip; \
    fetch build-tools_r36_linux.zip /tmp/build-tools.zip; \
    unzip -q /tmp/build-tools.zip -d /tmp/build-tools; \
    mv "$(find /tmp/build-tools -mindepth 1 -maxdepth 1 -type d)" "${ANDROID_SDK_ROOT}/build-tools/36.0.0"; \
    grep -q '36.0.0' "${ANDROID_SDK_ROOT}/build-tools/36.0.0/source.properties"; \
    rm -rf /tmp/build-tools /tmp/build-tools.zip; \
    fetch cmake-3.22.1-linux.zip /tmp/cmake.zip; \
    mkdir -p "${ANDROID_SDK_ROOT}/cmake/3.22.1"; \
    unzip -q /tmp/cmake.zip -d "${ANDROID_SDK_ROOT}/cmake/3.22.1"; \
    printf 'Pkg.Revision=3.22.1\n' > "${ANDROID_SDK_ROOT}/cmake/3.22.1/source.properties"; \
    rm /tmp/cmake.zip; \
    fetch platform-tools_r37.0.1-linux.zip /tmp/platform-tools.zip; \
    unzip -q /tmp/platform-tools.zip -d "${ANDROID_SDK_ROOT}"; \
    rm /tmp/platform-tools.zip; \
    fetch android-ndk-r27b-linux.zip /tmp/ndk.zip; \
    unzip -q /tmp/ndk.zip -d /tmp/ndk; \
    mv "$(find /tmp/ndk -mindepth 1 -maxdepth 1 -type d)" "${ANDROID_SDK_ROOT}/ndk/27.1.12297006"; \
    test -f "${ANDROID_SDK_ROOT}/ndk/27.1.12297006/source.properties"; \
    rm -rf /tmp/ndk /tmp/ndk.zip; \
    printf '%s\n' 24333f8a63b6825ea9c5514f83c2829b004d1fee d56f5187479451eabf01fb78af6dfcb131a6481e \
      > "${ANDROID_SDK_ROOT}/licenses/android-sdk-license"

# The React Native Gradle plugin compiles with jvmToolchain(17). On JDK 21 it
# downloads a JDK from api.foojay.io, which is what stalls and then fails the build.
# CMake 3.30.5 is the version that plugin expects; 3.22.1 alone makes it call sdkmanager.
RUN set -eu; \
    apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jdk-headless \
 && rm -rf /var/lib/apt/lists/* \
 && arch="$(dpkg --print-architecture)" \
 && ln -sfn "/usr/lib/jvm/java-17-openjdk-${arch}" /usr/lib/jvm/java-17; \
    fetch() { \
      name="$1"; dest="$2"; \
      for base in https://dl.google.com/android/repository https://mirrors.cloud.tencent.com/AndroidSDK; do \
        echo "sdk: ${base}/${name}"; \
        if curl -fL --retry 2 --retry-delay 2 --connect-timeout 20 -o "$dest" "${base}/${name}"; then \
          return 0; \
        fi; \
        echo "sdk: failed ${base}/${name}"; \
        rm -f "$dest"; \
      done; \
      return 1; \
    }; \
    ver=3.30.5; \
    fetch "cmake-${ver}-linux.zip" /tmp/cmake.zip; \
    mkdir -p "${ANDROID_SDK_ROOT}/cmake/${ver}"; \
    unzip -q /tmp/cmake.zip -d "${ANDROID_SDK_ROOT}/cmake/${ver}"; \
    chmod -R a+rx "${ANDROID_SDK_ROOT}/cmake/${ver}/bin" "${ANDROID_SDK_ROOT}/ndk/27.1.12297006" "${ANDROID_SDK_ROOT}/build-tools/36.0.0" "${ANDROID_SDK_ROOT}/platform-tools"; \
    printf 'Pkg.Revision=%s\nPkg.Path=cmake;%s\nPkg.Desc=CMake %s\n' "$ver" "$ver" "$ver" \
      > "${ANDROID_SDK_ROOT}/cmake/${ver}/source.properties"; \
    rm /tmp/cmake.zip
ENV JAVA_HOME=/usr/lib/jvm/java-17 \
    PATH="/usr/lib/jvm/java-17/bin:${PATH}"

# Do not let Gradle spend a quarter hour downloading a JDK that is already installed.
RUN mkdir -p /root/.gradle \
 && printf '%s\n' \
      'org.gradle.java.installations.auto-download=false' \
      'systemProp.org.gradle.internal.http.connectionTimeout=20000' \
      'systemProp.org.gradle.internal.http.socketTimeout=60000' \
      > /root/.gradle/gradle.properties

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npx expo prebuild --platform android --no-install --clean \
 && sed -i '/foojay-resolver-convention/d' node_modules/@react-native/gradle-plugin/settings.gradle.kts \
 && find android node_modules/@react-native/gradle-plugin -name '*.gradle' -print0 \
    | xargs -0 sed -i \
      -e "s|google()|maven { url 'https://maven.aliyun.com/repository/google' }; maven { url 'https://maven.aliyun.com/repository/public' }; google()|g" \
      -e "s|mavenCentral()|maven { url 'https://maven.aliyun.com/repository/public' }; mavenCentral()|g" \
      -e "s|gradlePluginPortal()|maven { url 'https://maven.aliyun.com/repository/gradle-plugin' }; gradlePluginPortal()|g" \
 && find node_modules/@react-native/gradle-plugin -name '*.gradle.kts' -print0 \
    | xargs -0 sed -i \
      -e 's|google()|maven { url = uri("https://maven.aliyun.com/repository/google") }; maven { url = uri("https://maven.aliyun.com/repository/public") }; google()|g' \
      -e 's|mavenCentral()|maven { url = uri("https://maven.aliyun.com/repository/public") }; mavenCentral()|g' \
      -e 's|gradlePluginPortal()|maven { url = uri("https://maven.aliyun.com/repository/gradle-plugin") }; gradlePluginPortal()|g' \
 && cd android \
 && chmod +x ./gradlew \
 && ./gradlew assembleRelease --no-daemon --stacktrace -PreactNativeArchitectures=arm64-v8a,armeabi-v7a \
 && mkdir -p /dist \
 && cp app/build/outputs/apk/release/app-release.apk /dist/dms-scanner.apk

FROM alpine:3.21
RUN mkdir -p /apk /dist
COPY --from=build /dist/dms-scanner.apk /dist/dms-scanner.apk
CMD ["cp", "-f", "/dist/dms-scanner.apk", "/apk/dms-scanner.apk"]
