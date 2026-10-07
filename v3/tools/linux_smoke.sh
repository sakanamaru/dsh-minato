#!/usr/bin/env bash
# dsh-minato Linux 冒烟测试
# 纪律：只读命令用真实数据；写命令一律在**隔离根**里跑且**不带 --yes**（只打印计划）；
#       绝不触碰默认端口 3080 的实例；所有可能等待输入的地方都加 timeout 防挂。
#
# ★★★ **0 断言的假绿修复（2026-10-07 批次 E5 / 外部审查建议 §2.1）** ✓✓
#   ✗ 这个脚本原来 **27 行、0 个断言** ✗ —— 它只能因**崩溃**或**超时**变红 ✓
#     → 挂在 CI 里却证明不了任何一条对外承诺 ✓（"绿色"是假象 ✓）
#   ✓ 现在：**每个对外宣传的命令都断言行** ✓✓ 并给**项数下限**（与
#     `verify_failure_paths.sh:127-131` 同一手法 ✓）→ "有人删掉一条检查"也会红 ✓✓
#   注意（**不能假设的事**）：CI 的隔离根是全新的 ✓ → 第一次 `backup` **必然**
#     报 `BACKUP_NEEDS_DIR`（产品契约：第一次必须显式给目录 ✓）→ 断言写成
#     "BACKUP_OK **或** 明确拒绝"，两者都算通过 ✓ 但**必须正好一条终态标记** ✓
set -u
BIN="${1:-./dsh-minato}"
ISO="$(mktemp -d /tmp/dsht-smoke.XXXXXX)"
T="timeout 20"
pass=0; fail=0
ok(){ pass=$((pass+1)); echo "  [PASS] $1"; }
bad(){ fail=$((fail+1)); echo "  [FAIL] $1"; }
# 断言一条标记行：$1=名称 $2=输出 $3=正则
want(){ if printf '%s' "$2" | grep -qE "$3"; then ok "$1"; else bad "$1（未匹配 /$3/）"; fi }
cleanup(){ rm -rf "$ISO" 2>/dev/null; }
trap cleanup EXIT

echo "===== dsh-minato Linux 冒烟 ====="
echo "--- 0 环境"; uname -a; echo "DSH_HOME=${DSH_HOME:-<未设置，用真实数据根>}"; ls -l "$BIN" | awk '{print $5, $9}'

# ---- 只读命令：用真实数据根（CI runner 上是空的 → 必须**明确拒绝**，不许崩）----
echo "--- 1 about"; A0=$($T "$BIN" about 2>&1)
want "about 打印非官方声明与版本行" "$A0" '非官方|Not affiliated|unofficial'
echo "--- 2 version"; A1=$($T "$BIN" version 2>&1)
want "version 打印 DSHT_VERSION 标记" "$A1" '^DSHT_VERSION [0-9]'
echo "--- 3 status（只读）"; A2=$($T "$BIN" status 2>&1)
want "status 打印 STATUS_ 标记" "$A2" '^STATUS_[A-Z]+'
echo "--- 4 doctor（只读）"; A3=$($T "$BIN" doctor 2>&1); printf '%s\n' "$A3" | head -24
want "doctor 打印 DOCTOR_ 结论" "$A3" '^DOCTOR_[A-Z]+'
echo "--- 5 sessions（只读，前 3 行）"; A4=$($T "$BIN" sessions 2>&1); printf '%s\n' "$A4" | head -3
# 诚实性断言：没数据就说 SESSIONS_FAIL ✓ 有数据就说 SESSIONS_OK ✓（**不许静默空输出** ✗）
want "sessions 明确报成功或失败（不静默）" "$A4" '^SESSIONS_(OK|FAIL)'
echo "--- 6 profiles（只读，前 8 行）"; A5=$($T "$BIN" profiles 2>&1); printf '%s\n' "$A5" | head -8
want "profiles 明确报成功或失败（不静默）" "$A5" '^(PROFILES_OK|PROFILES_FAIL)'
echo "--- 7 profilepatch 计划（不写盘）"; A6=$($T "$BIN" profilepatch --profile web --id demo 2>&1)
want "profilepatch 打印计划或明确失败" "$A6" '^PROFILEPATCH_[A-Z]+'

