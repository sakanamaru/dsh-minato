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
#    修复（读 `LinuxPaths.RealPath` 末尾注释核对过 ✓ 不是猜 ✗）：用 `QuoteArg`
#      引号化，保证"一个参数就是一个 argv 元素"✓；**不用** `ProcessStartInfo.ArgumentList`
#      ✗ —— 那会让源码无法被 .NET Framework 的 csc 编译（`verify_restore_apply.ps1` 走 csc ✓）
#  这个脚本就是那条路的一次性复现：在**隔离根**里用**含空格的 DSH_HOME** 跑
#    `status` / `backup --to` / `backup-list --verify` / `backup-dir`
#    → 断言**退出码 0 + 标记行正常** ✓（Windows 宿主上写不出 Linux 专属断言 ✓ 故独立成脚本 ✓）
#  用法： bash linux_paths_check.sh /path/to/dsh-minato
#  退出码：0 = 全部通过；1 = 有失败；2 = 用法错误
#  安全：全程在 /tmp 的一次性隔离根里 ✓ 且 `HOME` / `XDG_DATA_HOME` / `XDG_CACHE_HOME` /
#        `npm_config_cache` / `TMPDIR` **全部**指向它 ✓（否则会写客人真机 `~/.dsh`、
#        `~/.local/share/…` ✗ 已有人踩过 ✓）；**绝不碰**客人真实 `~/.dsh`、默认端口 3080
#        与真实备份 ✗；结束即清理 ✓
#  ---------------------------------------------------------------------------
#  ★★★ CI 假红修复（2026-10-07，run 37590614090 / job `V3 Linux package (linux-x64)`）✓✓
#   症状（CI 原文）：`  [FAIL] 标记 files= 不是 1：files=964`
#   根因 = **脚本对环境的隐含假设** ✗（不是产品缺陷 ✓ 实测定位见下）：
#     Linux 的"工作区"自动探测结果**就是当前目录** ✓（`LinuxPaths.WorkspaceRoot` ✓）
#     → CI 的 cwd 是**仓库检出**（GitHub 默认在 `/home/runner/work/<repo>/<repo>` ✓
#       运行器家目录 `/home/runner` 可由 CI 日志的 doctor 行直接读到 ✓ 检出目录按官方约定 ✓）
#     → `backup` 会把**整个检出**打包进包里的 `_workspace/` ✓（设计如此 ✓ 不是 bug ✓）
#     → 完成标记因此写 `files=964` = 1 个数据根文件 + 963 个工作区文件 ✓（真机复现一致 ✓）
#     → 本脚本却断言 `files=1` ✗ → **假红** ✓
#     为什么当年真机 13/13 绿 ✗：那次 cwd 在 `/tmp/…` 下 ✓ 而 `/tmp` 属于
#       `ForbiddenWorkspaceRoots` ✓ → 工作区被拒 → 包内只有数据根 ✓ → `files=1` ✓
#       （= 同一个脚本换了个 cwd 就从绿变红 ✓ 这就是"环境假设" ✓）
#   ✓ 修复方式：从**根上**让 cwd 不再影响包内容 ✓（`cd "$HOME"` ✓ 见下），
#     **没有**放宽任何断言 ✗（`files=1` 仍是 `files=1` ✓），并新增一条**显式前提断言**：
#     "包里不许出现 `_workspace`" ✓ → 环境再变也能一眼看出是隔离失效 ✓（而不是又猜一轮 ✗）
# ============================================================================
set -u
BIN="${1:-./dsh-minato}"
# ★ 先把 BIN **绝对化** ✓✓：下面会把 cwd 切进隔离根 ✓，而 CI 传的是**相对路径**
#   （`bash v3/tools/linux_paths_check.sh out/sc/dsh-minato` ✓）→ 不绝对化会立刻假红 ✗
if [ -x "$BIN" ]; then
  BIN="$(cd "$(dirname "$BIN")" && pwd)/$(basename "$BIN")"
fi
if [ ! -x "$BIN" ]; then echo "用法: bash linux_paths_check.sh /path/to/dsh-minato"; exit 2; fi

