#!/usr/bin/env bash
# 落镜像前的硬前置：HEAD 在 GitHub 上的 CI 绿了吗？
#
# 为什么要有它：这个仓库的部署动作是「手工 docker compose build + up -d」，没有 PR 合并那道门，
# 所以 CI 红了也照样能把镜像推上 18080——门禁等于只跑了给人看。脚本把「CI 绿」变成构建前的条件。
#
# 用法：bash scripts/prebuild-check.sh        # 0=可以构建；1=CI 红；2=判不了（先按提示处理）
# 只用 curl + grep：Git Bash 与 ubuntu runner 都能跑，不依赖 jq/python。
set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

git rev-parse --verify HEAD >/dev/null 2>&1 || { echo "✘ 仓库还没有任何提交"; exit 2; }

# 工作区脏 → CI 跑的根本不是这份代码，结论无意义
if [ -n "$(git status --porcelain)" ]; then
  echo "✘ 工作区有未提交改动：CI 结论对应不了将要构建的这份代码。先提交（或明确 stash）再构建。"
  exit 2
fi

REMOTE_URL=$(git remote get-url origin 2>/dev/null) || { echo "✘ 没有 origin，无法查 CI 结论"; exit 2; }
REPO=$(printf '%s' "$REMOTE_URL" | sed -E 's#.*github\.com[:/]##; s#\.git$##')
SHA=$(git rev-parse HEAD)

# 先分清「没 push」和「push 了但流水线还没登记」——处置完全不同。
# ⚠️ 判据必须是输出而不是退出码：git branch -r --contains 对「本地有、远端没有」的提交
# 是打印空 + 退出码 0，拿退出码判断会永远走不到这一支。
if [ -z "$(git branch -r --contains "$SHA" 2>/dev/null)" ]; then
  echo "✘ 这个提交还没 push 到 origin：${SHA:0:7}。CI 只在远端跑，先 push 才有结论可查。"
  exit 2
fi

RUNS_API="https://api.github.com/repos/${REPO}/actions/runs?head_sha=${SHA}"
CHECK_API="https://api.github.com/repos/${REPO}/commits/${SHA}/check-runs"

# 排队 → 登记之间可能有好几分钟空窗（实测 push 后 2 分钟仍查不到 run 对象），所以给一个宽窗口。
# 问 runs 而不是只问 check-runs：后者要等 job 真正起跑才有内容，把它当「没触发」会误报。
SEEN_RUN=0
for attempt in $(seq 1 12); do
  RUNS=$(curl -sf --max-time 30 "$RUNS_API") || { echo "✘ 查不到 ${REPO}@${SHA:0:7} 的运行记录（网络？私有库需带 token？）"; exit 2; }
  if printf '%s' "$RUNS" | grep -qE '"status":"(queued|in_progress)"'; then
    echo "… CI 还在排队/执行（${SHA:0:7}），等它结束再构建。"
    exit 2
  fi
  printf '%s' "$RUNS" | grep -q '"total_count":[1-9]' && { SEEN_RUN=1; break; }
  if [ "$attempt" = "12" ]; then break; fi
  [ $((attempt % 3)) = 0 ] && echo "… 还没有 ${SHA:0:7} 的运行记录（已等 $((attempt * 20)) 秒，GitHub 排队可能较慢）"
  sleep 20
done
if [ "$SEEN_RUN" = "0" ]; then
  echo "✘ 等了 4 分钟，origin 上的 ${SHA:0:7} 仍没有任何工作流运行记录。两种可能：排队异常久，或根本没被触发"
  echo "  （分支不在 on: 里？Actions 被禁用？workflow 权限不足？）。去 Actions 页确认后再构建，别硬上。"
  exit 2
fi

JSON=$(curl -sf --max-time 30 "$CHECK_API") || { echo "✘ 读 check-runs 失败（${SHA:0:7}）"; exit 2; }
CONCLUSIONS=$(printf '%s' "$JSON" | grep -o '"conclusion":[^,}]*' | sed 's/"conclusion"://' | sort | uniq -c)
if printf '%s' "$CONCLUSIONS" | grep -q 'null'; then
  echo "… 运行已结束但检查结论还没落定（${SHA:0:7}），稍后再试。当前：$(printf '%s' "$CONCLUSIONS" | tr '\n' ' ')"
  exit 2
fi
if printf '%s' "$CONCLUSIONS" | grep -qv 'success'; then
  echo "✘ CI 对 ${SHA:0:7} 不是全绿，别带着红的结论去 build 镜像。当前："
  printf '%s\n' "$CONCLUSIONS" | sed 's/^/    /'
  exit 1
fi

echo "✔ CI 全绿：${REPO}@${SHA:0:7} —— 可以构建镜像"
