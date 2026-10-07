#!/bin/sh
# dsh-minato Linux 安装脚本
#
# 设计原则（与 Windows 安装器**完全一致** ✓）：
#   · **纯 POSIX sh** ✓ 零第三方依赖 ✓ 不需要 sudo ✓（装到用户目录 ✓）
#   · **绝不动用户数据** ✓ —— ~/.dsh 是 dsh 自己的 ✓ 本脚本只读它的位置并在卸载时告知 ✓
#   · **幂等** ✓ —— 重复运行不会重复添加、不会报错 ✓
#   · **逐项报告** ✓ —— 每一步都打印做了什么 ✓ 不静默 ✓
#   · **只删自己加的** ✓ —— 卸载逐项列出 ✓ 删不干净**如实报告** ✗ 不假报干净 ✓
#
# 用法：
#   ./install.sh                    安装
#   ./install.sh --uninstall        卸载（**默认不删数据** ✓ 会在桌面留一份说明）
#   ./install.sh --prefix DIR       自定义安装位置（也支持 --prefix=DIR ✓）
#   ./install.sh --no-desktop       不写 .desktop 菜单项
#   ./install.sh --force            允许装进非空目录（**危险** ✓ 默认拒绝 ✓）
#
# 环境变量（可选）：
#   DSH_MINATO_PREFIX   安装位置（默认 ~/.local/share/dsh-minato）
#   DSH_MINATO_BINDIR   命令链接位置（默认 ~/.local/bin）

set -eu

APP="dsh-minato"
MARKER=".dsh-minato-install"
SRC_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PREFIX="${DSH_MINATO_PREFIX:-$HOME/.local/share/$APP}"
BINDIR="${DSH_MINATO_BINDIR:-$HOME/.local/bin}"
DESKTOP_DIR="$HOME/.local/share/applications"
WRITE_DESKTOP=1
DO_UNINSTALL=0
FORCE=0
STAGING=""

say()  { printf '%s\n' "$*"; }
ok()   { printf '  ✓ %s\n' "$*"; }
warn() { printf '  ! %s\n' "$*"; }
die()  { printf '  ✗ %s\n' "$*" >&2; exit 1; }

# 退出时清理暂存 ✓（F9：中断/失败不再留半份副本 ✓ 幂等 ✓）
cleanup() { [ -n "$STAGING" ] && [ -d "$STAGING" ] && rm -rf "$STAGING" 2>/dev/null || true; }
trap cleanup EXIT INT TERM

# ---- 参数 ----
while [ $# -gt 0 ]; do
    case "$1" in
        --uninstall) DO_UNINSTALL=1 ;;
        --no-desktop) WRITE_DESKTOP=0 ;;
        --force) FORCE=1 ;;
        --prefix)
            # ✓ F10：缺值要**报错** ✓ 不能把下一个开关当成目录 ✗
            [ $# -ge 2 ] || die "--prefix 后面要跟一个目录"
            case "$2" in -*) die "--prefix 的值看起来是另一个开关：$2" ;; esac
            PREFIX="$2"; shift ;;
        --prefix=*)
            # S7 FIX (Linux audit MINOR): an empty value fell through to $(pwd), so running
            # from an empty directory installed into it. The guard below never fired because
            # the value was no longer empty by then.
            PREFIX="${1#--prefix=}"
            [ -n "$PREFIX" ] || die "--prefix= 后面要跟一个目录 ✓（空值会装到当前目录 ✗ 已拒绝 ✓）" ;;
        --help|-h) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;   # S11 FIX: the usage comment ends at line 20; 24 also printed set -eu and APP="
        *) die "未知参数：$1（用 --help 看用法）" ;;   # ✓ 未知参数**不能静默忽略** ✓
    esac
    shift
done

