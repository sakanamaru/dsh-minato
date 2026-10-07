#!/usr/bin/env bash
# ============================================================================
#  verify_backup_chain.sh —— 备份可信链与失败路径的可重复验证
#  ---------------------------------------------------------------------------
#  为什么有这个脚本：这条链上的每一条结论（标记、哈希、闸门、回滚、合并语义、
#  多工作区、失败时的诚实性）都是**真机验证**出来的，但验证过程原本只写在
#  会话记录里 —— 下一个人无法复现，只能选择相信。这个脚本把那些步骤固化成
#  一条命令。
#
#  用法：  bash verify_backup_chain.sh /path/to/dsh-minato
#  退出码：0 = 全部通过；1 = 有失败
#
#  安全：全程使用**隔离数据根**与**隔离备份根**（备份根 = exe 同目录 ✓），
#        绝不触碰默认数据根，绝不触碰默认端口，绝不联网。
# ============================================================================
set -u
CLI="${1:-}"
if [ -z "$CLI" ] || [ ! -x "$CLI" ]; then echo "用法: bash verify_backup_chain.sh /path/to/dsh-minato"; exit 2; fi
CLI=$(readlink -f "$CLI")
T="timeout 300"
pass=0; fail=0
ok(){ pass=$((pass+1)); echo "  [PASS] $1"; }
bad(){ fail=$((fail+1)); echo "  [FAIL] $1"; }

# 隔离根：exe 复制到全新目录 → 状态目录（含备份根）随之全新 ✓
WORK=$(mktemp -d /tmp/vbc-XXXXXX)
BIN="$WORK/bin"; mkdir -p "$BIN"; cp "$CLI" "$BIN/dsh-minato"; chmod +x "$BIN/dsh-minato"
CLI="$BIN/dsh-minato"
# ★★★ **CI 红修复（真机复现）**：备份根**必须在 exe 目录之外** ✓✓
#   ✗ 原来 `BKROOT="$BIN/backup"` ✗ —— 而 Linux 的 StateDir 是 XDG ✓
#     启动时的一次性迁移会把 `<exe 目录>/backup` **搬进 XDG** ✗✗
#     → **刚建好的备份当场被搬走** ✗ → 调用方手里的路径失效 → 4 条连锁 FAIL ✓
#   ✓ 现在放在 `$WORK/bk`（exe 目录的**外面** ✓）→ 迁移没有东西可搬 ✓✓
BKROOT="$WORK/bk"
A="$WORK/a"; mkdir -p "$A/storages"; printf 'ALPHA\n' > "$A/storages/a.txt"; printf 'BETA\n' > "$A/storages/b.txt"
cleanup(){ rm -rf "$WORK" "${MT:-/nonexistent}" 2>/dev/null; }
trap cleanup EXIT

# ★★★ **CI 红修复（2026-10-01 真机复现）—— 必须切到中性目录再跑** ✓✓
#   ✗ 工作区**自动探测**取的是**当前目录**（`LinuxPaths.WorkspaceRoot` ✓）
#     → 从仓库根调用本脚本时 → **整个仓库被当成工作区一起备份** ✗✗
#       （真机 CI 实测：`标记计数与内容一致（1015）` ✗ 而夹具里只有 2 个文件 ✓）
#     → 于是"包内混格式（顶层文件 19）"与"恢复报 UNRECOGNIZED"两条必然失败 ✓
#   ✓ 现在：**切到 $WORK**（在 /tmp 下 ✓ 而 /tmp 在**禁用工作区**列表里 ✓✓）
#     → 探测不到工作区 ✓ → 备份源就是夹具本身 ✓✓
#   ✓ 所有路径都是绝对路径 ✓ 所以 cd 是安全的 ✓
cd "$WORK" || exit 2
echo "== 备份可信链验证 =="
echo "  CLI: $($CLI version 2>/dev/null || echo '(version 失败)')"

# ---- 1 备份 + 完成标记 ----
O=$(DSH_HOME="$A" $T $CLI backup --to "$BKROOT" 2>&1 | tr -d '\r'); P=$(echo "$O" | awk '/BACKUP_OK/{print $2}')
[ -n "$P" ] && ok "备份成功（BACKUP_OK ✓）" || { bad "备份失败"; echo "$O" | sed 's/^/      /'; }
[ -f "$P.manifest" ] && ok "完成标记存在 ✓" || bad "完成标记缺失 ✗"
grep -q 'sha256=' "$P.manifest" 2>/dev/null && ok "标记含内容哈希 ✓" || bad "标记缺哈希 ✗"
MK=$(grep -o 'files=[0-9]*' "$P.manifest" 2>/dev/null | head -1 | cut -d= -f2)
REAL=$(find "$P" -type f 2>/dev/null | wc -l)
[ "$MK" = "$REAL" ] && ok "标记计数与内容一致（$MK ✓）" || bad "计数不符（标记 $MK ≠ 实际 $REAL）✗"

