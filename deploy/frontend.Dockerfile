# The web frontend: built with Node, served by nginx, which also proxies /api to the API
# container. Same origin for the browser, so no CORS and no API URL baked into the bundle.
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
# Empty base URL: the bundle calls /api/... on whatever host serves it.
ENV VITE_API_BASE_URL=""
RUN npm run build

FROM nginx:alpine AS runtime
# Non-root: listen on 8080 (already in default.conf), writable pid/cache for the nginx user.
RUN mkdir -p /var/cache/nginx /tmp/nginx \
 && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /tmp/nginx \
 && sed -i 's@pid\s\+.*\;@pid /tmp/nginx/nginx.pid;@' /etc/nginx/nginx.conf \
 && sed -i '/^user /d' /etc/nginx/nginx.conf
COPY --from=build --chown=nginx:nginx /app/dist /usr/share/nginx/html
COPY --from=nginxconf --chown=nginx:nginx default.conf /etc/nginx/conf.d/default.conf
USER nginx
EXPOSE 8080