# ✓ F5：**规范化路径** ✓（相对路径会让符号链接悬空 ✗ 原来的坑 ✓）
# NB3 FIX (Linux audit MAJOR): this check used to run AFTER the case below, and that case turns
# an empty value into "$(pwd)/" - so `--prefix ""` was no longer empty and installed into the
# current directory (the audit reproduced it, and the directory was renamed to .old.<pid>).
[ -n "$PREFIX" ] || die "安装位置不能为空 ✓（--prefix \"\" 会装到当前目录 ✗ 已拒绝 ✓）"
case "$PREFIX" in
    /*) ;;
    *) PREFIX="$(pwd)/$PREFIX" ;;
esac
# N5 FIX (Linux audit MINOR): S7 guarded only --prefix=; --prefix "" fell through to $(pwd)
# and installed into the current directory. The guard must run after every parse path.
[ -n "$PREFIX" ] || die "安装位置不能为空 ✓（--prefix= 空值会装到当前目录 ✗ 已拒绝 ✓）"
PREFIX=$(printf '%s' "$PREFIX" | sed 's:/*$::')      # 去掉尾部斜杠 ✓
# ★★★ **NB8 修复（最终复审 MINOR —— `.` / `..` 段被原样记进标记）** ✓✓
#   ✗ 原来只做"变绝对 + 去尾斜杠" ✗ → `--prefix sub/../pfx`（`sub` 不存在时）**原样记录** ✗
#     → 标记里写 `/cwd/sub/../pfx` ✓ 而真实目录是 `/cwd/pfx` ✓ → 下次卸载对不上 ✓
#     → 而且 `mkdir -p` 会**顺手建出一个空的 `sub`** ✗（审计实测 ✓）
#   ✓ 现在：**词法规范化** ✓✓（纯字符串 ✓ 不碰磁盘 ✓ 段内 `..` 就地消掉 ✓ 不建多余目录 ✓）
_normpath() {
    _in=$1; _out=""
    _oldifs=$IFS; IFS=/
    for _seg in $_in; do
        case "$_seg" in
            ''|.) continue ;;
            ..) _out=$(printf '%s' "$_out" | sed 's|/[^/]*$||') ;;
            *) _out="$_out/$_seg" ;;
        esac
    done
    IFS=$_oldifs
    [ -n "$_out" ] || _out="/"
    printf '%s' "$_out"
}
PREFIX=$(_normpath "$PREFIX")
# S4 FIX (Linux audit MINOR): the path was only made absolute and stripped of trailing
# slashes, so "sub/../pfx" and "pfx" recorded different strings in the marker and the
# uninstall then refused - a permanent lockout (--force could not rescue it before S3).
if [ -d "$PREFIX" ]; then
    _canon=$( cd -- "$PREFIX" 2>/dev/null && pwd -P ) || _canon=""
    if [ -n "$_canon" ]; then PREFIX="$_canon"; fi
else
    # N4 FIX (Linux audit MINOR): a first install through a symlinked parent recorded the
    # LOGICAL path; a later uninstall canonicalised to the real one and refused - a permanent
    # lockout. Canonicalise the parent even when the leaf does not exist yet.
    _pdir=$(dirname -- "$PREFIX"); _pbase=$(basename -- "$PREFIX")
    _pcanon=$( cd -- "$_pdir" 2>/dev/null && pwd -P ) || _pcanon=""
    if [ -n "$_pcanon" ]; then PREFIX="$_pcanon/$_pbase"; fi
fi
[ -n "$PREFIX" ] || die "安装位置为空"
# S5 FIX (Linux audit MINOR): the marker records path= as one line, so a newline in the
# prefix truncated it on read-back and the install could never be uninstalled.
_pnl=$(printf '%s' "$PREFIX" | wc -l | tr -d ' ')
[ "$_pnl" = "0" ] || die "安装位置里不能有换行符 ✓（标记按行记录路径 ✗ 会永远卸载不掉 ✓）"
case "$BINDIR" in /*) ;; *) BINDIR="$(pwd)/$BINDIR" ;; esac
BINDIR=$(printf '%s' "$BINDIR" | sed 's:/*$::')

# ✓ F6：桌面目录用 xdg-user-dir ✓ 再回退多候选 ✓（原来写死 ~/Desktop ✗ 中文系统会写错地方 ✓）
desktop_dir() {
    d=""
    if command -v xdg-user-dir >/dev/null 2>&1; then d=$(xdg-user-dir DESKTOP 2>/dev/null || true); fi
    [ -n "$d" ] && [ -d "$d" ] && { printf '%s' "$d"; return; }
    for c in "$HOME/Desktop" "$HOME/桌面" "$HOME/デスクトップ"; do
        [ -d "$c" ] && { printf '%s' "$c"; return; }
    done
    printf '%s' "$HOME"
}

# ================================================================ 卸载

if [ "$DO_UNINSTALL" -eq 1 ]; then
    say "== 卸载 $APP =="
    # ★★ F1 修复：**身份校验不能只看文件名** ✗✗
    #   ✗ 原来只判"存在一个叫 dsh-minato 的文件" ✓ → **任何含该文件名的目录都会被 rm -rf** ✗✗
    #     （审计实测：`important.txt` 与 `photos/` 全没了 ✓ 与 Windows 侧 C1 同类 ✓）
    #   ✓ 现在要求**安装器写下的标记文件** ✓（含路径 ✓ 不匹配即拒绝 ✓）
    if [ ! -f "$PREFIX/$MARKER" ]; then
        die "拒绝卸载：$PREFIX 里没有安装标记 $MARKER → 它不像安装目录 ✓ **一个字节都不删** ✓"
    fi
    # ★★ m3 修复（审计）✓✓
    #   ✗ 原来 `[ -n "$recorded" ] && [ "$recorded" != "$PREFIX" ]` ✗
    #     → **没有 path= 行时直接放行** ✓ → 外来目录 + 一行 marker → **被接受** ✓
    #     → **然后删掉它的 gui/data.txt** ✗✗（审计实测 ✓）
    #   ✓ 现在：**没有 path= 行 → 拒绝** ✓（我们自己的安装器**一定**会写 path= ✓）
    recorded=$(sed -n 's/^path=//p' "$PREFIX/$MARKER" 2>/dev/null | head -1)
    if [ -z "$recorded" ]; then
        die "拒绝卸载：安装标记里**没有记录安装位置** ✓ → 无法确认这个目录是我们装的 ✓ **一个字节都不删** ✓"
    fi
    if [ "$recorded" != "$PREFIX" ]; then
        # S3 FIX (Linux audit MAJOR): --force was only ever read by the install path, so a
        # directory that had been moved or renamed could never be uninstalled - a permanent
        # lockout, with the launcher left dangling. --force now overrides the location check,
        # loudly, and only that check: the marker and the file list are still required.
        if [ "$FORCE" -eq 1 ]; then
            warn "**--force：跳过安装位置校验** ✓ 标记里写的是「$recorded」，本次是「$PREFIX」✓ 仍然只删清单里的文件 ✓"
        else
            die "拒绝卸载：标记里记录的安装位置是「$recorded」，与本次的「$PREFIX」不一致 ✓ **一个字节都不删** ✓
  如果你确实移动过安装目录，可以用 --force 强制卸载 ✓（仍然只删清单里的文件 ✓）"
        fi
    fi

    # 符号链接：**只删指向我们的** ✓（F7：用户自己的链接不能动 ✗）
    if [ -L "$BINDIR/$APP" ]; then
        tgt=$(readlink "$BINDIR/$APP" 2>/dev/null || true)
        case "$tgt" in
            # N3 FIX (Linux audit MINOR): under --force the prefix in the marker is the OLD
            # location, so a link pointing at it was treated as "not ours" and left dangling.
            "$PREFIX"/*) rm -f "$BINDIR/$APP" && ok "已删命令链接 $BINDIR/$APP" ;;
            "$recorded"/*) rm -f "$BINDIR/$APP" && ok "已删命令链接 $BINDIR/$APP（指向标记里的旧位置 ✓ --force ✓）" ;;
            *) warn "跳过 $BINDIR/$APP：它指向 $tgt ✓ 不是我们建的 ✓ 不动它 ✓" ;;
        esac
    fi
    # 菜单项：**只删内容是我们的** ✓（F7）
        # S10 FIX (Linux audit MINOR): an unescaped prefix is a basic regex, so a '[' in the
    # path made grep fail and the stale menu entry was never removed. -F is literal.
    if [ -f "$DESKTOP_DIR/$APP.desktop" ] && { grep -qF "$PREFIX" "$DESKTOP_DIR/$APP.desktop" 2>/dev/null || { [ -n "$recorded" ] && grep -qF "$recorded" "$DESKTOP_DIR/$APP.desktop" 2>/dev/null; }; }; then   # N3 FIX: also accept the recorded path under --force
        rm -f "$DESKTOP_DIR/$APP.desktop" && ok "已删菜单项"
    fi
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true

    # ★★ F1 的另一半：**只删我们自己的** ✓✓（不再 rm -rf 整棵树 ✗）
    if [ -d "$PREFIX" ]; then
        removed=0; kept=0
        # ① **优先用安装时记录的文件清单** ✓✓（它才是完整的 ✓ 包内 hashes.txt 可能只覆盖几个 ✓）
        LIST=""
        [ -s "$PREFIX/.dsh-minato-files" ] && LIST="$PREFIX/.dsh-minato-files"   # N8 FIX: -s, so an empty list falls back to hashes.txt
        if [ -n "$LIST" ]; then
            while read -r name; do
                [ -n "${name:-}" ] || continue
                # NB7 FIX (Linux audit MINOR, reproduced): the reinstall path already refused
                # absolute paths and "..", but this loop did not - an edited manifest line could
                # delete a file outside the prefix. Same rule here.
                case "$name" in /*|*..*) warn "**跳过可疑清单条目**（越界 ✓ 不删 ✓）: $name"; continue ;; esac
                f="$PREFIX/$name"
                if [ -f "$f" ]; then rm -f "$f" 2>/dev/null && removed=$((removed+1)) || kept=$((kept+1)); fi
            done < "$LIST"
        elif [ -f "$PREFIX/hashes.txt" ]; then
            # 回退：老版本装的没有记录清单 ✓ 用包内 hashes.txt ✓（可能不完整 ✓ 如实说明 ✓）
            warn "没有安装文件清单（老版本装的 ✓）→ 回退用包内 hashes.txt ✓ 可能不完整 ✓"
            while read -r _h name; do
                [ -n "${name:-}" ] || continue
                case "$name" in /*|*..*) warn "**跳过可疑条目**（越界 ✓ 不删 ✓）: $name"; continue ;; esac   # NB7 FIX
                f="$PREFIX/$name"
                if [ -f "$f" ]; then rm -f "$f" 2>/dev/null && removed=$((removed+1)) || kept=$((kept+1)); fi
            done < "$PREFIX/hashes.txt"
        fi
        # ② 已知生成物 ✓
        for g in uninstall.exe "$MARKER" hashes.txt install.sh .dsh-minato-files; do
            [ -f "$PREFIX/$g" ] && { rm -f "$PREFIX/$g" 2>/dev/null && removed=$((removed+1)) || kept=$((kept+1)); }
        done
        # ★★★ **C1 修复（审计 CRITICAL —— 我重写时引入的）** ✓✓
        #   ✗✗ 原来 `for d in $PREFIX/$g` **没有引号** ✗ → **IFS 词分割** ✓
        #      → prefix 含空格时 `/home/u/My Apps/pfx/gui` 会分成 `/home/u/My` + `Apps/pfx/gui` ✓
        #      → **第一个是真实存在的目录 → rm -rf 递归删掉它** ✗✗
        #      → 审计实测：`My/victim-data/file.txt` 整个被删 ✓ **还报「你的数据没有被删除」** ✗✗
        #      → **与重写前的 F1 同类** ✓ —— 我修 F1 时引入了同类的另一个 ✓
        #   ✓ 现在：**路径全部加引号** ✓ 前缀用 `"$PREFIX"/` 形式 ✓（glob 仍展开 ✓ 前缀不被分割 ✓）
        # ★★ **M2 修复（审计）** ✓✓
        #   ✗ 原来整片 `rm -rf gui/bin/icons/app-*` ✗ → **用户放进这些目录的文件一起没** ✗
        #     （审计实测 gui/USER-NOTE.txt / icons/USER-ICON.txt / app-mine/USER-APP.txt 都被删 ✓）
        #     → 而我还宣称「只删清单里的」✗ **代码根本不是** ✓
        #   ✓ 现在：**只删空目录** ✓（文件已在上面按清单逐个删过 ✓）
        #     非空目录 = 里面还有**不属于清单的东西** → **保留** ✓ 由下面的 leftover 判定如实报告 ✓
        #   ✗ 注意：全局 `find -empty` 也会删**用户建的空目录** ✗（审计 m2 ✓）
        #     → 折中：**只清我们已知的那几个目录**下的空目录 ✓ 不碰用户自己建的顶层目录 ✓
        # ✗ 回归：`cli-small/` 不在上面那几个里 ✗ → 它的文件按清单删了 ✓ 但**空目录本身留下** ✗
        #   → 正常卸载后目录没删干净 ✓（实测确认 ✓）
        # ✓ 补上它 ✓（包里的顶层目录就是这几个 ✓ 其它目录一律不碰 ✓ 用户自己建的目录绝不动 ✓）
        for g in gui bin icons cli-small; do
            d="$PREFIX/$g"
            [ -d "$d" ] || continue
            find "$d" -depth -type d -empty -exec rmdir {} \; 2>/dev/null || true
        done
        for d in "$PREFIX"/app-*; do
            [ -d "$d" ] || continue
            find "$d" -depth -type d -empty -exec rmdir {} \; 2>/dev/null || true
        done
        ok "已删我们自己的 $removed 项"
        # ③ **目录空了才删目录** ✓ 还有别人的东西 → 保留 + 如实报告 ✓✓
        if [ -z "$(ls -A "$PREFIX" 2>/dev/null || true)" ]; then
            # ✓ 注意：上面的 `find -depth -empty` 可能**已经把目录删了** ✓ → 这里再删会失败 ✓
            #   所以**先看它还在不在** ✓ 免得日志自相矛盾（"没能删掉"但实际已删 ✗ 实测踩到 ✓）
            if [ -d "$PREFIX" ]; then
                rmdir "$PREFIX" 2>/dev/null && ok "已删安装目录（已空）" || warn "目录没能删掉：$PREFIX"
            else
                ok "已删安装目录（已空）"
            fi
        else
            # ✓ F8：删不干净**不假报干净** ✗
            warn "目录里**还有不属于本工具的文件** → 目录**保留** ✓：$PREFIX"
            kept=$((kept+1))
        fi
        [ "$kept" -gt 0 ] && warn "有 $kept 项没能删掉（可能是权限或被占用 ✓）" || true
    fi

    # **不删数据** ✓ 留一份说明 ✓
    DATA_DIR="$HOME/.dsh"
    NOTE="$(desktop_dir)/$APP-卸载说明.txt"
    {
        echo "$APP 已卸载。"
        echo ""
        echo "**你的数据没有被删除。**"
        echo ""
        echo "DeepSeek Harness 的会话、设置等数据在："
        echo "    $DATA_DIR"
        [ -d "$DATA_DIR" ] && echo "（这个目录现在还在 ✓）" || echo "（这个目录当前不存在）"
        echo ""
        echo "这些数据**不是 $APP 创建的** ✓ 是 DeepSeek Harness（dsh）自己的 ✓"
        echo "所以卸载 $APP **不会**、也**不应该**动它 ✓"
        echo ""
        echo "如果你确实想删掉这些数据："
        echo "  1. 先确认你不再需要这些会话记录（**删了无法恢复**）"
        echo "  2. 确认 dsh 本身也已卸载（否则它会重新生成）"
        echo "  3. 手动删除上面那个目录"
    } > "$NOTE" 2>/dev/null && ok "已写说明文档：$NOTE" || warn "写说明文档失败（不致命）"
    say "卸载完成 ✓（**你的数据没有被删除** ✓）"
    exit 0
fi

# ================================================================ 安装

say "== 安装 $APP =="
say "  源目录: $SRC_DIR"
say "  安装到: $PREFIX"
say "  命令链接: $BINDIR/$APP"

[ -f "$SRC_DIR/$APP" ] || die "找不到 $SRC_DIR/$APP —— 请在**解压后的包目录里**运行本脚本 ✓"
[ -x "$SRC_DIR/$APP" ] || chmod +x "$SRC_DIR/$APP" 2>/dev/null || true
[ -f "$SRC_DIR/gui/dsht-gui" ] || warn "包内没有 gui/dsht-gui（图形界面将不可用，CLI 仍可用）"

# ★★ F2 修复：目标**非空且不是我们的** → **拒绝** ✗✗
#   ✗ 原来 `rm -rf` 掉 --prefix 里已有的一切 ✓
#     （审计实测：`--prefix ~/.local/share` → `other-app/` 与 `other.desktop` **全没了** ✓ 还报成功 ✗✗）
#   ✓ 现在：非空 + 没有我们的标记 → 拒绝 ✓（要强装用 --force ✓）
if [ -d "$PREFIX" ] && [ -n "$(ls -A "$PREFIX" 2>/dev/null || true)" ] && [ ! -f "$PREFIX/$MARKER" ] && [ "$FORCE" -eq 0 ]; then
    die "拒绝安装：$PREFIX 非空且不是本工具的安装目录 ✓
  为了安全，安装器**不会**装进一个非空目录 —— 因为卸载时会删除安装目录里的文件 ✓
  请换一个空目录，或确认里面没有重要文件后用 --force 强制安装 ✓"
fi

mkdir -p "$(dirname -- "$PREFIX")" || die "建不了 $PREFIX 的父目录"
STAGING="$(dirname -- "$PREFIX")/.$APP.staging.$$"
rm -rf "$STAGING" 2>/dev/null || true
mkdir -p "$STAGING" || die "建不了暂存目录"

# ★★ F3 修复：**不再用 `tar | tar` 管道** ✗（左端失败会被忽略 → 文件静默缺失却报成功 ✗）
#   ✓ 现在用 `cp -R` ✓ 两侧都检查 ✓ 并核对文件数 ✓
say "  复制到暂存目录…"
if ! cp -R "$SRC_DIR/." "$STAGING/" 2>/dev/null; then
    die "复制失败（cp 出错）—— 检查源目录是否可读 ✓"
fi
# 去掉包内的大文件与校验和 ✓（它们不属于安装内容 ✓ 顺带避免 busybox tar 的 --exclude 问题 ✓ F16）
rm -f "$STAGING"/*.tar.gz "$STAGING"/*.tar.gz.sha256 "$STAGING"/*.zip "$STAGING"/*.sha256 2>/dev/null || true
src_n=$(find "$SRC_DIR" -type f 2>/dev/null | wc -l | tr -d ' ')
stg_n=$(find "$STAGING" -type f 2>/dev/null | wc -l | tr -d ' ')
ok "复制完成（暂存 $stg_n 个文件 ✓ 源 $src_n 个 ✓）"
if [ "$stg_n" -eq 0 ]; then die "复制后暂存目录是空的 ✗"; fi

chmod +x "$STAGING/$APP" 2>/dev/null || true
[ -f "$STAGING/gui/dsht-gui" ] && chmod +x "$STAGING/gui/dsht-gui" 2>/dev/null || true

# ---- 命令链接前置检查 (S1 FIX: must run before the install) ----
#   The audit found this check AFTER the landing move. Two consequences:
#     - a plain file at the target made the script exit 1 after the install was already done
#     - a foreign symlink there meant the old prefix had already been moved aside and removed,
#       taking any user files inside it, and a half-finished install was left behind
#   It now runs before anything is touched, so a refusal changes nothing on disk.
# ✗✗ F7：原来直接 `ln -sf` → **覆盖用户自己的符号链接** ✗
#   （审计实测：用户建的 `$BINDIR/dsh-minato -> /etc/hostname` 被装掉、然后被卸掉 ✓✗）
# ✓ 修：**安装时**就检查 ✓ —— 已存在且不指向我们 → **拒绝** ✓✓（卸载侧检查太晚 ✓）
if [ -L "$BINDIR/$APP" ]; then
    _tgt=$(readlink "$BINDIR/$APP" 2>/dev/null || true)
    case "$_tgt" in
        "$PREFIX"/*) : ;;   # 指向我们（重装 ✓）→ 可以覆盖 ✓
        *) die "拒绝：$BINDIR/$APP 已经指向另一个 dsh-minato 安装「$_tgt」✓ 它确实是我们建的 ✓ 只是**不是这一个 prefix** ✓（先卸载那一份，或改用同一个 --prefix ✓）
  它不是本工具建的 ✓ 为了不动别人的东西，请先自行处理它（或换 DSH_MINATO_BINDIR ✓）" ;;
    esac
elif [ -e "$BINDIR/$APP" ]; then
    die "$BINDIR/$APP 已存在且不是符号链接 ✓ 请先处理它 ✓"
fi
# ============================================================================
# 就位 + 旧版本处理  **顺序很关键** ✓✓
#   ★★★ **NB1/NB2 修复（最终复审 CRITICAL —— 我的上一版把顺序搞反了）** ✓✓
#     ✗ 上一版：改名 → **先从 OLD 搬回用户文件**（`mkdir -p` **创建了 `$PREFIX`** ✗）
#       → `mv "$STAGING" "$PREFIX"` **把整个载荷嵌套进 prefix** ✗✗
#         （`$PREFIX/dsh-minato` 不存在 ✓ 载荷在 `$PREFIX/.dsh-minato.staging.*/` ✓ 启动器悬空 ✓
#          而**日志还报"已安装到"** ✗ —— **零用户文件也会触发** ✓ 因为 `icons/hicolor` 让 `rmdir` 失败 ✓）
#     ✗ 而且清单是在**搬回之后**从 `$PREFIX` 生成的 ✗ → **用户文件被记成我们的** ✗✗
#       → **下一次卸载把它们 `rm -f` 掉** ✓（实测数据丢失 ✓）
#   ✓ 正确顺序：
#       ① **先从 `$STAGING` 生成清单** ✓（那时里面**只有我们的文件** ✓✓）
#       ② 改名旧 prefix 让开 ✓
#       ③ `mv "$STAGING" "$PREFIX"` ✓（此时 `$PREFIX` **还不存在** ✓ 不会嵌套 ✓）
#       ④ 从 `$OLD` 删掉清单里属于我们的文件 ✓
#       ⑤ **再把剩下的用户文件搬进新 prefix** ✓（这一步在清单之后 ✓ 不会被误记 ✓✓）
#       ⑥ 删掉空的 `$OLD` ✓（NB6 ✓）
# ============================================================================

# ① **先记清单** ✓✓（`$STAGING` 里只有我们的文件 ✓ 这是 NB2 的关键 ✓）
( cd "$STAGING" && find . -type f 2>/dev/null | sed 's|^\./||' ) > "$STAGING/.dsh-minato-files" 2>/dev/null \
    && ok "已记录安装文件清单（$(wc -l < "$STAGING/.dsh-minato-files" | tr -d ' ') 个文件 ✓ 从暂存目录生成 ✓ 不含用户文件 ✓）" \
    || warn "记录文件清单失败（卸载会更保守 ✓）"

# ②③ 改名旧的 + 新载荷就位（**此时 `$PREFIX` 不存在** ✓ 不会嵌套 ✓）
OLD=""
if [ -d "$PREFIX" ]; then
    OLD="$PREFIX.old.$$"
    rm -rf "$OLD" 2>/dev/null || true
    if ! mv "$PREFIX" "$OLD" 2>/dev/null; then
        warn "旧目录改名失败（可能有程序占用）→ 就地覆盖 ✓"
        OLD=""
    fi
fi
mv "$STAGING" "$PREFIX" || die "就位失败（暂存目录留在 $STAGING，可以手动看）"
STAGING=""     # 已就位 ✓ trap 不用再清理 ✓
ok "已安装到 $PREFIX"

# ④⑤⑥ 旧目录：删我们的 ✓ 搬回用户的 ✓ 删空的 ✓
if [ -n "$OLD" ] && [ -d "$OLD" ]; then
    if [ -f "$OLD/$MARKER" ] && [ -f "$OLD/.dsh-minato-files" ]; then
        while IFS= read -r rel; do
            [ -n "$rel" ] || continue
            case "$rel" in
                /*|*..*) continue ;;
            esac
            rm -f "$OLD/$rel" 2>/dev/null || true
        done < "$OLD/.dsh-minato-files"
        # 只清**我们自己布局内**的空目录 ✓（N1 ✓ 不用全局 find -empty ✓）
        for _kd in gui bin icons cli-small plugin; do
            [ -d "$OLD/$_kd" ] && rmdir "$OLD/$_kd" 2>/dev/null || true
        done
        # NB5 FIX: 这个 glob 原来在**当前目录**展开 ✗（不是 `$OLD` ✓）→ 我们自己的 `app-*` 从没被清 ✓
        for _kd in "$OLD"/app-*; do
            [ -d "$_kd" ] && rmdir "$_kd" 2>/dev/null || true
        done
        # ⑤ 剩下的都是**用户的东西** → 搬进新 prefix ✓（**在清单之后** ✓ 不会被误记 ✓✓）
        _moved=0
        # ★★★ **NB4 修复（最终复审 MAJOR —— 含空格的 prefix 会造垃圾树）** ✓✓
        #   ✗ 上一轮用 `$(find … -print0)` ✗ —— **命令替换会把 NUL 字节丢掉** ✗✗
        #     → `read -d ""` 读到的是**一整块**（不是一条一条 ✓）→ 仍然错 ✗
        #     → 而且 `read -d` 是 bash 专有 ✓ **dash 下直接失败** ✗（Ubuntu 的 /bin/sh 就是 dash ✓）
        #   ✓ 现在：**find 写进临时文件 ✓ 再逐行读** ✓✓（`IFS= read -r` 保留空格 ✓ POSIX ✓）
        #     · 先搬**文件**（缺父目录就建 ✓）
        #     · 再按 `-depth` 处理**目录** ✓ —— 在新 prefix 里**重建**（保住用户建的空目录 ✓ 不违反 N1 ✓）
        #       然后 `rmdir` 旧的 ✓（我们自己的布局目录已存在 ✓ rmdir 只在空时成功 ✓）
        # ★★★ **真机抓到的 bug（VM 端到端）—— 临时文件建在 `$OLD` 里，被自己搬走了** ✓✓
        #   ✗ `_fl="$OLD/.dsh-minato-moveback.$$"` ✗ → 而下面的搬回循环**搬 `$OLD` 里所有文件** ✗
        #     → **它把自己搬进了新 prefix** ✗ → 末尾 `rm -f "$_fl"` 已经找不到它 ✗
        #     → **永久残留 `.dsh-minato-moveback.<pid>`** ✗✗
        #     → 而卸载时它算"不属于本工具的文件" → **每一次卸载都判"目录没删干净"** ✗✗
        #       （真机实测：40/42 通过，唯一真问题就是它 ✓ 另一条是测试脚本自己的 bug ✓）
        #   ✓ 现在：**① 临时文件建在 `$OLD` **外面** ✓（放在 prefix 的父目录里 ✓ 刚建过 prefix 所以可写 ✓）
        #     ② 搬回循环**跳过一切 `.dsh-minato-*` 管理文件** ✓✓（双保险 ✓）
        _fl="$(dirname -- "$OLD")/.dsh-minato-moveback.$$"
        find "$OLD" -type f -print 2>/dev/null > "$_fl"
        while IFS= read -r _f; do
            [ -n "$_f" ] || continue
            _rel=${_f#"$OLD"/}
            case "$_rel" in
                .dsh-minato-*) continue ;;   # 我们的管理文件（清单/标记/临时文件）**绝不搬回** ✓✓
            esac
            if [ -e "$PREFIX/$_rel" ]; then continue; fi
            if mkdir -p "$(dirname -- "$PREFIX/$_rel")" 2>/dev/null && mv -- "$_f" "$PREFIX/$_rel" 2>/dev/null; then
                _moved=$((_moved + 1))
            fi
        done < "$_fl"
        rm -f "$_fl" 2>/dev/null || true
        find "$OLD" -depth -type d -print 2>/dev/null > "$_fl"
        while IFS= read -r _d; do
            [ -n "$_d" ] || continue
            [ "$_d" = "$OLD" ] && continue
            _drel=${_d#"$OLD"/}
            [ -d "$PREFIX/$_drel" ] || mkdir -p "$PREFIX/$_drel" 2>/dev/null || true
            rmdir "$_d" 2>/dev/null || true
        done < "$_fl"
        rm -f "$_fl" 2>/dev/null || true
        if [ "$_moved" -gt 0 ]; then
            ok "已把 $_moved 项你自己的文件**搬回新安装目录** ✓（不再留在 $OLD ✓）"
        fi
    else
        warn "旧目录里没有文件清单 ✓ → **不删它** ✓ 保留在 $OLD ✓（请自行确认后删除 ✓）"
    fi
    # ⑥ NB6 FIX: 搬空了就**删掉 OLD** ✓（原来只有"本来就空"的分支会删 ✗ 干净重装也会留一个空目录 ✗）
    #   ★ NB6 补全：搬回循环**故意跳过** `.dsh-minato-files` ✓ → 它留在 OLD 里 →
    #     `ls -A` 非空 → **OLD 永远删不掉** ✗（实测残留 1 个 ✓）→ 先删掉我们的管理文件 ✓
    rm -f "$OLD/.dsh-minato-files" "$OLD/.dsh-minato-install" 2>/dev/null || true
    if [ -z "$(ls -A "$OLD" 2>/dev/null || true)" ]; then
        rmdir "$OLD" 2>/dev/null && ok "已删掉空的旧目录 ✓ $OLD" || true
    else
        warn "旧目录里**还有搬不动的东西** ✓ → 保留在 $OLD ✓（没有删 ✗ 你可以自己看 ✓）"
    fi
fi

# ---- 安装标记 ✓（F1 的前提 ✓ 卸载时靠它认身份 ✓）----
{
    echo "dsh-minato install marker"
    echo "path=$PREFIX"
    echo "installed=$(date '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo unknown)"
} > "$PREFIX/$MARKER" 2>/dev/null && ok "已写安装标记 ✓" || warn "安装标记写入失败（卸载会更保守 ✓）"

# ★★ **记下我们装了哪些文件** ✓✓（卸载**只删这些** ✓✓）
#   ✗ 包内 `hashes.txt` 只覆盖 2 个文件（248 个里的 2 个 ✗ 与 Windows 侧审计 #3 同类 ✓）
#     → 卸载时"只删清单里的"会**剩 246 个文件** ✗✗（VM 实测确认 ✓）
#   ✓ 现在：安装时**把真实装进去的每个文件都记下来** ✓ → 卸载时删这份清单 ✓✓
    # S6 FIX (Linux audit MINOR): the prefix was interpolated into a sed program that used
    # ':' as its delimiter, so a ':' in the path made sed fail (empty list) and a glob
    # character left absolute paths in the file. Uninstall then deleted only the four fixed
    # names and reported nine of our own files as the user's. Running from inside the prefix
    # avoids both problems entirely.
    # ★★★ **NB2 修复（最终复审 CRITICAL —— 清单必须从 `$STAGING` 生成）** ✓✓
    #   ✗ 这里原来**又从 `$PREFIX` 生成了一遍** ✗ → 而**用户文件已经搬进来了** ✗✗
    #     → **它们被记成我们的** ✓ → **下一次卸载 `rm -f` 掉** ✓（审计实测数据丢失 ✓）
    #   ✓ 现在：**清单只在 `$STAGING`（搬进来之前）生成一次** ✓✓ 见上面 ✓
    #     → 只含**我们的**文件 ✓ 用户文件永远不在清单里 ✓✓

# ---- 命令链接 ✓ ----
mkdir -p "$BINDIR" || die "建不了 $BINDIR"
ln -sf "$PREFIX/$APP" "$BINDIR/$APP"
ok "已链接 $BINDIR/$APP"
case ":$PATH:" in
    *":$BINDIR:"*) : ;;
    *) warn "$BINDIR 不在 PATH 里 —— 加一行到 ~/.profile 即可：export PATH=\"\$HOME/.local/bin:\$PATH\"" ;;
esac

# ---- 菜单项 ✓（F11：Exec= **要加引号** ✗ 有空格否则 GLib 直接拒绝 ✓）----
if [ "$WRITE_DESKTOP" -eq 1 ]; then
    mkdir -p "$DESKTOP_DIR" || warn "建不了 $DESKTOP_DIR（跳过菜单项）"
    ICON="$PREFIX/icons/$APP.png"
    # S9 FIX (Linux audit MINOR): the Linux package ships
    # icons/hicolor/256x256/apps/dsh-minato.png (the Windows package uses icons/dsh-minato.png),
    # so Icon= fell back to the executable and the menu entry had no icon at all.
    [ -f "$ICON" ] || ICON="$PREFIX/icons/hicolor/256x256/apps/$APP.png"
    [ -f "$ICON" ] || ICON="$PREFIX/$APP"
    cat > "$DESKTOP_DIR/$APP.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=$APP
Comment=DeepSeek Harness 的非官方工具箱（只读本地状态，不联网上传）
Exec="$PREFIX/gui/dsht-gui"
Icon=$ICON
Terminal=false
Categories=Utility;
EOF
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
    ok "已写菜单项 $DESKTOP_DIR/$APP.desktop"
fi

# ---- 指纹自检 ✓（F4：**不再静默跳过** ✗ 覆盖不全 → 拒绝 ✓ 真不符 → 非零退出 ✓✓）----
if [ -f "$PREFIX/hashes.txt" ]; then
    say "  自检安装后的文件指纹…"
    if command -v sha256sum >/dev/null 2>&1; then
        checked=0; bad=0; missing=0
        while read -r want name; do
            case "${want:-}" in ''|\#*) continue ;; esac
            [ -n "${name:-}" ] || continue
            f="$PREFIX/$name"
            [ -f "$f" ] || f="$PREFIX/gui/$name"
            # ✗ F4：原来这里 `continue` **静默跳过** ✓ 然后照样打印"✓ 指纹全部一致" ✗✗
            #   ✓ 现在**计数** ✓ 缺文件就是缺文件 ✓
            if [ ! -f "$f" ]; then missing=$((missing+1)); warn "清单里有但装完没有：$name"; continue; fi
            checked=$((checked+1))
            got=$(sha256sum "$f" 2>/dev/null | cut -d' ' -f1)
            [ "$got" = "$want" ] || { bad=$((bad+1)); warn "指纹不符：$name"; }
        done < "$PREFIX/hashes.txt"
        # ★★★ 真机实测抓到（D2）：分母原来数的是**目录里所有文件** ✗ → 把安装器**自己写的**
        #   `.dsh-minato-files`（清单，生成时含自身）· `.dsh-minato-install`（标记）与**清单自身** `hashes.txt`
        #   （清单无法含自身摘要 ✓）也算成"应当被清单覆盖" ✗ → 103 条全对却报 `103/106 覆盖不全`
        #   → **正常安装假红 exit 3** ✗✗（对照实测：安装目录里 `sha256sum -c hashes.txt` 是 103/103 全 OK ✓）
        # ✓ 现在：分母只数**我们的载荷** —— 安装清单 `.dsh-minato-files` 里记录、清单**应当**覆盖的文件
        #   （排除 hashes.txt 自身与 .dsh-minato-files 自身 ✓；也天然不含用户搬回来的文件 ✓）
        #   判据仍是"清单项 + 缺失 ≥ 载荷文件数"✓ → 清单**真的**不全（例如只列 2 个）时照样会红 ✓✓
        expected=0
        if [ -f "$PREFIX/.dsh-minato-files" ]; then
            while IFS= read -r rel; do
                [ -n "$rel" ] || continue
                case "$rel" in hashes.txt|.dsh-minato-files) continue ;; esac
                expected=$((expected+1))
            done < "$PREFIX/.dsh-minato-files"
        fi
        if [ "$expected" -eq 0 ]; then
            # 清单缺失（上面已 warn ✓）→ 退回"目录里除安装器管理文件与清单自身之外的文件数" ✓ 不静默放行 ✓
            expected=$(find "$PREFIX" -type f ! -name "$MARKER" ! -name '.dsh-minato-files' ! -name 'hashes.txt' 2>/dev/null | wc -l | tr -d ' ')
        fi
        say "    清单核对 $checked 项 · 不符 $bad · 缺失 $missing · 应覆盖载荷 $expected 项（安装器管理文件与清单自身不计入 ✓）"
        if [ "$bad" -gt 0 ] || [ "$missing" -gt 0 ]; then
            # ✓ F4：**真不符要非零退出** ✗ 原来只 warn 然后照样"安装完成 ✓" exit 0 ✗✗
            warn "**指纹校验没通过** ✗ 安装包可能被改动过，或文件不完整 ✓"
            warn "已安装，但请从官方 Releases 重新下载核对 ✓"
            exit 3
        fi
        # ★★★ 审查抓到：这里只比"清单里列出的" ✓ 而**从不检查清单覆盖了多少载荷** ✗✗
        #   清单只列 2 个文件、载荷有 248 个 → 两个都对 → 照样打印"指纹全部一致" ✗（246 个文件根本没校验 ✓）
        # ✓ 现在：**覆盖率也要过** ✓✓（清单项数 + 缺失 必须 ≥ 应覆盖载荷数 ✓ 差值说明清单不全 ✓）
        cov_total=$((checked + missing))
        if [ "$cov_total" -lt "$expected" ]; then
            warn "**清单只覆盖 $cov_total / $expected 个载荷文件** ✗ 覆盖不全 → **不算通过** ✓"
            warn "已安装，但请从官方 Releases 重新下载核对 ✓"
            exit 3
        fi
        ok "指纹全部一致 ✓（$checked 项，覆盖 $cov_total/$expected 项载荷 ✓）"
    else
        warn "没有 sha256sum，跳过指纹自检（**未校验** ✓ 不是通过 ✓）"
    fi
else
    warn "包内没有 hashes.txt → **未做任何校验** ✓（不是通过 ✓）"
fi

# ---- 结束提示 ✓ ----
say ""
say "安装完成 ✓"
say "  · 启动图形界面：$BINDIR/$APP gui   或   $PREFIX/gui/dsht-gui"
say "  · 命令行：$BINDIR/$APP --help"
say "  · 卸载：$PREFIX/install.sh --uninstall   或   $SRC_DIR/install.sh --uninstall"
say "  · **本工具只读你的数据** ✓ 卸载也**不会删** ~/.dsh ✓"
say "  · 非官方工具，与 DeepSeek 官方无关 ✓（许可见 docs/ASSETS.md，隐私见 docs/PRIVACY.md）"
