# ---------- 阶段1：前端构建 ----------
FROM node:22-alpine AS web-build
WORKDIR /src/web
COPY web/package*.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---------- 阶段2：后端编译 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY Directory.Build.props Panshi.slnx ./
COPY src/Panshi.Model src/Panshi.Model
COPY src/Panshi.Common src/Panshi.Common
COPY src/Panshi.Repository src/Panshi.Repository
COPY src/Panshi.Service src/Panshi.Service
COPY src/Panshi.Api src/Panshi.Api
RUN dotnet publish src/Panshi.Api -c Release -o /publish /p:NoWarn=NETSDK1138

# ---------- 阶段3：运行时单容器（API + 静态资源） ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ⚠️ 备份作业用 pg_dump，客户端版本必须 ≥ 服务端（PG 17）。Ubuntu 默认 postgresql-client 是 16，
# 会因 "server version mismatch" 直接放弃 → 0 字节备份。故从 PGDG 装 postgresql-client-17。
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl ca-certificates gnupg; \
    install -d /usr/share/postgresql-common/pgdg; \
    curl -fsSL --retry 3 https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc; \
    echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt $(. /etc/os-release && echo "$VERSION_CODENAME")-pgdg main" > /etc/apt/sources.list.d/pgdg.list; \
    apt-get update; \
    apt-get install -y --no-install-recommends postgresql-client-17; \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api-build /publish ./
COPY --from=web-build /src/web/dist ./wwwroot
RUN mkdir -p /app/uploads /app/logs
ENV TZ=Asia/Shanghai
EXPOSE 8080
# 存活探针：只问进程答不答（查依赖会让库抖动把 API 反复判死重启）；就绪看 /api/v1/health/ready。
# start-period 给足——启动引导会等 PG 就绪，最长 24×5s。curl 是上面装 PGDG 时已经进镜像的。
HEALTHCHECK --interval=30s --timeout=5s --start-period=150s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/api/v1/health || exit 1
ENTRYPOINT ["dotnet", "Panshi.Api.dll"]