# ---- 2 --verify complete ----
V=$(DSH_HOME="$A" $T $CLI backup-list --verify 2>&1 | tr -d '\r')
echo "$V" | grep -qE "^BACKUP_VERIFY .*complete $REAL" && ok "--verify 报 complete ✓" || bad "--verify 异常 ✗"
echo "$V" | grep -qE '^BACKUP_VERIFY .*mismatch' && bad "干净的包被判 mismatch ✗" || ok "无 mismatch ✓"

# ---- 3 篡改内容 → 必须被发现（不改文件名/数量 ✓ 只有哈希能发现）----
printf 'TAMPERED\n' > "$P/storages/a.txt"
V2=$(DSH_HOME="$A" $T $CLI backup-list --verify 2>&1 | tr -d '\r')
echo "$V2" | grep -qE '^BACKUP_VERIFY .*mismatch' && ok "篡改被识别为 mismatch ✓" || bad "篡改未被发现 ✗✗"
printf 'ALPHA\n' > "$P/storages/a.txt"   # 还原 ✓

# ---- 4 恢复闸门：截断包必须被拒 ----
printf 'X\n' > "$WORK/keep.txt"; rm -f "$P/storages/b.txt"
R=$(DSH_HOME="$A" $T $CLI restore --path "$P" --apply --yes 2>&1 | tr -d '\r')
echo "$R" | grep -qE '^RESTORE_FAIL' && ok "截断包被拒（RESTORE_FAIL ✓）" || bad "截断包未被拒 ✗✗"
# #40：断言失败原因（只断言"出现 FAIL"会把"因别的理由失败"也算通过）
echo "$R" | grep -qE '不完整|哈希' && ok "截断包：原因正确（不完整/哈希 ✓✓）" || bad "截断包：原因不是不完整/哈希 ✗"
echo "$R" | grep -qE '^RESTORE_OK' && bad "截断包竟然报成功 ✗✗" || ok "未谎报成功 ✓"
printf 'BETA\n' > "$P/storages/b.txt"    # 还原 ✓

# ---- 5 合并语义：仅目标端存在的文件不得被删 ----
TG="$WORK/tg"; mkdir -p "$TG/storages"
printf 'TARGET-ONLY\n' > "$TG/storages/only-here.txt"
printf 'OLD\n' > "$TG/storages/a.txt"
DSH_HOME="$TG" $T $CLI restore --path "$P" --apply --yes >/dev/null 2>&1
[ "$(cat "$TG/storages/a.txt" 2>/dev/null)" = "ALPHA" ] && ok "同名文件被覆盖（合并语义 ✓）" || bad "同名文件未覆盖 ✗"
[ -f "$TG/storages/only-here.txt" ] && ok "仅目标端存在的文件被保留 ✓✓" || bad "目标端独有文件被删 ✗✗"

# ---- 6 回滚锚点：恢复前自动备份必须存在且可用 ----
grep -qE '^RESTORE_PRE_BACKUP ' <<<"$(DSH_HOME="$TG" $T $CLI restore --path "$P" --apply --yes 2>&1 | tr -d '\r')" \
  && ok "恢复前锚点被告知（RESTORE_PRE_BACKUP ✓）" || bad "锚点未告知 ✗"

# ---- 7 失败诚实性：不可读目录必须如实报告 ----
if [ "$(id -u)" != "0" ]; then
  U="$WORK/unread"; mkdir -p "$U/storages" "$U/secret"
  printf 'R\n' > "$U/storages/r.txt"; printf 'S\n' > "$U/secret/s.txt"; chmod 000 "$U/secret"
  UO=$(DSH_HOME="$U" $T $CLI backup --to "$BKROOT" 2>&1 | tr -d '\r')
  echo "$UO" | grep -q 'BACKUP_INCOMPLETE' && ok "不可读目录被如实报告（BACKUP_INCOMPLETE ✓）" || bad "静默少备份 ✗✗"
  # ★★ 这条断言原来是：要求"不完整时**也**打印 BACKUP_OK" ✗ —— 那正是后来**故意修掉**的行为 ✓✓
  #   · 起因：GUI 只看 `BACKUP_OK` ✗ → 部分失败的备份被 GUI 报成**成功** ✗
  #   · 修法：**不完整就不报 OK** ✓（现在只打 `BACKUP_INCOMPLETE <包路径> …` ✓）
  #   · 而**包路径没丢** ✓ —— 它就跟在 `BACKUP_INCOMPLETE` **同一行**上 ✓✓
  #   → 所以断言改成检查"不完整标记**带包路径**" ✓ 测的是**新的、更诚实**的行为 ✓
  echo "$UO" | grep -qE 'BACKUP_INCOMPLETE +[^ ]*dsh-data-' && ok "不完整标记**带包路径**（部分成功 ✓）" || bad "未给路径 ✗"
  chmod 755 "$U/secret" 2>/dev/null
