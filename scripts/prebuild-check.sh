#!/usr/bin/env bash
# 落镜像前的硬前置：HEAD 在 GitHub 上的 CI 绿了吗？
#
# 为什么要有它：这个仓库的部署动作是「手工 docker compose build + up -d」，没有 PR 合并那道门，
# 所以 CI 红了也照样能把镜像推上 18080——门禁等于只跑了给人看。脚本把「CI 绿」变成构建前的条件。
#
# 用法：bash scripts/prebuild-check.sh        # 通过=0，可以 build；不通过=非 0，并说明为什么
set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

if ! git rev-parse --verify HEAD >/dev/null 2>&1; then
  echo "✘ 仓库还没有任何提交"
  exit 2
fi

# 工作区脏 → CI 跑的根本不是这份代码，结论无意义
if [ -n "$(git status --porcelain)" ]; then
  echo "✘ 工作区有未提交改动：CI 结论对应不了将要构建的这份代码。先提交（或明确 stash）再构建。"
  exit 2
fi

REMOTE_URL=$(git remote get-url origin 2>/dev/null) || { echo "✘ 没有 origin，无法查 CI 结论"; exit 2; }
REPO=$(printf '%s' "$REMOTE_URL" | sed -E 's#.*github\.com[:/]##; s#\.git$##')
SHA=$(git rev-parse HEAD)

# 先分清「没 push」和「push 了但流水线还没登记」——两者的处置完全不同。
# ⚠️ 判据必须是输出而不是退出码：git branch -r --contains 对「本地有、远端没有」的提交
# 是打印空 + 退出码 0，拿退出码判断会永远走不到这一支。
if [ -z "$(git branch -r --contains "$SHA" 2>/dev/null)" ]; then
  echo "✘ 这个提交还没 push 到 origin：${SHA:0:7}。CI 只在远端跑，先 push 才有结论可查。"
  exit 2
fi

API="https://api.github.com/repos/${REPO}/commits/${SHA}/check-runs"
TOTAL=""
for attempt in 1 2 3; do
  JSON=$(curl -sf --max-time 30 "$API") || { echo "✘ 查不到 ${REPO}@${SHA:0:7} 的 CI 记录（网络？仓库私有需带 token？）"; exit 2; }
  TOTAL=$(printf '%s' "$JSON" | grep -o '"total_count":[0-9]*' | head -1 | cut -d: -f2)
  [ -n "$TOTAL" ] && [ "$TOTAL" != "0" ] && break
  [ "$attempt" = "3" ] && break
  echo "… 远端已有该提交但还没有检查结论，流水线大概刚排队；15 秒后重试（$attempt/3）"
  sleep 15
done
if [ -z "$TOTAL" ] || [ "$TOTAL" = "0" ]; then
  echo "✘ origin 上有 ${SHA:0:7}，但 GitHub 没有给它任何检查结论——工作流没被触发（分支不在 on: 里？Actions 页被禁用？）。去看一眼再构建。"
  exit 2
fi

# conclusion 可能是 null（还在跑）、success、failure、cancelled…
CONCLUSIONS=$(printf '%s' "$JSON" | grep -o '"conclusion":[^,}]*' | sed 's/"conclusion"://' | sort | uniq -c)
if printf '%s' "$CONCLUSIONS" | grep -q 'null'; then
  echo "… CI 还在跑（${SHA:0:7}），等它结束再构建。当前：$(printf '%s' "$CONCLUSIONS" | tr '\n' ' ')"
  exit 2
fi
if printf '%s' "$CONCLUSIONS" | grep -qv 'success'; then
  echo "✘ CI 对 ${SHA:0:7} 不是全绿，别带着红的结论去 build 镜像。当前："
  printf '%s\n' "$CONCLUSIONS" | sed 's/^/    /'
  exit 1
fi

echo "✔ CI 全绿：${REPO}@${SHA:0:7}（$TOTAL 个检查）——可以构建镜像"
