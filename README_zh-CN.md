# DeepSeek Harness Toolkit — `dsh-minato`

**给 DeepSeek Harness（dsh）用的安装 / 监控 / 备份 / 修复工具箱：不用终端，双击即用。**
支持 Windows 与 Linux。只在本机运行：无遥测、无账号、不上传任何数据。

> ⚠️ **非官方工具。** 本项目与 DeepSeek 官方无关，也未获其认可或授权；它只是驱动你已有的 `dsh` 命令行。
>
> **简体中文** · [English](README.md) · [日本語](README_ja.md)

---

<p align="center">
  <img src="logo.png" alt="dsh-minato" width="180">
</p>

## 截图

下面全部是真实界面，采集自一份**夹具数据目录** —— 图中不出现任何真实会话、路径或数字。

| 看板 | 会话与 Token | 备份 |
|---|---|---|
| ![看板](docs/screenshots/gui-kanban.png) | ![会话](docs/screenshots/gui-sessions.png) | ![备份](docs/screenshots/gui-backup.png) |

| 体检 | 设置 | 更新 |
|---|---|---|
| ![体检](docs/screenshots/gui-doctor.png) | ![设置](docs/screenshots/gui-settings.png) | ![更新](docs/screenshots/gui-update.png) |

<details>
<summary>其余页面（概览 / 形态与插件 / 说明 / 日志）与命令行</summary>

| 概览 | 形态与插件 | 说明 | 日志 |
|---|---|---|---|
| ![概览](docs/screenshots/gui-overview.png) | ![插件](docs/screenshots/gui-plugins.png) | ![说明](docs/screenshots/gui-about.png) | ![日志](docs/screenshots/gui-logs.png) |

| 命令行菜单 | 命令行状态 |
|---|---|
| ![命令行菜单](docs/screenshots/cli-menu.png) | ![命令行状态](docs/screenshots/cli-status.png) |

</details>

---

## 为什么做这个

`dsh` 是命令行工具：安装它、启动它、给它做备份、以及在它起不来时查出原因，都是终端里的活 ——
而 Web UI 本身没有这些操作的入口。

这个工具箱把这些活搬到一个能双击的窗口里：

- **不用碰终端** —— 安装、启动、停止、更新、卸载，都在图形界面里点。
- **备份是可信的** —— 每个备份包带「完成标记」和逐文件内容哈希；被截断或被改过的包会被**拒收**，
  而不是稀里糊涂恢复进去。
- **坏了就直说哪里坏了** —— 体检会指出具体是哪一项不通过，并给出那一行修复命令，而不是甩给你一段堆栈。

## 功能

| | |
|---|---|
| **安装 dsh** | 双击即装。不需要懂终端，也不需要懂 Node.js。它会装好 CLI、配好 PATH 与开始菜单，并在安装目录里留一个卸载程序。 |
| **启动 / 停止 / 监控** | 一个按钮起停 Web UI，附实时状态行和要打开的地址。 |
| **备份与恢复** | 会话、设置与凭据打包成一份带完成标记和内容哈希的包；被截断或被改动的包会被拒绝恢复。 |
| **备份管理器** | 动手之前先看清这个包里到底有什么。 |
| **换机器迁移** | 导出备份包，在新机器上导入。 |
| **更新中心** | web UI、官方桌面端、本工具、已装插件，一页看全。 |
| **体检** | 「到底哪儿坏了」—— 点名出问题的那一项，并给出修复的那一行。 |
| **卸载** | **默认绝不删你的数据**；目录看起来不像一份安装时，会拒绝删除。 |

---

## 安装

### Windows

从 [Releases](../../releases) 下载 `dsh-minato-<版本>-win-x64-setup.exe` 并运行。

- Windows 产物**没有数字签名**，首次运行可能出现 SmartScreen「未知发布者」提示。运行前请用公布的 `.sha256`
  和带 GPG 签名的 `hashes.txt` 核对下载。
- 想用免安装版？下载 `dsh-minato-win-x64.zip`，解压后运行 `gui\dsht-gui.exe`。

### Linux

下载 `dsh-minato-linux-x64.tar.gz`，然后：

```bash
tar -xzf dsh-minato-linux-x64.tar.gz
cd dsh-minato-linux-x64
./install.sh            # 装到当前用户下；用 --prefix <目录> 指定位置
```

卸载：`./install.sh --uninstall`（只有你自己挪动过安装目录时才加 `--force`）。

---

## 快速上手

1. 按上面的步骤装好，打开 **dsh-minato**。
2. 看板会显示当前状态。如果 `dsh` 还没装，体检会告诉你，并给出安装入口。
3. 点 **启动**，然后打开它打印出来的 Web UI 地址。
4. 动手改任何东西之前，先点一次 **备份**。

---

## 图形界面

左侧导航共十页：

| 页面 | 用途 |
|---|---|
| **概览** | 一屏看全：装了什么、在跑什么、什么该更新了。 |
| **看板** | 关键数字 —— 会话数、缓存命中率、token —— 以及一键起停。 |
| **会话与 Token** | 逐条会话的明细，父子分组，可排序。 |
| **形态与插件** | 每个 profile 装了哪些插件、哪些被隔离、哪些会让 dsh 起不来。 |
| **备份** | 创建、查看、校验、恢复、导出、删除备份包。 |
| **体检** | 「哪儿坏了」报告与处方。**点按钮才跑，不会自动运行。** |
| **设置** | 语言、端口、行为 —— 不用手改 YAML。 |
| **说明** | 版本、致谢，以及本工具**刻意不做**的事。 |
| **更新** | 更新 dsh、桌面端、本工具或插件。 |
| **日志** | 过滤、搜索、导出启动器日志。 |

