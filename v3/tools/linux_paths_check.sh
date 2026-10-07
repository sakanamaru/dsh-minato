#!/usr/bin/env bash
# ============================================================================
#  linux_paths_check.sh —— Linux 路径**引号**回归（L4：DSH_HOME 含空格）
#  ---------------------------------------------------------------------------
#  为什么有它：真机实测（2026-10-07）`DSH_HOME="/tmp/l4/my dsh"` 时
#      `backup --to …` 报 **BACKUP_FAIL** ✗（数据根含空格 → 整个命令面走错路径 ✗）
#    根因（读码定位，不是照抄行号）：`LinuxPaths.RealPath` 执行的是
#      `new ProcessStartInfo("readlink", "-f " + p)` —— **路径没有引号** ✗
#      → .NET 按空白把 Arguments 切成 argv → readlink 收到**两个**操作数
#      → 输出两行（`/tmp/l4/my` 与 `dsh`）→ 拼出来的"真实路径"里**带换行符** ✗
#      → 它就成了 DataRoot → 之后每个命令都在一个错误路径上工作 ✗
#    修复：改用 `ProcessStartInfo.ArgumentList` 逐个传 argv（等价于引号化 ✓
#      但**没有引号/转义陷阱** ✓）；同文件里其它命令拼接已核过（见修复记录 ✓）
#  这个脚本就是那条路的一次性复现：在**隔离根**里用**含空格的 DSH_HOME** 跑
#    `status` / `backup --to` / `backup-list --verify` / `backup-dir`
#    → 断言**退出码 0 + 标记行正常** ✓（Windows 宿主上写不出 Linux 专属断言 ✓ 故独立成脚本 ✓）
#  用法： bash linux_paths_check.sh /path/to/dsh-minato
#  退出码：0 = 全部通过；1 = 有失败；2 = 用法错误
#  安全：全程在 /tmp 的一次性隔离根里 ✓ 且 `XDG_DATA_HOME` / `XDG_CACHE_HOME` /
#        `npm_config_cache` **全部**指向它 ✓（否则会写客人真机 `~/.local/share/…` ✗
#        已有人踩过 ✓）；**绝不碰**客人真实 `~/.dsh`、默认端口 3080 与真实备份 ✗；结束即清理 ✓
# ============================================================================
set -u
BIN="${1:-./dsh-minato}"
if [ ! -x "$BIN" ]; then echo "用法: bash linux_paths_check.sh /path/to/dsh-minato"; exit 2; fi

ROOT="$(mktemp -d /tmp/dsht-pathcheck.XXXXXX)"
T="timeout 20"
pass=0; fail=0
ok(){ pass=$((pass+1)); echo "  [PASS] $1"; }
bad(){ fail=$((fail+1)); echo "  [FAIL] $1"; }
# 断言一条标记行：$1=名称 $2=输出 $3=正则（失败时把实际输出前 3 行打出来 ✓ 便于定位 ✓）
want(){ if printf '%s\n' "$2" | grep -qE "$3"; then ok "$1"; else bad "$1（未匹配 /$3/ ; 实际: $(printf '%s\n' "$2" | tr -d '\r' | head -3 | tr '\n' '|')）"; fi }
rc0(){ if [ "$2" -eq 0 ]; then ok "$1"; else bad "$1（退出码 $2）"; fi }
cleanup(){ rm -rf "$ROOT" 2>/dev/null; }
trap cleanup EXIT

# ★ 所有可能落盘的"家目录"类路径都指到隔离根（绝不写客人真机 ✗）
export XDG_DATA_HOME="$ROOT/xdg-data"
export XDG_CACHE_HOME="$ROOT/xdg-cache"
export npm_config_cache="$ROOT/npm-cache"
export TMPDIR="$ROOT/tmp"       # 自哈希缓存走 Path.GetTempPath() → 也关在隔离根里 ✓
mkdir -p "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$npm_config_cache" "$TMPDIR"

# ★★ L4 的触发条件：数据根**含空格**；里面的子条目也含空格（备份内容本身就走含空格路径 ✓）
export DSH_HOME="$ROOT/my dsh"
mkdir -p "$DSH_HOME/a b"
printf 'ALPHA\n' > "$DSH_HOME/a b/note.txt"
BK="$ROOT/bk root"          # 备份根**也含空格** ✓（一次把两处都试到 ✓）
mkdir -p "$BK"

