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

---

## Logo：`logo.svg` / `logo.png` / `logo-128.png` / `logo-64.png`（Minato logo）

> 本节与上文的 `logo-icon.png` / `logo-icon.ico`（鲸鱼娘）**互相独立**：
> 那是社区同人形象 + 生成式 AI 产物，**不在 MIT 授权范围内**；
> 本节的 Minato logo 是**本项目自绘的几何图形**，**随代码以 MIT 分发**。

### 来源

| 项 | 说明 |
|---|---|
| 母题 | **锚**（`minato` = 港）—— 真孔锚环（闭环）+ 横杆 + 锚杆 + 两支抓地的锚爪 |
| 创作方式 | **本项目自绘** —— 全部为纯几何路径，**不使用字体、不依赖文字**；矢量源是手写的 `logo.svg` |
| 生成式 AI | **未使用** —— 不含任何生成式图片服务的产物 |
| 第三方素材 | **未使用** —— 不含任何第三方商标、图标库或现成素材 |
| 作者 / 归属 | **本项目维护者（本项目管理方）** —— 见仓库提交历史 |
| 配色 | 只有两个主色：深海军蓝 `#0B2545`（描边）+ 青 `#2BB3A3`（主体） |
| 文件 | `logo.svg`（矢量源，可任意缩放）· `logo.png` **512×512** · `logo-128.png` **128×128** · `logo-64.png` **64×64**（三者均**正方形**、**透明背景**、RGBA） |
| 图内不含 | 文字水印、网址、作者名、内嵌位图 |

### 许可

**项目资产，随代码以 MIT 分发，商标权保留。**

| 项 | 说明 |
|---|---|
| 许可 | 本 logo 由本项目自绘，随本仓库代码以 **MIT** 分发（见 [`LICENSE`](LICENSE)）—— 与上文**不是 MIT** 的鲸鱼娘图标**明确区分** |
| 为什么可以随 MIT | 几何图形自绘、无第三方素材、无生成式 AI 产物 ⇒ 权利链干净，不存在上文图标那两类不确定性 ✓ |
| "商标权保留"的含义 | 允许用它指代本项目（README、发版说明、界面）；**不授予**将其注册为商标、或用于暗示与 DeepSeek 官方存在关联的权利 |
| 你可以做什么 | 在 MIT 范围内自由复制、修改、再分发本 logo，并可随本项目的二进制一起分发 ✓ |
| 建议 | 再分发**修改过**的版本时，请换成你自己的名称与图标，避免与本项目混淆 |

> **TODO: 维护者确认** —— 以上为**保守表述**；最终许可措辞（是否保留商标权声明、是否改用 CC0 / CC BY 4.0 等）由维护者定稿。

### 可复现方式

**工具**：Python + **Pillow**（本项目实测 `Pillow 12.3.0`）。
**不需要联网、不需要 SVG 渲染器（cairosvg / Inkscape / ImageMagick 都不需要）、不需要任何字体。**

**做法**：几何定义在 512 单位的设计空间里；每一层（描边层 = 轮廓外扩 6、主体层）先按 **8× 超采样**
画成**单通道覆盖遮罩**，再用 LANCZOS 缩到目标尺寸，最后把两个**恒定颜色**按 straight-alpha「over」合成。
颜色只在层上取常量、抗锯齿完全由 alpha 承载 —— 这样缩放不会产生常见的暗边 / 彩边，
所以 PNG 的颜色极值恰好就是那两个主色（`R 11..43 · G 37..179 · B 69..163`）✓

**校验**：把下面的脚本原样存成 `gen_logo.py` 跑一遍，会得到与仓库中**逐字节相同**的三个 PNG：

| 文件 | SHA-256 |
|---|---|
| `logo.png` | `7672a634ab18a04bd518cc90fc82733f164e43ca7e64c9bb39fe3691b4aaf6a7` |
| `logo-128.png` | `00ef8b057138eecfe2f36b4a68bb74d47dcd275428ada61e28a2527c4efe91c9` |
| `logo-64.png` | `56dffc18399b151ab9efa9f7b920be0cefec14628446906710c373bae2939fff` |

