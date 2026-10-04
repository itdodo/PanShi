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
LOG_DIR=$(mktemp -d 2>/dev/null || echo /tmp/panshi-verify)
mkdir -p "$LOG_DIR"

step() {
  local name=$1 code=0; shift
  printf '\n▶ %s\n' "$name"
  "$@" || code=$?
  if [ "$code" -eq 0 ]; then
    printf '✔ %s\n' "$name"
  else
    printf '✘ %s 失败（退出码 %s）\n' "$name" "$code"
    FAILED=1
  fi
}

# 只对「已知会偶发」的步骤重试。重试成功也要大声说出来并留下日志——
# 静默重试等于把真问题藏起来；这里要的是「偶发可容忍，但不许无声」。
# ⚠️ 退出码必须用 `cmd || code=$?` 立刻取：`if cmd; then…fi` 走 false 分支后 $? 是 if 语句自己的 0。
step_retry() {
  local name=$1 log="$LOG_DIR/$2" code=0 code2=0; shift 2
  printf '\n▶ %s\n' "$name"
  "$@" >"$log" 2>&1 || code=$?
  if [ "$code" -eq 0 ]; then
    printf '✔ %s\n' "$name"
    return 0
  fi
  printf '  … 第 1 次失败（退出码 %s），重试一次。日志：%s\n' "$code" "$log"
  sleep 3
  "$@" >"$log" 2>&1 || code2=$?
  if [ "$code2" -eq 0 ]; then
    printf '⚠ %s 重试后通过——第 1 次是偶发失败，原因见 %s（别忽略这类抖动）\n' "$name" "$log"
    return 0
  fi
  printf '✘ %s 重试仍失败（退出码 %s），失败输出末尾：\n' "$name" "$code2"
  tail -25 "$log" | sed 's/^/    /'
  FAILED=1
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
  # 计数行按语言两种写法都认：CI 上 DOTNET_CLI_UI_LANGUAGE=en，输出是 "0 Warning(s)"
  dotnet build Panshi.slnx --nologo | grep -E "个警告|个错误|Warning\(s\)|Error\(s\)"
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
  # 数据库从哪来：本机是 compose 起的 panshi-db 容器；CI 用服务容器 + PANSHI_TEST_CONN 自证，
  # 这时再要求「有个叫 panshi-db 的容器」就是门禁自己造的假阴性。
  if [ -n "${PANSHI_TEST_CONN:-}" ] || docker ps --format '{{.Names}}' 2>/dev/null | grep -q '^panshi-db$'; then
    step "集成测试（真实 PG）" dotnet test tests/Panshi.IntegrationTests/Panshi.IntegrationTests.csproj --nologo -v q
  else
    printf '\n✘ 集成测试需要数据库：先 `docker compose up -d panshi-db`，或设 PANSHI_TEST_CONN，或用 --fast 跳过。\n'
    FAILED=1
  fi
fi

# 4) 前端类型检查。注意：vite build 不会发现类型问题，所以这步不能被 build 代替。
typecheck_web() { (cd web && npx vue-tsc --noEmit); }
step "前端类型检查" typecheck_web

# 5) 前端构建。这一步观察到过一次偶发失败（单独重跑与整脚本重跑都是绿的，原因未定位），
#    所以给它一次重试，并把两次的输出都留在 $LOG_DIR 里。
build_web() { (cd web && npm run build); }
step_retry "前端构建" web-build.log build_web

stop_at_first_failure
printf '\n验收全部通过。步骤日志：%s\n' "$LOG_DIR"