echo "===== dsh-minato Linux 路径引号回归（L4）====="
echo "--- 0 环境"; uname -sr; echo "  BIN=$BIN"; echo "  DSH_HOME=$DSH_HOME"; echo "  备份根=$BK"

echo "--- 1 status（含空格 DSH_HOME → 只读面不许被路径拼错拖垮）"
A=$($T "$BIN" status 2>&1); RC=$?
printf '%s\n' "$A" | head -6
rc0 "status 退出码 0" "$RC"
want "status 打印 STATUS_ 标记行（没有静默/崩溃）" "$A" '^STATUS_[A-Z]+'

echo "--- 2 backup --to（含空格数据根 + 含空格目标目录）"
B=$($T "$BIN" backup --to "$BK" 2>&1); RC=$?
printf '%s\n' "$B" | head -6
rc0 "backup 退出码 0（修复前这里是 1 且报 BACKUP_FAIL ✗）" "$RC"
want "backup 打印 BACKUP_OK" "$B" '^BACKUP_OK '
# ⚠ 取"BACKUP_OK 之后**整行剩余部分**"（不能用 awk '{print $2}' ✗ —— 备份根本身含空格 ✓ 会被截断 ✗）
P=$(printf '%s\n' "$B" | tr -d '\r' | sed -n 's/^BACKUP_OK //p' | head -1)
if [ -n "$P" ] && [ -d "$P" ]; then ok "备份包真的落在含空格的备份根里（$P）"; else bad "备份包目录不存在：${P:-<未打印 BACKUP_OK>}"; fi
if [ -n "$P" ] && [ -f "$P.manifest" ]; then ok "完成标记（.manifest）存在" ; else bad "完成标记缺失"; fi
# ★ 最硬的一条：数据根里的**含空格子条目**必须被原样备份进去
#   → 证明 DSH_HOME 被解析成了正确路径（解析错 → 备份的是别处或什么都没有 ✗）
if [ -n "$P" ] && [ -f "$P/a b/note.txt" ]; then ok "含空格子路径 'a b/note.txt' 被备份（DSH_HOME 解析正确 ✓）"; else bad "备份包内找不到 'a b/note.txt'（数据根解析错 ✗）"; fi
if [ -n "$P" ] && grep -q '^files=1$' "$P.manifest" 2>/dev/null; then ok "标记 files=1（只该有这一个文件 ✓）"; else bad "标记 files= 不是 1：$(grep -m1 '^files=' "$P.manifest" 2>/dev/null || echo '<无标记>')"; fi

echo "--- 3 backup-list --verify（同一个隔离根）"
C=$($T "$BIN" backup-list --verify 2>&1); RC=$?
printf '%s\n' "$C" | head -4
rc0 "backup-list --verify 退出码 0" "$RC"
want "--verify 判 complete" "$C" '^BACKUP_VERIFY .* complete '
want "TOTAL 汇总 complete=1（无 incomplete / mismatch）" "$C" '^BACKUP_VERIFY_TOTAL 1 complete=1 incomplete=0 mismatch=0'

echo "--- 4 backup-dir（含空格路径照原样显示 ✓ 不吞空格 ✗）"
D=$($T "$BIN" backup-dir 2>&1); RC=$?
printf '%s\n' "$D" | head -3
rc0 "backup-dir 退出码 0" "$RC"
# ⚠ 只取**标记行**（不是 head -1 ✗）：未随发布包分发的裸二进制会先打一条 INTEGRITY_SKIPPED 横幅 ✓
DIRLINE=$(printf '%s\n' "$D" | tr -d '\r' | grep -m1 '^BACKUP_DIR ')
if [ "$DIRLINE" = "BACKUP_DIR $BK" ]; then ok "BACKUP_DIR 打印的就是那个含空格的根（空格没被吞 ✗）"; else bad "BACKUP_DIR 不等于设定值（实际: ${DIRLINE:-<无标记行>}）"; fi

echo "===== 结果：$pass 通过 / $fail 失败 ====="
[ "$fail" -eq 0 ] || exit 1
exit 0
