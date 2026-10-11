#!/usr/bin/env bash
# 老库升级演练：证明「线上那种已经跑到某个历史版本的库」能被当前构建升上来，而且不误拦。
#
# 为什么单独跑一条：verify.sh 的集成测试每次都从**空库**完整引导（CodeFirst → 全部迁移 → 种子），
# 它证明不了「增量升级」这条路径。而这一轮真正栽过的就是增量路径——
# 0005 被追加过语句（老库永远不重跑）、0013 之前老库里两张单据表根本没索引、0014 的指纹列要补记 13 行。
# 空库引导对这些全部无感，只有「先有老库、再升上去」才会暴露。
#
# 用法：
#   Db__ConnectionString="Host=...;Database=panshi_up;Username=...;Password=..." \
#   BASELINE_SHA=<老版本提交> bash scripts/upgrade-check.sh
# 也可把基线提交作为第一个参数传进来。连接串里的库必须是**允许随意建表删表**的演练库。
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1

BASELINE="${1:-${BASELINE_SHA:-}}"
CONN="${Db__ConnectionString:-}"
PSQL_CMD="${PSQL_CMD:-psql}"
PORT_APP="${PORT_APP:-5125}"
MIG_DIR="src/Panshi.Repository/db/migrations"

if [ -z "$BASELINE" ] || [ -z "$CONN" ]; then
  echo "✘ 缺 BASELINE_SHA（老版本提交）或 Db__ConnectionString（演练库连接串）。"
  echo "  例：BASELINE_SHA=5631a98 Db__ConnectionString=\"Host=localhost;Port=5432;Database=panshi_up;Username=panshi;Password=…\" bash scripts/upgrade-check.sh"
  exit 2
fi
if ! command -v "$PSQL_CMD" >/dev/null 2>&1; then
  echo "✘ 找不到 psql 客户端（或 PSQL_CMD 指错了）。主机 PATH 里常常没有 psql，"
  echo "  这时可以用 PSQL_CMD 指向一个包到容器里执行的壳子。"
  exit 2
fi

# 连接串 → psql 认的 PG* 变量（同一份配置喂给应用和断言，避免两边写岔）
pick() { sed -n "s/.*$1=\([^;]*\).*/\1/p" <<<"$CONN" | head -1; }
export PGHOST="$(pick Host)"; export PGPORT="$(pick Port)"; export PGDATABASE="$(pick Database)"
export PGUSER="$(pick Username)"; export PGPASSWORD="$(pick Password)"
q() { "$PSQL_CMD" -At -c "$1"; }

# Git Bash 下 mktemp 给的是 MSYS 路径（/tmp/…），原生 git 收到它可能把目录建到别的盘符下，
# 于是 bash 这边 cd 不进去、还静默失败。给 git 换成 Windows 路径，两边指同一处。
to_native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# 读 Serilog 文件计数（APP_LOG_DIR 由 boot 设好）
app_log_count() { cat "$APP_LOG_DIR"/panshi-*.log 2>/dev/null | grep -cE "$1"; }

# 某个提交上有几个迁移脚本＝那个版本引导出来的水位
scripts_at() { git ls-tree -r --name-only "$1" -- "$MIG_DIR" | grep -c '\.sql$'; }

WANT_OLD="$(scripts_at "$BASELINE")"
WANT_NEW="$(ls "$MIG_DIR"/*.sql | wc -l | tr -d ' ')"
[ "$WANT_OLD" -gt 0 ] || { echo "✘ 基线 $BASELINE 上找不到迁移脚本（历史没拉全？CI 需要 fetch-depth: 0）"; exit 1; }

echo "▶ 基线 $BASELINE（$WANT_OLD 个迁移） → 当前 $WANT_NEW 个迁移，演练库 $PGDATABASE"

LOG_DIR="$(mktemp -d)"
WT="$LOG_DIR/baseline"
FAILED=0

