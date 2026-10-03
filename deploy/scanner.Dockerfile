# Scanner web app: Expo export, then nginx. The bundle calls /api/v1 on the same host,
# and nginx proxies that to the API container.
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
ENV EXPO_NO_TELEMETRY=1
ENV EXPO_PUBLIC_API_URL=/api/v1
RUN npx expo export --platform web --output-dir dist

FROM nginx:alpine AS runtime
RUN mkdir -p /var/cache/nginx /tmp/nginx \
 && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /tmp/nginx \
 && sed -i 's@pid\s\+.*\;@pid /tmp/nginx/nginx.pid;@' /etc/nginx/nginx.conf \
 && sed -i '/^user /d' /etc/nginx/nginx.conf
COPY --from=build --chown=nginx:nginx /app/dist /usr/share/nginx/html
COPY --from=nginxconf --chown=nginx:nginx scanner.conf /etc/nginx/conf.d/default.conf
USER nginx
EXPOSE 8080
