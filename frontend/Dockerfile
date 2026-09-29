# syntax=docker/dockerfile:1.7
#   docker build -t fundflow/web ./frontend

FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

# Unprivileged nginx (uid 101, port 8080): serves the SPA and proxies /api to the API container, so the browser sees
# one origin and the refresh-token cookie stays first-party.
FROM nginxinc/nginx-unprivileged:1.27-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --retries=3 CMD wget -qO- http://127.0.0.1:8080/healthz >/dev/null || exit 1