else
  echo "  [SKIP] 以 root 运行 → 不可读目录测不了（权限对 root 无效 ✓）"
fi

# ---- 8 多工作区：ws=A;B → 新格式 + 恢复闭环 ----
W1="$WORK/w1"; W2="$WORK/w2"; mkdir -p "$W1" "$W2"
printf 'W1\n' > "$W1/one.txt"; printf 'W2\n' > "$W2/two.txt"
DSH_HOME="$A" $T $CLI config-set ws "$W1;$W2" >/dev/null 2>&1
MO=$(DSH_HOME="$A" $T $CLI backup --to "$BKROOT" 2>&1 | tr -d '\r'); MP=$(echo "$MO" | awk '/BACKUP_OK/{print $2}')
N=$(find "$MP/_workspace" -name '.dshws' 2>/dev/null | wc -l)
[ "$N" -eq 2 ] && ok "多工作区：两个 .dshws 标记 ✓" || bad "多工作区标记数 $N ≠ 2 ✗"
FLAT=$(find "$MP/_workspace" -maxdepth 1 -type f 2>/dev/null | wc -l)
[ "$FLAT" -eq 0 ] && ok "包内只有一种格式（无扁平残留 ✓）" || bad "包内混格式（顶层文件 $FLAT）✗✗"
# 恢复目标必须在**产品接受的位置** ✓ —— /tmp 被产品排除（#19 ✓ 我第一次就踩了这个 ✗）
# 用 $HOME 下的普通子目录 ✓（第 75 轮实测：$HOME 的子目录**被接受** ✓）
MT="$HOME/vbc-tg-$$"; rm -rf "$MT"; mkdir -p "$MT"
MR=$(cd "$MT" && DSH_HOME="$WORK/md" $T $CLI restore --path "$MP" --apply --yes 2>&1 | tr -d '\r')
echo "$MR" | grep -q 'UNRECOGNIZED' && bad "恢复报 UNRECOGNIZED ✗" || ok "恢复无 UNRECOGNIZED ✓"
[ -f "$MT/one.txt" ] && [ -f "$MT/two.txt" ] && ok "多工作区内容都恢复 ✓✓" || bad "多工作区恢复不全 ✗"
DSH_HOME="$A" $T $CLI config-set ws "" >/dev/null 2>&1

echo ""
echo "== 结果：PASS=$pass FAIL=$fail =="
# ★★★ 批次 E5（2026-10-07 外部审查建议 §2.1）：**项数下限**断言 ✓✓
#   为什么：`[ "$fail" -eq 0 ]` 只能抓"检查跑了但红了" ✗ ——
#     而**删除一条检查**（或整段被 `exit` 提前截断）时 PASS 变少、FAIL 仍是 0 ✓
#     → 脚本照样报"通过" ✗✗（这正是本脚本里 chain 类脚本出过的"假绿"形态 ✓）
#   做法与 `verify_failure_paths.sh:127-131` 完全一致 ✓（同一句话、同一退出语义 ✓）
#   取值说明（**下限不是目标** ✓）：静态清点 `ok` 调用点 = **140 行外 14 项 + 第 7 段 2 项** ✓
#     · 非 root（真机/CI 的常态）→ 16 项 ✓
#     · root → 第 7 段（不可读目录诚实性）整段 SKIP ✓ → **14 项** ✓
#     → 取 12：给 root 与将来的小改动留 2 项余量 ✓ 同时仍能抓到"一批检查一起消失" ✗✓
#     （说明：本脚本在 Windows 的 Git Bash 上**不可用** —— 依赖 `timeout`/`ss`/可执行位 ✓
#       所以这里只能静态清点 + 交给 ubuntu 上的 CI 真跑 ✓ 不假装本地验过 ✗）
EXPECTED_MIN=12
echo "== 结果：PASS=$pass FAIL=$fail（声明最少 $EXPECTED_MIN 项）=="
if [ "$pass" -lt "$EXPECTED_MIN" ]; then
  echo "  [FAIL] 项数不足：实跑 $pass < 声明 $EXPECTED_MIN（有检查被删掉或没执行到）"
  fail=$((fail+1))
fi
[ "$fail" -eq 0 ] && exit 0 || exit 1