ROOT="$(mktemp -d /tmp/dsht-pathcheck.XXXXXX)"
T="timeout 20"
pass=0; fail=0
ok(){ pass=$((pass+1)); echo "  [PASS] $1"; }
bad(){ fail=$((fail+1)); echo "  [FAIL] $1"; }
# 断言一条标记行：$1=名称 $2=输出 $3=正则（失败时把实际输出前 3 行打出来 ✓ 便于定位 ✓）
want(){ if printf '%s\n' "$2" | grep -qE "$3"; then ok "$1"; else bad "$1（未匹配 /$3/ ; 实际: $(printf '%s\n' "$2" | tr -d '\r' | head -3 | tr '\n' '|')）"; fi }
rc0(){ if [ "$2" -eq 0 ]; then ok "$1"; else bad "$1（退出码 $2）"; fi }
# 清理前先离开隔离根 ✓（cwd 就在里面 ✓ 免得"在自家目录里拆自家房子" ✗）
cleanup(){ cd / 2>/dev/null; rm -rf "$ROOT" 2>/dev/null; }
trap cleanup EXIT

# ★ 所有可能落盘的"家目录"类路径都指到隔离根（绝不写客人真机 ✗）
export HOME="$ROOT/home"        # ★ 新增 ✓：`~/.dsh` 默认数据根、npm 家缓存等都挂在它下面 ✓
export XDG_DATA_HOME="$ROOT/xdg-data"
export XDG_CACHE_HOME="$ROOT/xdg-cache"
export npm_config_cache="$ROOT/npm-cache"
export TMPDIR="$ROOT/tmp"       # 自哈希缓存走 Path.GetTempPath() → 也关在隔离根里 ✓
mkdir -p "$HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$npm_config_cache" "$TMPDIR"

# ★★★ **cwd 隔离**（CI 假红的根因修复 ✓✓）：所有 CLI 调用都从**隔离家目录**里跑 ✓
#   ✗ 原来直接沿用调用者的 cwd ✗ → 工作区自动探测 = cwd ✓ → CI 上就是仓库检出 ✗
#     → `backup` 把整棵检出打进 `_workspace/` ✗ → 标记 `files=964` ✗（实测复现 ✓）
#   ✓ 现在这个 cwd 被**两道独立的产品规则**判为"不是工作区" ✓✓：
#     (1) cwd == $HOME ✓ —— `LinuxPaths.WorkspaceRoot` 明确"家目录本身不是工作区（否则会备份一切）"✓
#     (2) cwd 在 /tmp 下 ✓ —— 命中 `ForbiddenWorkspaceRoots` ✓
#   → 包里**不会**有 `_workspace/` ✓ → `files=` 只反映数据根 ✓ → 断言恢复为**确定值** ✓
#   ⚠ 这也让本脚本不再把 CI 的整棵检出拷进 /tmp ✓（更快、更省盘 ✓ 只测"路径引号"这一件事 ✓）
#   ⚠ 有意**不依赖** `config-set ws <不存在的路径>` ✗：真机实测那条路会
#     `CONFIGSET_FAIL 写入未生效` ✓（HEAD 上如此 ✓ 与本脚本无关 ✗ 故不用它 ✓）
cd "$HOME" || { echo "  [FAIL] 无法进入隔离家目录（$HOME）—— 隔离失败，拒绝在未知 cwd 下继续 ✗"; exit 9; }

# 粘性备份根是**假证据**来源之一 ✗（`.backup-dir` 优先于 `DSH_MINATO_BACKUP_DIR` ✗）
# → 显式清掉环境变量 ✓，且 StateDir 在全新 mktemp 根里 ✓ 不可能有粘性 `.backup-dir` ✓
unset DSH_MINATO_BACKUP_DIR 2>/dev/null || true
STATEDIR="$XDG_DATA_HOME/DeepSeekHarnessLauncher"

# ★★ L4 的触发条件：数据根**含空格**；里面的子条目也含空格（备份内容本身就走含空格路径 ✓）
export DSH_HOME="$ROOT/my dsh"
mkdir -p "$DSH_HOME/a b"
printf 'ALPHA\n' > "$DSH_HOME/a b/note.txt"
BK="$ROOT/bk root"          # 备份根**也含空格** ✓（一次把两处都试到 ✓）
mkdir -p "$BK"

