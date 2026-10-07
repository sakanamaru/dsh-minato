# 发布说明 / Releasing dsh-minato（仓库内可见的摘要）

> 面向**未来的维护者（包括六个月后的你）**。
> 权威清单在项目根：`D:\dsh-minato\发版与协作清单.md`（§三 前置检查 · §五 文档骨架 · §九 陷阱 · §十 发版政策 P1~P8）。
> 这里只放**仓库内可见的摘要 + 指针**，不复制整份长清单 —— 两份文件冲突时，以项目根那份为准。

---

## 0. 触发条件：**这一版过了 gate**，不是"我们干完了一批活"

政策 **P8** 的原话就是这一句。落到动作上：

```
CLI 构建 0 错误 · GUI 构建 0 错误
契约测试 · GUI 逻辑测试 · 插件 Node 自测
verify_fixes · verify_restore_apply · verify_command_matrix · v2 replicate_ci · verify_switchover（== 全部就绪 ==）
YAML 真解析通过 · main == v3-linux == 目标提交（全长 SHA）
        ↓  全绿
      才打 tag
```

**任一项红 → 立刻中止 → 不打 tag、不 push。** 这条不是口号：`v3/tools/release.ps1` 把它做成了**机械化**的
（每一步取真实退出码，失败即 `exit 1`）。**在脚本之外手动发版 = 放弃这道 gate** —— 别这么做。

> 反例（真实事故，见《发版与协作清单》§九 陷阱 17）：用短 SHA 去比 40 位全长 SHA → 条件恒假 →
> CI 已经红了，tag 照样打上去。所以脚本里**所有** SHA 比对都强制两侧全长。

---

## 1. 用 `v3/tools/release.ps1`

```powershell
# 自检（不碰远端；验证本脚本的关键机制：全长 SHA / 真实退出码 / YAML 真解析 / DryRun 不写东西）
powershell -NoProfile -ExecutionPolicy Bypass -File v3\tools\release.ps1 -SelfTest

# 只打印计划，什么都不改
powershell -NoProfile -ExecutionPolicy Bypass -File v3\tools\release.ps1 -Version 3.0.8 -DryRun

# 正式发布（gate 全绿 → 打 tag → 推 → 注入 release body 的来源块）
powershell -NoProfile -ExecutionPolicy Bypass -File v3\tools\release.ps1 -Version 3.0.8
```

| 参数 | 作用 |
|---|---|
| `-Version x.y.z` | **必填**（脚本不猜、不写死版本号）；会与 `Program.cs` 里的 `ToolkitVersion` 核对同源 |
| `-Repo <path>` | 仓库根，默认脚本上两级 |
| `-DryRun` | 只打印计划，不改任何东西（含"不 push、不打 tag"） |
| `-SelfTest` | 自检关键机制；**不碰远端** |
| `-SkipGate` | 仅与 `-DryRun` 同用（快速看计划）；正式发布不允许跳过 |
| `-NoPush` | 不推远端（本地演练用） |

脚本**不会**做的事：改版本号、改 CHANGELOG、改 workflow、往 tag 之后落提交。
`release:` 提交请自己按《发版与协作清单》§二 ①~⑤ 做完（版本号三处 + notes + CHANGELOG + 提交 + push），再交给脚本。

发完之后照《发版与协作清单》**§四 发版后终验**逐项核对（本文件不重复）。

---

## 2. 版本号只给"**用户需要采取行动**"的改动（政策 P1）

只有满足下列之一才发新号：

- **数据 / 安全正确性修复**；
- **用户可见行为变化**（需要重装 / 改配置 / 改用法）；
- **用户必须更新才能避免损失**。

**纯 CI 修复 · 文档 · 测试 · 内部重构 → 只提交到 `main`/`v3-linux`，不升号、不打 tag、不发 release。**

自查一句：*"用户不更新会出事吗？"* 答不上来就别发号。

---

## 3. 发版说明分层（政策 P2）

| 粒度 | 写多少 |
|---|---|
| **每个 patch** | CHANGELOG **一行**（版本 · 日期 · 一句话 · 是否需要行动）+ release body 一段短叙述。用 `docs/release-notes-TEMPLATE.md` 的**短版** |
| **每个 minor** | 一份**合并叙述**（把期间所有 patch 合并写一次：动机 / 功能 / 已知边界） |

✗ 不要再"每版都写 4k~8k 字符的完整文档" —— 那是 v3.0.0 返工的根源（政策 P2）。
✗ 不要写"**随下一版本发布**"这类**注定作废**的句子 —— 出现这句就说明你在发布里承诺了还没发生的事。

每条"**已验证**"必须指向**一条会失败的测试**或**一次真实端到端运行**；指不到就写"**已实现；测试未覆盖**"。
（这是《发版与协作清单》§三 里最值钱的一条。）

---

## 4. 产物来源必须可核对（政策 P3）

- release body **顶部**固定写：`commit=<全长 sha>` · `tag=<tag>` · `tag-ci-run=<id>`。
  `release.ps1` 会自动注入（run id 取不到就写 `unavailable` —— **不编数字**）。
- **tag 必须打在"产出这些产物"的那个提交上**：脚本在打 tag 前断言 `main == v3-linux == 目标提交`，
  打 tag 后再断言 `tag^{commit} == 目标提交`。
- **tag 之后不要再往分支落提交。** 现状：CI 会在**分支**上推 manifest 提交（`build: regenerate hashes.txt`）
  —— 见 §6 的 TODO；在那之前，必须**先把 CI 的 manifest 提交合并进来**，再打 tag。
- 正文的来源块**只以仓库内的规范 notes 文件为源**（`docs/release-notes-vX.Y.Z.md`）。
  ✗ 不要用 `gh --jq` 抓正文再回写：PowerShell 5.1 会按 ANSI 解码 UTF-8，你会以为文件有乱码（今天踩过）。

---

## 5. 撤版必须有痕迹（政策 P4 / P7）

撤版**是正当处置**（留一个坏版本在线更糟），但**必须留痕**：

1. 往 `docs/WITHDRAWN.md` 加一行：**版本号 · 撤版日期 · 原因 · 用户该怎么办**；
2. 保留一份标注 `withdrawn` 的 **stub 发版说明**（三行也够）；
3. 撤版动 tag 之前先看《发版与协作清单》§八 陷阱 7（immutable release 锁 tag：先删 release，临时关 tag 保护规则集）。

没有痕迹的撤版 = 六个月内没人能解释版本号为什么跳号（`v3.0.2` 就是这么差点丢掉记录的）。

---

## 6. 已知差距与 TODO（只记录，不代表已解决）

- **workflow 目前是"分支 push 驱动"**，tag 上的 CI 从 tag 构建，但 manifest 提交仍推回分支。
  建议改成 **tag 驱动**（让 tag CI 从 tag 构建、不再往分支推提交）。**建议 diff 见《发版与协作清单》§十一**；
  `.github/workflows/build-release.yml` 本批次**未改动**。
- `release.ps1` **不替代**《发版与协作清单》§二 ①~⑤（版本号三处 / notes / CHANGELOG / 提交 / push）；
  它只接管**从 gate 到 release body** 这一段。
- 门槛里的**项数下限**（契约 ≥385 · GUI 逻辑 ≥69 · 插件 ≥25 等）写死在脚本里：门槛用例**增加**时无需改，
  **减少**时会红 —— 这是故意的（防止静默删用例）。

---

*本文件由施工会话于 2026-10-07 新增；与项目根《发版与协作清单》冲突时以后者为准。*
