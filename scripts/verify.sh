#!/usr/bin/env bash
# 提交前验收：一条命令跑完后端编译/两套测试 + 前端类型检查/构建。
# 为什么要它：这一轮审计里发现 vue-tsc 一直是红的（wangEditor 类型入口问题），
# 但因为 vite build 不受影响，没人被拦下来——红着跑了很多次。所以把五步钉成一条必跑命令。
#
# 用法：bash scripts/verify.sh          # 全跑
#       bash scripts/verify.sh --fast    # 跳过集成测试（不需要数据库的那几步）
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
FAST=${1:-}
FAILED=0

step() {
  local name=$1; shift
  printf '\n▶ %s\n' "$name"
  if "$@"; then
    printf '✔ %s\n' "$name"
  else
    local code=$?
    printf '✘ %s 失败（退出码 %s）\n' "$name" "$code"
    FAILED=1
  fi
}

stop_at_first_failure() {
  if [ "$FAILED" -ne 0 ]; then
    printf '\n验收未通过。修完再跑一次，别带着红的步骤去 build 镜像。\n'
    exit 1
  fi
}

# 1) 后端编译。0 警告是本仓库的既有基线（CS1591 已被 Directory.Build.props 有意压掉），
#    所以这里只卡错误，但把警告数打出来——它变多了就该看一眼。
build_backend() {
  dotnet build Panshi.slnx --nologo | grep -E "个警告|个错误"
  return "${PIPESTATUS[0]}"
}
step "后端编译" build_backend

# 2) 单元测试（不依赖数据库）
step "单元测试" dotnet test tests/Panshi.Tests/Panshi.Tests.csproj --nologo -v q

if [ "$FAST" = "--fast" ]; then
  echo
  echo "⏭  --fast：跳过集成测试（需要 panshi-db 在跑）"
else
  # 3) 集成测试跑真实 PG。两个 csproj 必须一个一个跑——并行会撞 dll 文件锁。
  if ! docker ps --format '{{.Names}}' 2>/dev/null | grep -q '^panshi-db$'; then
    printf '\n✘ 集成测试需要数据库：先 `docker compose up -d panshi-db`，或用 --fast 跳过。\n'
    FAILED=1
  else
    step "集成测试（真实 PG）" dotnet test tests/Panshi.IntegrationTests/Panshi.IntegrationTests.csproj --nologo -v q
  fi
fi

# 4) 前端类型检查。注意：vite build 不会发现类型问题，所以这步不能被 build 代替。
typecheck_web() { (cd web && npx vue-tsc --noEmit); }
step "前端类型检查" typecheck_web

# 5) 前端构建
build_web() { (cd web && npm run build); }
step "前端构建" build_web

stop_at_first_failure
printf '\n验收全部通过。\n'
