# The web frontend: built with Node, served by nginx, which also proxies /api to the API
# container. Same origin for the browser, so no CORS and no API URL baked into the bundle.
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
# Empty base URL: the bundle calls /api/... on whatever host serves it.
ENV VITE_API_BASE_URL=""
RUN npm run build

FROM ${NGINX_IMAGE} AS runtime
# Non-root: listen on 8080 (already in default.conf), writable pid/cache for the nginx user.
RUN mkdir -p /var/cache/nginx /tmp/nginx \
 && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /tmp/nginx \
 && sed -i 's@pid\s\+.*\;@pid /tmp/nginx/nginx.pid;@' /etc/nginx/nginx.conf \
 && sed -i '/^user /d' /etc/nginx/nginx.conf
COPY --from=build --chown=nginx:nginx /app/dist /usr/share/nginx/html
COPY --from=nginxconf --chown=nginx:nginx default.conf /etc/nginx/conf.d/default.conf
USER nginx
EXPOSE 8080
