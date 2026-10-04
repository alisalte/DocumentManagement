# Scanner web app: Expo export, then nginx with HTTPS so phones can use the camera.
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
ENV EXPO_NO_TELEMETRY=1
ENV EXPO_PUBLIC_API_URL=/api/v1
RUN npx expo export --platform web --output-dir dist

FROM nginx:alpine AS runtime
# Non-root nginx cannot open the image's /var/log/nginx/*.log symlinks to /dev/stderr
# (open uses O_CREAT and fails with EACCES). Use real files, and send errors to stderr.
RUN apk add --no-cache openssl su-exec \
 && mkdir -p /var/cache/nginx /tmp/nginx /etc/nginx/certs /var/log/nginx \
 && rm -f /var/log/nginx/access.log /var/log/nginx/error.log \
 && touch /var/log/nginx/access.log /var/log/nginx/error.log \
 && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /tmp/nginx /etc/nginx/certs /var/log/nginx \
 && sed -i 's@pid\s\+.*\;@pid /tmp/nginx/nginx.pid;@' /etc/nginx/nginx.conf \
 && sed -i 's#error_log.*#error_log stderr notice;#' /etc/nginx/nginx.conf \
 && sed -i '/^user /d' /etc/nginx/nginx.conf
COPY --from=build --chown=nginx:nginx /app/dist /usr/share/nginx/html
COPY --from=nginxconf scanner.conf /etc/nginx/conf.d/default.conf
COPY --from=deployfiles --chmod=755 scanner-entrypoint.sh /scanner-entrypoint.sh
# Entrypoint runs as root once to mint/refresh the cert, then drops to nginx.
EXPOSE 8080 8443
ENTRYPOINT ["/scanner-entrypoint.sh"]