---

## 命令行

图形界面驱动的就是这套 CLI，它也可以单独使用：

```text
dsh-minato status [--detail]      装了什么 / 在跑什么 / 在听哪个端口
dsh-minato start | stop           启动或停止 dsh Web UI
dsh-minato install | update       安装或更新 dsh（以及本工具）
dsh-minato uninstall              卸载本工具（绝不动你的数据）
dsh-minato sessions               逐条会话的 token 与缓存数字
dsh-minato backup [--to <目录>]   创建一个备份包
dsh-minato backup-list [--verify] 列出备份包，并校验内容
dsh-minato backup-dir [--set <d>] 备份写到哪里
dsh-minato restore --path <包> [--apply] [--yes]
dsh-minato backup-export | backup-delete
dsh-minato doctor                 完整体检，附处方
dsh-minato profiles | profilecheck | profilepatch | bridge-install
dsh-minato bootdiag               dsh 为什么起不来？
dsh-minato verify-install         核对你自己下载的文件
dsh-minato balance                DeepSeek 账户余额（需先在设置里填 key）
dsh-minato log | config-get | config-set | autostart | shortcut
dsh-minato version | about | selftest
```

每条命令都打印机器可读的标记行（`STATUS_OK`、`BACKUP_OK`、`RESTORE_FAIL` 等），脚本和图形界面据此判断结果，
而不是靠猜散文。

---

## 安全与隐私 —— 只说代码里真做了的

本节只陈述代码的行为。如果这里写了代码没做的事，那就是 bug，请提 issue。

- **只在本机。** 工具只读写你自己的机器。**只有**这些命令会联网：`check`、`update-info`、`update-center`、
  `doctor`、`install`、`update`、`verify-install --url` 和 `balance`（**只在你填了 `balance_key` 之后**才联网）。
  其余（status、sessions、backup、restore、日志）**绝不联网**。
- **无遥测、无账号、不上传。**
- **卸载不会删你的数据。** 卸载只移除工具自己的文件；删数据是另一个显式动作。
- **备份的完整性是校验出来的，不是假设的。** 包里最后写入一个完成标记，并带逐文件哈希；中途中断或内容被改过的包会被拒收。
- **CLI、安装器、启动器和 dsh 插件零第三方运行时依赖。** 图形界面基于 Avalonia（一个 UI 框架），是唯一的例外。
- **如果你填了 API key**，它以**明文**存在你本机的配置文件里，且只有 `balance` 命令会把它发往 DeepSeek 的接口。
  留空则什么都不存。

---

## 已知限制

- **体积**：Windows 自包含包约 50 MB（.NET 运行时在里面，所以不需要另装运行时）。
- **没有数字签名**：请改用公布的 `.sha256` 与 GPG 签名核对，而不是依赖证书。
- **桥接插件需要 pnpm**（`npm i -g pnpm`）；`dsh` 不会替你装。
- **`desktop` profile 由官方桌面端管理** —— 它的插件要在桌面端自己的对话框里添加，命令行装不进去。
- **已发布版本（3.0.0 起至 3.0.6）`backup-export` 不可用**：对非空包必然失败（文件会拷出但不写
  旁挂 `.manifest`，打印 `BKEXPORT_FAIL`）。**3.0.7 起已修**（`BKEXPORT_OK` + 旁挂标记 + 端到端断言）。
- **已发布版本（3.0.0 起至 3.0.6）CLI 的退出码不能用于脚本判断**：拒绝/失败时退出码仍是 0。请以标记行
  （`BACKUP_FAIL` / `RESTORE_FAIL` / `BKEXPORT_FAIL` …）为准。**3.0.7 起已修**（失败=1，用法错误=2，
  自完整性=3）。
- **测试数量 ≠ 安全承诺的证明**：契约 / GUI / 插件测试的全绿只说明回归覆盖面；个别安全承诺曾在全绿下
  失效（如上述导出与退出码两条）。逐条承诺以端到端断言与修复记录为准。

---

## 可选的桥接插件

`dsh` 有一个只有它自己知道的事实：**哪些会话还活着**。这个事实不落盘 —— 所以不装插件时，那一列显示 `unknown`。

| | 不装插件 | 装了插件 |
|---|---|---|
| 会话清单、token、缓存命中率、速度 | ✅（读磁盘投影） | ✅ |
| **「运行中 / 挂着」标记** | `unknown` | ✅ **进程内实时** |

它只读：不发模型请求、不写 dsh 状态、不读会话正文、不联网、也绝不阻塞 dsh。安装：

```bash
dsh-minato bridge-install --profile web --yes
# 或者把仓库地址粘进桌面端的「添加插件」对话框
```

不装它，工具箱的其他功能一样能用。

---

## 许可与致谢

- [MIT License](LICENSE)。**代码是 MIT 的，图标不是** —— 图标来源与许可见 `docs/ASSETS.md`。
- [DeepSeek Harness (dsh)](https://www.npmjs.com/package/@deepseek-ai/dsh)
- **AI 协助，如实写明**：v1 脚本由 SOGR-Momono Dango（QwenPaw）协助；v2 的重写与打包由 DeepSeek DSH 协助；
  v3 与本文档主要由 AI 编码代理完成，每一处改动都由维护者复核、决定并接受。logo 为生成式 AI 产出（工具：Kimi），
  提示词由维护者编写。
- 这不代表「AI 写的所以不可信」，也不代表「AI 写的所以没问题」—— 判断依据应该是**你能不能自己复核**：
  所有产物都附 SHA-256，CLI、安装器、启动器都是可读的源码。
- GitHub：[@sakanamaru](https://github.com/sakanamaru)