echo "===== dsh-minato Linux 路径引号回归（L4）====="
echo "--- 0 环境"; uname -sr
echo "  BIN=$BIN"
echo "  cwd=$(pwd)（隔离家目录 ✓ 工作区探测不会命中检出 ✓）"
echo "  HOME=$HOME"
echo "  DSH_HOME=$DSH_HOME"
echo "  备份根=$BK"
echo "  StateDir=$STATEDIR（全新 ✓ 粘性 .backup-dir=$([ -e "$STATEDIR/.backup-dir" ] && echo 有✗ || echo 无✓)）"

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
# ★ 显式前提断言（新增 ✓）：包里**不许**有 `_workspace` ✓
#   → 它在就等于"cwd 被当成了工作区"✗ → `files=` 里会多出整棵检出的文件数 ✗（CI 就死在这里 ✓）
#   → 把它单列成一条 ✓ 下次环境一变，红的是**原因**而不是只有"files 不是 1"这句症状 ✓
if [ -n "$P" ] && [ ! -d "$P/_workspace" ]; then
  ok "包里没有 _workspace（cwd 未被当成工作区 ✓ 隔离前提成立 ✓）"
else
  bad "包里出现了 _workspace ✗（cwd 被当成了工作区 → 环境隔离失效 ✓ 工作区贡献 $(find "$P/_workspace" -type f 2>/dev/null | wc -l) 个文件）"
fi
if [ -n "$P" ] && grep -q '^files=1$' "$P.manifest" 2>/dev/null; then
  ok "标记 files=1（只该有这一个文件 ✓）"
else
  # 失败时把 files= 的**来源**一起打出来 ✓（不然下一个人还得猜一轮 ✗ —— 本脚本就是这么踩的 ✗）
  bad "标记 files= 不是 1：$(grep -m1 '^files=' "$P.manifest" 2>/dev/null || echo '<无标记>')（包内实际 $(find "$P" -type f 2>/dev/null | wc -l) 个文件；_workspace/ 贡献 $(find "$P/_workspace" -type f 2>/dev/null | wc -l) 个 ✗ 非 0 即『cwd 被当成工作区』✓）"
fi

echo "--- 3 backup-list --verify（同一个隔离根）"
C=$($T "$BIN" backup-list --verify 2>&1); RC=$?
printf '%s\n' "$C" | head -4
rc0 "backup-list --verify 退出码 0" "$RC"
want "--verify 判 complete" "$C" '^BACKUP_VERIFY .* complete '
want "TOTAL 汇总 complete=1（无 incomplete / mismatch）" "$C" '^BACKUP_VERIFY_TOTAL 1 complete=1 incomplete=0 mismatch=0'

echo "--- 4 backup-dir（含空格路径照原样显示 ✓ 不吞空格 ✗）"
D=$($T "$BIN" backup-dir 2>&1); RC=$?
printf '%s\n' "$D" | head -3
# 证据（不额外计项 ✓）：`backup --to` 应把选择**持久化**到隔离 StateDir 的 `.backup-dir` ✓
STICKY="$(cat "$STATEDIR/.backup-dir" 2>/dev/null || true)"
echo "  粘性 .backup-dir=${STICKY:-<无>}"
rc0 "backup-dir 退出码 0" "$RC"
# ⚠ 只取**标记行**（不是 head -1 ✗）：未随发布包分发的裸二进制会先打一条 INTEGRITY_SKIPPED 横幅 ✓
DIRLINE=$(printf '%s\n' "$D" | tr -d '\r' | grep -m1 '^BACKUP_DIR ')
if [ "$DIRLINE" = "BACKUP_DIR $BK" ]; then ok "BACKUP_DIR 打印的就是那个含空格的根（空格没被吞 ✗）"; else bad "BACKUP_DIR 不等于设定值（实际: ${DIRLINE:-<无标记行>}）"; fi

echo "===== 结果：$pass 通过 / $fail 失败 ====="
# ★ 项数下限断言 ✓✓（与 `verify_failure_paths.sh:127-131`、`linux_smoke.sh:103` 同一手法 ✓）
#   → 否则"有人删掉一条检查"脚本仍报绿但**覆盖面变小** ✗
#   本脚本的断言**全部无条件执行** ✓（每个分支都恰好产出一项 ✓）→ 下限就是**实数** 14 ✓
EXPECTED_MIN=14
if [ "$pass" -lt "$EXPECTED_MIN" ]; then
  echo "  [FAIL] 项数不足：实跑 $pass < 声明 $EXPECTED_MIN（有检查被删掉或没执行到）"
  fail=$((fail+1))
fi
echo "===== 结果：$pass 通过 / $fail 失败（声明最少 $EXPECTED_MIN 项）====="
[ "$fail" -eq 0 ] || exit 1
exit 0
