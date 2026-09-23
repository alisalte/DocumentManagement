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
COPY --from=build /app/dist /usr/share/nginx/html
COPY --from=nginxconf default.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