# ---- 隔离根：写命令 ----
echo "--- 8 隔离根：backup（全新根 → 必然要 --to）"; A7=$(DSH_HOME="$ISO" $T "$BIN" backup 2>&1)
printf '%s\n' "$A7" | head -4
# 终态只能有一个：成功 ✓ 或明确要求目录 ✓（两者都算"没有静默失败" ✓）
if printf '%s' "$A7" | grep -qE '^BACKUP_OK '; then ok "backup 成功并打印 BACKUP_OK"
elif printf '%s' "$A7" | grep -qE '^BACKUP_(NEEDS_DIR|FAIL) '; then ok "全新隔离根下 backup 明确要求 --to（BACKUP_NEEDS_DIR ✓ 契约正确 ✓）"
else bad "backup 既没成功也没明确拒绝（疑似崩溃/静默）"; fi
echo "--- 9 隔离根：backup --to（真实写盘 + 完成标记）"; BK="$ISO/bk"; mkdir -p "$BK"
A8=$(DSH_HOME="$ISO" $T "$BIN" backup --to "$BK" 2>&1); printf '%s\n' "$A8" | head -4
want "backup --to 打印 BACKUP_OK（隔离根内真跑通）" "$A8" '^BACKUP_OK '
BP=$(printf '%s\n' "$A8" | tr -d '\r' | awk '/^BACKUP_OK /{print $2}'; exit 0)
if [ -n "$BP" ] && [ -f "$BP.manifest" ]; then ok "完成标记写在包**同级**（$BP.manifest ✓）"; else bad "完成标记缺失 ✗"; fi
echo "--- 10 隔离根：backup-list --verify"; A9=$(DSH_HOME="$ISO" $T "$BIN" backup-list --verify 2>&1)
printf '%s\n' "$A9" | head -4
want "backup-list --verify 打印汇总行" "$A9" '^BACKUP_VERIFY_TOTAL '
# 刚备份的包必须被判 complete ✓ 且不许出现 mismatch ✓（后者是"内容与标记不符"的硬失败）
want "刚备份的包被判 complete" "$A9" '^BACKUP_VERIFY .* complete '
if printf '%s' "$A9" | grep -qE '^BACKUP_VERIFY .* mismatch'; then bad "干净的包被判 mismatch ✗✗"; else ok "无 mismatch ✓"; fi
# ★ 备份链审查（2026-10-07，D1/A1）：导出必须**真的复活** ✓✓
#   修复前 CopyTree 返回"跳过的嵌套包数" → 非空包 100% BKEXPORT_FAIL ✗ —— Linux 同构实现同样中招 ✗
#   这里在隔离根里真导一次：BKEXPORT_OK + 旁挂 .manifest 缺一不可 ✓
echo "--- 10b 隔离根：backup-export（审查 D1/A1 回归：导出复活）"; EX="$ISO/export"; mkdir -p "$EX"
A9B=$(DSH_HOME="$ISO" $T "$BIN" backup-export --to "$EX" --yes 2>&1); printf '%s\n' "$A9B" | head -4
want "backup-export 打印 BKEXPORT_OK（修复前非空包 100% FAIL ✗✗）" "$A9B" '^BKEXPORT_OK '
EXP=$(printf '%s\n' "$A9B" | tr -d '\r' | awk '/^BKEXPORT_OK /{print $2}'; exit 0)
if [ -n "$EXP" ] && [ -f "$EXP.manifest" ]; then ok "导出旁挂 .manifest 存在（审查 D1 核心症状 ✓）"; else bad "导出旁挂 .manifest 缺失 ✗"; fi
echo "--- 11 隔离根：restore --dry-run（只读预览，绝不写盘）"; A10=$(DSH_HOME="$ISO" $T "$BIN" restore --dry-run 2>&1)
printf '%s\n' "$A10" | head -8
# 两种可接受结果：有备份 → DRYRUN_OK（预览 ✓）；没备份 → 明确拒绝 ✓。**都不许写盘** ✓
if printf '%s' "$A10" | grep -qE '^DRYRUN_(OK|FAIL)|^RESTORE_FAIL'; then ok "restore --dry-run 给出明确结论（DRYRUN_OK / 明确拒绝 ✓）"; else bad "restore --dry-run 结论不明 ✗"; fi
if printf '%s' "$A10" | grep -qE '^RESTORE_(OK|APPLY_ROOT)'; then bad "dry-run 竟然报告了真实写盘 ✗✗"; else ok "dry-run 未报告写盘 ✓"; fi
echo "--- 12 隔离根：config-get（只读配置面）"; A11=$(DSH_HOME="$ISO" $T "$BIN" config-get 2>&1 | head -12)
printf '%s\n' "$A11" | head -3
want "config-get 打印 CONFIGGET_OK 与配置项" "$A11" '^CONFIGGET_OK|^CONFIG '

echo "--- 13 安全：start 计划（不带 --yes，绝不启动）"; A12=$(DSH_HOME="$ISO" $T "$BIN" start --port 3999 2>&1)
want "start 不打 --yes 时只打印计划" "$A12" '^START_(PLAN|FAIL|OBSERVED)'
if printf '%s' "$A12" | grep -qE '^START_OK'; then bad "start 没确认就启动了 ✗✗"; else ok "start 未在未确认时启动 ✓"; fi
echo "--- 14 安全：stop 计划（不带 --yes，绝不停止）"; A13=$(DSH_HOME="$ISO" $T "$BIN" stop --port 3999 2>&1)
want "stop 给出明确结论（计划/未观测到）" "$A13" '^STOP_(PLAN|FAIL|OBSERVED)'
echo "--- 15 shortcut 计划（不带 --yes；同时验证单文件下能取到自身路径 ✓）"; A14=$(DSH_HOME="$ISO" $T "$BIN" shortcut 2>&1)
want "shortcut 不打 --yes 时只打印计划" "$A14" '^SHORTCUT_(PLAN|DRYRUN|FAIL)'
echo "--- 16 菜单（管道输入，EOF 即退出）"; printf '4\nq\n' | $T "$BIN" 2>&1 | head -8
echo "--- 17 默认端口是否仍被你的实例占用（应为监听中）"; (ss -ltnp 2>/dev/null || netstat -ltnp 2>/dev/null) | grep -E ':3080' || echo "3080 未监听"

echo ""
echo "== 结果：PASS=$pass FAIL=$fail =="
# #43：**项数下限**断言 ✓✓ —— 否则"删掉一条检查"脚本仍报 PASS 但项数变少 ✗（与 verify_failure_paths.sh 同一做法 ✓）
EXPECTED_MIN=20
echo "== 结果：PASS=$pass FAIL=$fail（声明最少 $EXPECTED_MIN 项）=="
if [ "$pass" -lt "$EXPECTED_MIN" ]; then
  echo "  [FAIL] 项数不足：实跑 $pass < 声明 $EXPECTED_MIN（有检查被删掉或没执行到）"
  fail=$((fail+1))
fi
[ "$fail" -eq 0 ] && exit 0 || exit 1
