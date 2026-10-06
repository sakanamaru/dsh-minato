# V3 跨平台 GUI（Avalonia）—— dsh-minato 的桌面壳

> 状态：**八个页面全部成型**（2026-09-28）。V3.0 方案 §7.5 拍板的"跨平台 GUI 重做"已越过骨架期：
> 全部数据页都接上了 CLI 标记行，无边框窗口 + 自绘标题栏，4 种配色 × 5 种布局可切。
> **它现在长什么样，要你自己跑一遍看**——界面效果我无法替你验收。

## 怎么跑

```bash
# 需要 .NET SDK 8
dotnet run --project v3/gui/Dsht.Gui.Avalonia
```

GUI 需要能找到**工具箱 CLI**，按这个顺序找：

1. 环境变量 `DSHT_CLI`（指向 exe 全路径）
2. GUI 同目录的 `dsh-minato.exe` / `dsht.exe` / `dsht_v3.exe` / `DeepSeek Harness Toolkit.exe`
   （Unix 找同名无扩展名的 apphost）

最省事的做法：把 V3 CLI（`dsh-minato.exe`）编译到 GUI 的输出目录旁，或直接指定：

```bash
export DSHT_CLI="$(pwd)/dsh-minato.exe"      # Windows PowerShell: $env:DSHT_CLI="D:\...\dsh-minato.exe"
dotnet run --project v3/gui/Dsht.Gui.Avalonia
```

profile 隔离等少数操作还需要 **v2.x 核心程序**（有 `profilepatch` 的那个经典 exe）：
按 `DSHT_CORE` 环境变量 → 同目录 `DeepSeek Harness Toolkit.exe` / `dsht.exe` 找；
该核心是 Windows/.NET Framework 专有，非 Windows 上这些按钮会如实报"平台不支持"。

## 八个页面（侧栏一级导航）

| 页面 | 数据源（CLI 标记行） | 内容 |
|---|---|---|
| 概览 | `overview`（聚合 `status --detail` + `profiles` + `sessions` + `backup-list`，一次调用）| 一键启动/停止 dashboard：运行状态 hero、PID/启动时间/已运行、token/缓存命中/解码速度/会话数，子页签切原始输出 |
| 看板 | 同上 | KPI 总览 + 操作回执；子页签是**手绘图表**（近 14 天新增会话、缓存命中率分布，纯 Grid/Border 柱子，零图表依赖） |
| 会话与 Token | `sessions` | 汇总条 + 工具栏（排序/过滤）+ 会话列表或统计子页（最耗 token 排行）；子菜单切"列表/统计"，排序一律走工具栏下拉 |
| 形态与插件 | `profiles` | 每个 profile 的形态（web/headless/acp）与插件卡片（含第三方），过滤 chips + 就地搜索；隔离等写操作走 v2.x 核心并有二次确认 |
| 备份 | `backup-list --detail` | 备份清单（时间/范围/大小）+ 立即备份/导出/恢复预览/应用恢复/删除（删除要两次确认） |
| 体检 | `doctor` | 结论徽章 + 分级条目（错误在前），原始输出另有子页 |
| 设置 | `config-get` / `config-set` | 当前配置项（脱敏后）展示与保存 |
| 说明 | `describe` | 工具箱对当前安装的判断与依据（标记行原文） |

## 窗口与外观

- **无边框窗口**：`ExtendClientArea` + 自绘标题栏（品牌 `dsh-minato` + logo 标、最小化/最大化/关闭）
- **4 种配色**（标题栏 Style0–3：浅卡片 / 深卡片 / 深色紧凑 / 浅仪表盘）× **5 种布局**（Shell0–4：侧栏 / 顶部标签 / 卡片网格 / 主从 / 混合，默认混合式）——运行时任切
- 所有颜色走 `Palette`（`ViewModels/SessionRowVm.cs` 顶部），不允许硬编码白色，否则深色风格必破

## 架构纪律（没变，且是底线）

- **GUI 不引用核心程序集**：它只运行 CLI 并解析标记行 —— 这是 §7.3"呈现层与核心隔开"的兑现，也是"换 UI 不用改核心"的前提
- 导航表（`NavItems`/`NavIcons`/`NavCli`/`NavSubs`/`NavDesc`）必须保持 8 行对齐；访问处带范围保护（审计教训：错位曾是 UI 线程越界崩溃源）
- 刷新异步化 + 重入保护：CLI 调用不占 UI 线程，刷新期间再点不叠加

## 逻辑测试

`v3/gui/Dsht.Gui.LogicTests`：**58 项，不依赖 Avalonia**（CI 里 windows+ubuntu 都跑）——覆盖标记行解析、unknown 语义、脏数据容错、备份/体检/配置解析。

```bash
dotnet run --project v3/gui/Dsht.Gui.LogicTests -c Release
```

## 还没做的（诚实清单）

| 项 | 说明 |
|---|---|
| i18n | 界面是硬编码中英混排；**没有** L10N 机制（v2.x WinForms 那套 i18n 强制检查还没搬过来） |
| 发布形态 | **未定**：self-contained（60–90 MB，免装运行时）还是 framework-dependent（小，要 .NET 8）——需要拍板 |
| 体检页原始输出子页 | 子页签只有一项时切不到"原始输出"页（`SubTab==1` 分支暂不可达），内容本身在说明页可见 |
| 与 WinForms 的关系 | 并行存在；v2.x 发布线仍是 WinForms 版，成熟前不替换 |

## 图标许可提醒

标题栏/窗口图标是**网友二创 + AI 生成**的形象，**不适用本仓库的 MIT 许可**——来源脉络与使用边界见 `Assets/README.md`。

## 与 v2.x WinForms 版的关系

| | v2.x WinForms | V3 Avalonia（本目录） |
|---|---|---|
| 平台 | 仅 Windows | Windows / Linux / macOS |
| 编译 | `csc` 单文件 exe（零依赖） | net8 + NuGet（Avalonia） |
| 状态 | **已发布**（v2.7.3） | **页面成型，发布形态未定** |
| 复用 | — | 复用同一套 CLI 标记行契约与领域逻辑 |
