# 资产来源与许可（Assets）

本仓库的**代码**以 **MIT** 许可发布（见 [LICENSE](LICENSE)）。

**非代码资产（图标）有独立的来源与许可** —— 这里逐项写清，目的是让任何人都能自己判断
「能不能用、怎么用、有什么风险」。

> **一句话总结**：**代码是 MIT 的，图标不是。** 两者分开标注，互不覆盖。

---

## 图标：`logo-icon.png` / `logo-icon.ico`

### 来源

| 项 | 说明 |
|---|---|
| 形象 | **鲸鱼娘**（Whale-chan）—— DeepSeek 社区广泛使用的**拟人化同人形象**，从官方鲸鱼标识衍生而来 |
| 为什么用它 | 这是社区**公认**的形象，多个第三方 dsh 项目都在使用（见下方"社区使用情况"）。本项目**不声称拥有它** |
| 生成方式 | **生成式 AI 产出**（工具：**Kimi**；提示词由本项目维护者编写） |
| 作者署名 | **本项目维护者（提示词作者）** —— 见仓库提交历史 |
| 文件 | `v3/gui/Dsht.Gui.Avalonia/Assets/logo-icon.png` · 同目录 `logo-icon.ico` |

### 许可

| 项 | 说明 |
|---|---|
| 许可 | **不适用本项目的 MIT 许可** —— 代码随 MIT，**图标不在 MIT 授权范围内** |
| 为什么单独标注 | ① **AI 生成内容的版权归属在法律上并不清晰**（不同法域结论不同）；② **鲸鱼娘是社区同人形象**，其权利归属涉及原同人作者与官方标识两方 |
| 你可以做什么 | 按 MIT 自由使用、修改、再分发本项目的**代码** ✓；再分发**含此图标的二进制**时请自行判断该图标的风险，或**替换成你自己的图标** ✓ |
| 替换成本 | **极低** —— 图标只用在三处（GUI 窗口图标、启动器壳、安装器），换三个文件即可 ✓ |

### 本项目为降低风险做的事

- **不声称拥有**该形象，也不把它注册为商标 ✓
- **不把它当作官方标识** —— README 顶部有明确的「**非官方、与 DeepSeek 官方无关**」声明 ✓
- **不用于商业用途** ✓（本项目完全免费、非商业 ✓）
- **主动署名** ✓（见下方"署名"）
- **主动说明**：本文档本身就是主动说明，不藏着 ✓

### 署名（Credits）

```
鲸鱼娘（Whale-chan）形象来自 DeepSeek 社区同人创作。
本项目的图标为生成式 AI 产出（工具：Kimi），提示词由本项目维护者编写。
本项目为非官方、非商业的开源工具，与 DeepSeek 官方无关，不使用任何官方标识。
```

这段署名同时出现在：
- 本文件（`ASSETS.md`）✓
- `README.md` / `README_zh-CN.md` ✓
- GUI 的「关于」页 ✓
- CLI 的 `about` 命令输出 ✓

### 社区使用情况（说明"公认"的依据）

同人形象「鲸鱼娘」在 DeepSeek Harness 生态里被广泛使用，例如：