```python
# 生成 dsh-minato logo 的三个 PNG（离线，不需要网络/字体，只用 Pillow）
import math
from PIL import Image, ImageDraw

SS = 8
N = 512 * SS
NAVY = (11, 37, 69, 255)     # #0B2545
TEAL = (43, 179, 163, 255)   # #2BB3A3
K = 6.0                      # 描边：轮廓外扩量
C = (256.0, 140.0)           # 锚环圆心
RO, RI = 66.0, 34.0          # 锚环外/内半径（环是真的环，孔真透明）
STOCK = (120.0, 196.0, 392.0, 240.0)
SR = 22.0
SHANK = (230.0, 174.0, 282.0, 400.0)
AC = (256.0, 264.0)
AR, AW, A0, A1 = 148.0, 46.0, 16.0, 164.0
FL, FH, FS = 46.0, 28.0, 65.0
CX, CY, SC = 256.0, 254.5, 1.16          # 设计空间定位：缩放 1.16 并居中
X = lambda v: ((v - CX) * SC + 256) * SS
Y = lambda v: ((v - CY) * SC + 256) * SS
L = lambda v: v * SC * SS
BB = lambda cx, cy, r: [X(cx - r), Y(cy - r), X(cx + r), Y(cy + r)]


def fluke(a, g):                          # 锚爪：径向底边 + 朝上外的尖
    t = math.radians(a)
    u = (math.cos(t), math.sin(t))
    q = math.radians(-FS * (1 if a < 90 else -1))
    d = (u[0] * math.cos(q) - u[1] * math.sin(q), u[0] * math.sin(q) + u[1] * math.cos(q))
    p = (AC[0] + AR * u[0], AC[1] + AR * u[1])
    h, n = FH + g, FL + g
    return [(X(p[0] + h * u[0]), Y(p[1] + h * u[1])), (X(p[0] - h * u[0]), Y(p[1] - h * u[1])),
            (X(p[0] + n * d[0]), Y(p[1] + n * d[1]))]


def mask(g):                              # 单通道覆盖遮罩（颜色恒定，只让 alpha 抗锯齿）
    im = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(im)
    d.ellipse(BB(*C, RO + g), fill=255)
    d.ellipse(BB(*C, RI - g), fill=0)
    x0, y0, x1, y1 = STOCK
    d.rounded_rectangle([X(x0 - g), Y(y0 - g), X(x1 + g), Y(y1 + g)], radius=L(SR + g), fill=255)
    x0, y0, x1, y1 = SHANK
    d.rectangle([X(x0 - g), Y(y0 - g), X(x1 + g), Y(y1 + g)], fill=255)
    d.arc(BB(AC[0], AC[1], AR + AW / 2 + g), A0, A1, fill=255, width=int(round(L(AW + 2 * g))))
    for a in (A0, A1):
        t = math.radians(a)
        p = (AC[0] + AR * math.cos(t), AC[1] + AR * math.sin(t))
        d.ellipse(BB(p[0], p[1], (AW + 2 * g) / 2), fill=255)
        d.polygon(fluke(a, g), fill=255)
    return im


mn, mt = mask(K), mask(0.0)
for size, name in ((512, "logo.png"), (128, "logo-128.png"), (64, "logo-64.png")):
    a = Image.new("RGBA", (size, size), NAVY)
    a.putalpha(mn.resize((size, size), Image.LANCZOS))
    b = Image.new("RGBA", (size, size), TEAL)
    b.putalpha(mt.resize((size, size), Image.LANCZOS))
    Image.alpha_composite(a, b).save(name, "PNG", optimize=True)
```

**关于 `logo.svg`**：`logo.svg` 与上面的脚本是**手工同步的两份**（同一套几何、同样两个图层、同一变换
`translate(256 256) scale(1.16) translate(-256 -254.5)`）。已用 headless Chrome 把 `logo.svg`
栅格化成 512×512 后与 `logo.png` 逐像素比对：轮廓 **IoU 0.9969**、平均通道差 **0.55/255**、
差异 >24 的像素仅 **0.31%**（全部落在边缘抗锯齿上）⇒ 两者是同一枚标 ✓
（本机没有 cairosvg / Inkscape / ImageMagick，所以用 Chrome 做这次交叉验证。）