cleanup() {
  git worktree remove --force "$(to_native "$WT")" >/dev/null 2>&1
  git worktree prune >/dev/null 2>&1
  rm -rf "$LOG_DIR"
}
trap cleanup EXIT

API_PID=""
APP_LOG_DIR=""

# 起服务/等就绪。两个写法坑（都实测踩过）：
#   · `( cd x && exec env A=1 dotnet … ) &` 在 Git Bash 里会静默不启动（日志 0 字节、无监听），
#     去掉 exec 改用前置赋值才正常；
#   · 直接跑 dll 而不是 dotnet run：后者会再 fork 一层，kill $! 打不到真正的服务进程。
boot() { # boot <目录> <日志名>
  # ⚠️ 不要写成 `local dir=$1 out="$dir/…"`：local 的一条语句会先把右侧全部展开，
  # 那时 dir 还不存在，配上 set -u 就是「dir: unbound variable」。分行赋。
  local dir=$1
  local log=$2
  local out="$dir/src/Panshi.Api/bin/Debug/net10.0"
  if [ ! -f "$out/Panshi.Api.dll" ]; then
    echo "✘ 找不到 $out/Panshi.Api.dll —— 该目录是否存在？（build 有没有产出）"
    ls -d "$dir/src/Panshi.Api/bin" 2>&1 | sed 's/^/    /'
    return 1
  fi
  # 断言一律读 Serilog 自己写的文件（UTF-8）。控制台重定向在 Windows 上是 GBK，
  # 用它 grep 中文关键词永远 0 命中 —— 那不是「没有」，是假通过。
  APP_LOG_DIR="$out/logs"
  rm -f "$APP_LOG_DIR"/panshi-*.log 2>/dev/null
  ( cd "$out"       && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$PORT_APP"          Db__ConnectionString="$CONN" dotnet Panshi.Api.dll > "$LOG_DIR/$log" 2>&1 ) &
  API_PID=$!
  local i
  for i in $(seq 1 90); do
    if curl -sf -o /dev/null --max-time 3 "http://127.0.0.1:$PORT_APP/api/v1/health/ready"; then
      echo "  ready（第 $i 次探测）"; return 0
    fi
    sleep 2
  done
  echo "✘ 180 秒内没就绪。控制台日志末尾："
  tail -30 "$LOG_DIR/$log" 2>&1 | sed 's/^/    /'
  echo "  Serilog 文件末尾："
  cat "$APP_LOG_DIR"/panshi-*.log 2>/dev/null | tail -30 | sed 's/^/    /'
  return 1
}

# 停服务：kill $! 之外再按端口兜一次，否则下一轮 boot 撞端口，报出来的还是上一轮的日志。
stop_api() {
  [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null
  API_PID=""
  if command -v ss >/dev/null 2>&1; then
    local p; p="$(ss -ltnp 2>/dev/null | grep ":$PORT_APP " | grep -oE 'pid=[0-9]+' | head -1 | cut -d= -f2)"
    [ -n "$p" ] && kill "$p" 2>/dev/null
  elif command -v fuser >/dev/null 2>&1; then
    fuser -k "${PORT_APP}/tcp" >/dev/null 2>&1
  elif command -v netstat >/dev/null 2>&1 && command -v powershell >/dev/null 2>&1; then
    local p; p="$(netstat -ano 2>/dev/null | grep ":$PORT_APP .*LISTENING" | head -1 | awk '{print $NF}')"
    [ -n "$p" ] && powershell -NoProfile -Command "Stop-Process -Id $p -Force -ErrorAction SilentlyContinue" >/dev/null 2>&1
  fi
  local i
  for i in $(seq 1 20); do
    curl -sf -o /dev/null --max-time 2 "http://127.0.0.1:$PORT_APP/api/v1/health/ready" || return 0
    sleep 1
  done
  echo "✘ 端口 $PORT_APP 上的服务没停下来，后面的探测都不可信"
  return 1
}

# ① 用基线版本把演练库建成「老库」
git worktree add --detach "$(to_native "$WT")" "$BASELINE" >/dev/null 2>&1 || { echo "✘ worktree 建不起来（$WT）"; exit 1; }
echo "▶ 用基线版本引导老库"
(cd "$WT" && dotnet build src/Panshi.Api --nologo -v q >/dev/null) || { echo "✘ 基线版本编译失败"; exit 1; }
if ! boot "$WT" old.log; then FAILED=1; fi
OLD_WATERMARK="$(q "select coalesce(max(version),0) from sys_db_migration")"
OLD_ROWS="$(q "select count(*) from sys_db_migration")"
HAS_SUM="$(q "select count(*) from information_schema.columns where table_name='sys_db_migration' and column_name='checksum'")"
stop_api
echo "  老库：水位 $OLD_WATERMARK、流水 $OLD_ROWS 条、指纹列 $([ "$HAS_SUM" = "0" ] && echo '没有（正是升上去的前提）' || echo '已有')"
[ "$OLD_ROWS" = "$WANT_OLD" ] || { echo "✘ 基线引导出的流水条数不是 $WANT_OLD"; FAILED=1; }

# ② 当前构建升上去
echo "▶ 用当前构建升级"
dotnet build src/Panshi.Api --nologo -v q >/dev/null || { echo "✘ 当前构建编译失败"; FAILED=1; }
if ! boot . new1.log; then FAILED=1; fi
NEW_ROWS="$(q "select count(*) from sys_db_migration")"
MISSING_SUM="$(q "select count(*) from sys_db_migration where checksum is null")"
APPLIED="$(app_log_count '迁移已应用')"
BACKFILL="$(app_log_count '首次登记内容指纹')"
ERRS="$(app_log_count '\[ERR\]|\[FTL\]')"
echo "  升级后：流水 $NEW_ROWS 条、缺指纹 $MISSING_SUM 条、本次应用 $APPLIED 个、补记 $BACKFILL 条、ERR/FTL $ERRS"
[ "$NEW_ROWS" = "$WANT_NEW" ] || { echo "✘ 升级后流水不是 $WANT_NEW 条"; FAILED=1; }
[ "$MISSING_SUM" = "0" ] || { echo "✘ 有 $MISSING_SUM 行没登记指纹"; FAILED=1; }
[ "$ERRS" = "0" ] || { echo "✘ 启动日志里有 ERR/FTL"; FAILED=1; }
if [ "$WANT_NEW" -gt "$WANT_OLD" ]; then
  [ "$APPLIED" -ge "$((WANT_NEW - WANT_OLD))" ] || { echo "✘ 该应用的迁移没应用够"; FAILED=1; }
fi

# ③ 第二次启动必须完全静默——这才是「指纹比对不误拦」的证据
stop_api
echo "▶ 第二次启动（必须静默）"
if ! boot . new2.log; then FAILED=1; fi
NOISY="$(app_log_count '迁移已应用|首次登记内容指纹|不一致')"
echo "  第二次启动的迁移输出行数：$NOISY（期望 0）"
[ "$NOISY" = "0" ] || { echo "✘ 第二次启动还在动迁移或报不一致——指纹守卫把老库自己拦住了"; FAILED=1; }
AFTER_ROWS="$(q "select count(*) from sys_db_migration")"
[ "$AFTER_ROWS" = "$WANT_NEW" ] || { echo "✘ 第二次启动改写了流水（$AFTER_ROWS）"; FAILED=1; }
stop_api

if [ "$FAILED" -ne 0 ]; then
  echo "老库升级演练未通过。日志目录（临时）：$LOG_DIR —— trap 会清掉，先别急着重跑，把上面输出留住。"
  exit 1
fi
printf '✔ 老库升级演练通过：%s → %s 个迁移，第二次启动静默\n' "$WANT_OLD" "$WANT_NEW"
