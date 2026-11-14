# Scanner web app: Expo export, then nginx with HTTPS so phones can use the camera.
# NODE_IMAGE / NGINX_IMAGE: point these at a registry mirror when Docker Hub is unreachable.
ARG NODE_IMAGE=node:22-alpine
ARG NGINX_IMAGE=nginx:alpine
FROM ${NODE_IMAGE} AS build
WORKDIR /app

# NPM_REGISTRY: an npm mirror when registry.npmjs.org is unreachable (empty keeps the default).
ARG NPM_REGISTRY=
COPY package.json package-lock.json ./
RUN if [ -n "$NPM_REGISTRY" ]; then npm config set registry "$NPM_REGISTRY"; fi \
 && npm ci --no-audit --no-fund

COPY . .
ENV EXPO_NO_TELEMETRY=1
ENV EXPO_PUBLIC_API_URL=/api/v1
RUN npx expo export --platform web --output-dir dist

FROM ${NGINX_IMAGE} AS runtime
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