- [Neko3000/deepseek-whalechan](https://github.com/Neko3000/deepseek-whalechan) —— **角色设定规范与视觉素材库**（"公认形象"的出处）
- [fornarwhal/deepseek-whale-girl-icon](https://github.com/fornarwhal/deepseek-whale-girl-icon) —— 专门的图标仓库
- [small-tailqwq/dsh-deep-whale](https://github.com/small-tailqwq/dsh-deep-whale) —— 鲸鱼娘皮肤系列
- [Andersen216/dsh-whale-girl-live2d](https://github.com/Andersen216/dsh-whale-girl-live2d) —— Live2D 桌宠（该仓库标注「模型素材非商业，**CC BY-NC-SA 4.0**」）
- [Elave-66/dsh-blue-sea-launcher](https://github.com/Elave-66/dsh-blue-sea-launcher) · [turtle2209/Bigfish](https://github.com/turtle2209/Bigfish) · [online111111/whalechan-dsh-theme](https://github.com/online111111/whalechan-dsh-theme) · [touche-s/rice-loving-whale](https://github.com/touche-s/rice-loving-whale) · [Yelloooooow/dsh-whale-pet](https://github.com/Yelloooooow/dsh-whale-pet)

**若将来发现原同人作者提出了明确的许可条款**（例如 CC BY-NC-SA 一类），本项目会：
1. 按该条款**补充署名** ✓
2. 若条款与本项目冲突，**换成自绘图标** ✓

### 关于代码签名（许可兼容性说明）

申请**免费的开源代码签名**（例如 SignPath Foundation 一类）时，审核方可能要求
**整个作品处于 OSI 认可许可之下**。届时此图标**可能成为障碍**（AI 生成 + 社区同人，两条都不是 OSI 许可）。

**届时的处理顺序**：

1. **在申请材料里主动说明** ✓ —— 主动说明远比被问出来好；同时说明本项目**非商业**、**已署名**、**不声称拥有**
2. 若审核方坚持要求 OSI 许可 → **换成自绘图标**并改为随 MIT 发布（见下方"替换指引"）
3. 替换后再提交申请 —— 自绘图标在许可上**没有任何可争议之处** ✓

### 替换指引（换成自绘图标）

1. 画一个自己的图标（**几何图形即可**），导出 `logo-icon.png`
   （建议 **256×256 或更大**、正方形、带透明通道）
2. 生成 `.ico`：**PNG-in-ICO** 即可 —— 6 字节文件头 + 16 字节目录项 + PNG 原始数据：
   ```
   偏移 0:  00 00        （保留）
   偏移 2:  01 00        （类型 = 图标）
   偏移 4:  01 00        （图像数量）
   偏移 6:  宽（256 写 0）高（256 写 0）00 00 01 00 20 00
   偏移 14: PNG 字节数（4 字节小端）
   偏移 18: 22 00 00 00  （数据偏移 = 6 + 16 = 22）
   偏移 22: PNG 原始数据
   ```
3. 覆盖这两处：
   `v3/gui/Dsht.Gui.Avalonia/Assets/logo-icon.png` 与同目录 `logo-icon.ico`
4. 重新构建：
   - GUI：`dotnet publish v3/gui/Dsht.Gui.Avalonia/Dsht.Gui.Avalonia.csproj -c Release -r win-x64 --self-contained true`
   - 启动器壳 + 安装器：`pwsh -File v3/tools/build_installer.ps1 -PayloadDir <包目录> -Version <版本>`
5. 把本文件"来源/许可/署名"三段改成「**自绘，随本项目 MIT 许可发布**」

---

## 与 DeepSeek 官方标识的关系

**本项目不使用 DeepSeek 官方的任何标识** ✓：

- **不使用官方鲸鱼 logo 本身** —— 它是 DeepSeek 的商标
- **不使用官方名称、官方界面素材**
- 使用的是**社区同人形象**（鲸鱼娘）—— 本项目**如实标注**其来源，且**明确声明非官方**
- README 顶部有明确的「**非官方**」声明；界面与文档中不出现任何"官方"字样

---

## 字体

界面使用**系统字体**（Windows 的 Microsoft YaHei UI、Linux 的 Noto 系列等），
**不随包分发任何字体文件** ✓ —— 因此不涉及字体授权问题。

---

## 第三方代码与运行时依赖

**运行时零第三方依赖** ✓：

| 部分 | 说明 |
|---|---|
| CLI / GUI | .NET **自包含**发布；GUI 使用 **Avalonia**（MIT ✓，已在 `Dsht.Gui.Avalonia.csproj` 中声明） |
| 启动器壳 / 安装器 | **.NET Framework 4.8** —— **Windows 系统组件** ✓，不是第三方依赖 ✓ |
| 打包 / 测试脚本 | Windows 自带工具 + .NET SDK + PowerShell（**构建期**依赖，不随包分发 ✓） |
| 自解压安装器 | **自研** ✓（未使用 7-Zip / NSIS / Inno Setup 等第三方打包器 ✓） |

---

## 数据

本工具**只读** DeepSeek Harness（dsh）的本地状态，**不上传任何数据** ✓。
卸载时**默认不删除**你的 `~/.dsh` 数据 ✓（那是 dsh 自己的数据，不是本工具的）。
