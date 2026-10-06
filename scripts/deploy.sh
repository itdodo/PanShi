#!/usr/bin/env bash
# 部署 = 前置检查 → 构建镜像 → 起容器 → 冒烟 → 留痕，一条命令。
#
# 为什么把 prebuild-check 焊在这里而不是「记得先跑一下」：这个仓库的部署一直是手敲
# docker compose build，门禁只要能被随手跳过，赶时间时它就一定被跳过。
#
# 用法：bash scripts/deploy.sh
#       bash scripts/deploy.sh --force "<绕过的理由>"   # 会大声打出来并记进日志
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
BASE=${PS_BASE:-http://127.0.0.1:18080}
DEPLOY_LOG=logs/deploy.log
mkdir -p logs

FORCE_REASON=""
if [ "${1:-}" = "--force" ]; then
  FORCE_REASON="${2:-（没给理由）}"
  echo "⚠ 跳过 CI 前置检查（--force）：$FORCE_REASON"
  echo "  只在「CI 结论已知等价」或「紧急回滚」时用；正常情况先修 CI。"
else
  bash scripts/prebuild-check.sh || {
    echo
    echo "✘ 部署中止：CI 前置检查没过（原因见上）。"
    echo "  确需绕过：bash scripts/deploy.sh --force \"<理由>\""
    exit 1
  }
fi

SHA=$(git rev-parse --short HEAD)
DIRTY=$(git status --porcelain | wc -l | tr -d ' ')
[ "$DIRTY" != "0" ] && echo "⚠ 工作区有 $DIRTY 处改动未提交：构建出来的镜像与 CI 验过的代码不是同一份"
echo "▶ 构建镜像（${SHA}）"
docker compose build panshi-api || { echo "✘ 构建失败（mcr 偶发 TLS/限速，重试即可；别改 Dockerfile 绕）"; exit 1; }
IMAGE=$(docker images --format '{{.ID}}' panshi-panshi-api | head -1)

echo "▶ 起容器"
docker compose up -d panshi-api || { echo "✘ 容器启动失败"; exit 1; }

echo "▶ 冒烟（${BASE}）"
FAIL=0
NOTE=""

# 等应用起来：启动引导要等 PG → CodeFirst → 迁移 → 种子，别一上来就判失败
HTTP=""
for _ in $(seq 1 30); do
  HTTP=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$BASE/api/v1/health" || echo 000)
  [ "$HTTP" = "200" ] && break
  sleep 2
done

expect() { # expect <名称> <期望> <实际>
  if [ "$2" = "$3" ]; then
    printf '  ✔ %-34s %s\n' "$1" "$3"
  else
    printf '  ✘ %-34s 期望 %s，实际 %s\n' "$1" "$2" "$3"
    FAIL=$((FAIL + 1))
  fi
}

expect "存活探针 /api/v1/health" "200" "$HTTP"
# ⚠️ 状态码在这套架构里不够：SPA 兜底会把未知路径回成 200 + index.html，所以要核响应体
expect "就绪探针响应体（真问库）" "Healthy" "$(curl -s --max-time 10 "$BASE/api/v1/health/ready" | tr -d '\r\n')"
expect "验证码开着（安全开关）" "image/gif" "$(curl -s -o /dev/null -w '%{content_type}' --max-time 10 "$BASE/api/v1/auth/captcha")"
expect "无令牌访问业务端点" "401" "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$BASE/api/v1/sys/user/page")"
expect "生产不暴露 openapi 文档" "404" "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$BASE/openapi/v1.json")"
expect "SPA 首页在" "served" "$(curl -s --max-time 10 "$BASE/" | grep -q 'id="app"' && echo served || echo missing)"

ERRS=$(docker compose logs --since 3m panshi-api 2>&1 | grep -cE '\[[0-9:]+ (ERR|FTL)\]' || true)
expect "启动日志无 ERR/FTL" "0" "$ERRS"
STATUS=$(docker inspect --format '{{.State.Health.Status}}' panshi-api 2>/dev/null || echo 未知)
echo "  · 容器健康态：$STATUS（HEALTHCHECK 有宽限期，starting 属正常）"

if [ "$FAIL" = "0" ]; then
  echo "✔ 部署完成：${SHA} / 镜像 ${IMAGE} / ${BASE}"
else
  echo "✘ 冒烟有 $FAIL 项不符，先别当成功收尾（看上面哪条红了）"
fi

# 留痕：logs/ 已被 gitignore，这是本机部署史，不进仓库
printf '%s\tsha=%s\tdirty=%s\timage=%s\tsmoke=%s\tforce=%s\n' \
  "$(date '+%Y-%m-%d %H:%M:%S')" "$SHA" "$DIRTY" "$IMAGE" \
  "$([ "$FAIL" = "0" ] && echo pass || echo "fail($FAIL)")" \
  "${FORCE_REASON:--}" >> "$DEPLOY_LOG"

[ "$FAIL" = "0" ] || exit 1
