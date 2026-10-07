<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="180">
</p>

# dsh-minato 3.0.1 — 2026-10-03（响应性与界面整理 / responsiveness & UI tidy-up）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：把被指出的「卡」与「读数不诚实」集中修掉 —— 切页不再等 CLI、体检改成按钮触发、
「运行中」改成如实三态、设置页分类并改成选择框、新增 DeepSeek 余额检测；GUI 体积同时瘦了三分之一。

**In one line**: the reported sluggishness and the misleading session status are fixed — instant page
switches, a button-triggered health check, three honest session states, a categorised settings page with
drop-downs, a new DeepSeek balance check, and a GUI a third smaller.

**需要你做什么 / Action required**：**否**（建议升级）—— 正文无安全类条目；升级可换来更诚实的会话状态、即时切页，以及体积小三分之一的 GUI。

**本版改了什么（概述）**

- 修复**读数不诚实**：不再把「挂在 dsh 进程里」说成「运行中」，改为 **运行中 / 挂着·最后活动 N 前 / 活动未知** 三态（老插件用最后活动时间兜底）。
- 修复**点击后零反馈**：进页原本要等 CLI 跑完才重画（体检 6.5 秒、更新 7.9 秒屏幕不动）→ 现在点击立刻换页 + 加载指示 + 数据到达淡入。
- 新增：**DeepSeek 余额检测**（自填 key，未绑定则整卡隐藏且不发任何请求）、概览自动刷新（0.5/1/3/5 秒 + 暂停 + 自定义）、可选启动页、体检逐行出结果、桥接插件 0.2.0「真在动」信号。
- 变更：GUI 启用裁剪，Windows 自包含包 **约 74 MB → 约 50 MB**；设置页分类并改用下拉选择框；体检不再自动运行、改为按钮触发。
- 变更：v2 整棵树移入 `v2/`（仓库根条目 24 → 14）；三语 README 与本页统一为同一套骨架。

`commit=e065a693dd30450ffb2f8a2a4858d8129cd672b1` · `tag=v3.0.1`

<details>
<summary><b>完整发版说明（点开展开）</b></summary>

---

## 新增 / Added

- **DeepSeek 余额检测**：在设置里填入你自己的 key（`balance_key`）后，概览页显示**充值余额 / 活动赠送余额**；
  未绑定则整卡隐藏，且**一个网络请求都不发**。key 明文存在本机配置里，只发给 DeepSeek 的接口。
  **DeepSeek balance**: set your own key in Settings to see topped-up and granted balance; hidden and fully
  offline when unset. The key is stored in plain text locally and only ever sent to the DeepSeek API.
- **概览自动刷新**：实时 0.5 秒 / 快 1 秒 / 中 3 秒 / 慢 5 秒 / 暂停 / 自定义（0.5–3600 秒）。只在概览与看板生效；
  刷新在跑时跳过本拍，不会堆积。
  **Overview auto-refresh** with 0.5 / 1 / 3 / 5 s presets, pause, and a custom interval.
- **启动页可自选**：设置里选好，下次打开就落在那一页。
  **Selectable start page.**
- **体检逐行出结果**：点「运行体检」后列表先出现，结果一行一行补上（慢的那项最后出，且如实说明它为什么慢）。
  **Row-by-row health check**: the list appears first and fills in as each check finishes.
- **桥接插件 0.2.0**：新增「真在动」信号（相邻两拍之间事件序号变过即为在动）—— 长生成中途也判得准。
  **Bridge plugin 0.2.0** adds a real activity signal (event-sequence delta), accurate even mid-generation.

## 修复 / Fixed

- **不再把「挂在 dsh 里」说成「运行中」**：`live` 只表示会话还挂在 dsh 进程内（桌面端开着时它 store 里的会话全是 `live`，
  哪怕几天没碰）。现在如实分三态：**运行中 / 挂着 · 最后活动 N 前 / 活动未知**，计数改名「dsh 活跃」。
  插件还是 0.1.0 时用**最后活动时间**兜底，所以老用户同样吃到这条修复。
  **"live" is no longer reported as "running"** — three honest states, and it works with the older plugin too.
- **点击后零反馈**：原来进页要等 CLI 跑完才重画（体检 6.5 秒、更新 7.9 秒里屏幕一动不动）。现在点击**立刻换页**
  （先显示旧数据），附加载指示、说明为什么等，数据到账时淡入。
  **Instant page switch with visible loading** instead of up to 7.9 s of a frozen screen.
- **排队的刷新会提前熄灯** → 改为「还有排队就继续亮」。
  **The loading indicator no longer goes dark while a queued refresh is still pending.**
- **发布清单里的名字与包内实际条目对不上**（清单写 `.github/SECURITY.md`，而 zip 里是 `SECURITY.md`）。
- **两处自检漏检**：`verify_fixes` 的命令面清单、`compare_markers` 的忽略规则（空值键在行尾 Trim 后匹配不到）。
- **`.dsh_launcher_root` 曾被取消跟踪**，而发布清单与 zip 仍按路径取它 —— 会让 `build` job 失败、并让发布包丢掉
  卸载器的「防误删」闸门。**已恢复跟踪**。

## 变更 / Changed

- **GUI 体积 −33%**：启用裁剪（`PublishTrimmed` + `TrimMode=partial`）。Windows 自包含包 **约 74 MB → 约 50 MB**；
  GUI 自身的压缩包 43.7 MB → 19.3 MB。刻意**不做**单文件（只再多省 3.5 MB，代价是首次运行要解包到临时目录）。
- **设置页分类 + 选择框**：分为「界面与启动（含排障开关）」「dsh · 更新 · 数据 · 余额」「其它」；
  枚举型配置一律下拉选择，不再手敲。
- **体检不再自动运行**：进页只显示上次结果，点按钮才跑。
- **形态与插件页的安装教程移到底部。**
- **v2 整棵树移入 `v2/`**：仓库根条目 **24 → 14**。已发布的老版本不受影响（v2 的发布包自带 `verify.ps1`，
  公钥回退按不变的 tag 下载）。
- **文档统一**：三语 README 重写为同一套骨架（动机 / 功能 / 安装 / 快速上手 / 图形界面 / 命令行 /
  安全与隐私 / 已知限制 / 可选插件 / 许可与致谢）；本页与后续发版说明也统一为同一骨架。

---

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.0.1-win-x64-setup.exe` (+`.sha256`) | Windows 安装器（双击即装） |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`./install.sh`） |
| `hashes.txt` / `hashes.txt.asc` | 全部产物的 SHA-256，及其 GPG 签名 |

## 验证 / Verification

- **本地就绪度门槛 13 项全绿**；契约测试 **355** · GUI 逻辑 **68** · 桥接插件自测 **25** · v2 单元测试 **318**
- **CI 两平台全绿**（windows-latest / ubuntu-latest）；Windows 包内含 **GUI 窗口冒烟**（真的启动并创建窗口）
- **人工验证**：Windows 上逐页点过（GUI）。Linux 真机（虚拟机）安装验证停留在 **3.0.0** —— 本版只有 CI 的
  打包与冒烟，**没有重跑真机**，如实写明。

## 已知边界 / Known limits

- Windows 产物**没有数字签名** —— 请用公布的 `.sha256` 与 GPG 签名核对，而不是依赖证书。
- 桥接插件需要 pnpm（`npm i -g pnpm`）；`desktop` profile 由官方桌面端管理，插件要在桌面端自己的对话框里添加。
- 图形界面基于 Avalonia，因此不是零依赖 —— 项目其余部分是。
- Linux 真机验证停留在 3.0.0（见上）。

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)

</details>
