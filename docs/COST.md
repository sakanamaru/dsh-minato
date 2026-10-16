# 成本（估算）—— dsh-minato

> 版本口径：v3.2（2026-10-16）· 命令：`dsh-minato cost`
> 一句话：**金额全部是估算**，来自**你自己填的单价** ✗ 不是账单 ✗ 也不是官方计费口径。

---

## 1. 怎么用

```powershell
# 命令行（只读，绝不写任何文件）
dsh-minato cost                          # 总计档（days=unknown）
dsh-minato cost --days 7                 # 近 7 天（--days 只认 7 / 14 / 30）
dsh-minato cost --from 2026-10-01 --to 2026-10-16   # 自定义区间（成对给出、含端点）
dsh-minato cost --level parents          # 口径三档：global / parents / parents_sub

# 图形界面：看板「指标」页顶部的成本卡（走 same 一次 overview 调用，不额外起进程）
```

**先填单价**，否则一律不显示金额：

```powershell
dsh-minato config-set sessions_price_in_per_mtok 0.14          # 输入（未命中缓存）
dsh-minato config-set sessions_price_cache_read_per_mtok 0.0028  # 缓存读
dsh-minato config-set sessions_price_cache_write_per_mtok 0     # 缓存写
dsh-minato config-set sessions_price_out_per_mtok 0.28         # 输出
dsh-minato config-set sessions_budget_usd_per_day 5            # 可选：每日上限（美元/天）
```

- 单位一律 **美元 / 百万 token**；`0` = **未填**。
- **四个键全 0 ⇒ 不显示任何金额**（只报 token）✓ 界面会说「未配置单价 → 不显示金额」。
- **我们绝不预置任何价格**，也不读第三方价表（见 §4）。
- 只填了其中几个键时，**没填的键按 0 计** ⇒ 金额会**偏低**。这是我们选择的口径：不猜、不补默认值。

## 2. 金额怎么算

```
金额(USD) = ( uncached × 输入价
            + cacheRead × 缓存读价
            + cacheWrite × 缓存写价
            + output × 输出价 ) ÷ 1,000,000
```

- 四个 token 桶**直接取自 dsh 的会话投影** `tokenUsage.val.totals.*`（`uncachedInputTokens` /
  `cacheReadTokens` / `cacheWriteTokens` / `outputTokens`），**没有任何换算或重命名**。
- ★ **算钱只用 `output=`** ✗ 我们**不用** `decodeTokens`：它来自另一个投影单元（`sessionStats`），
  而且**只在测到 firstToken 的那部分**才累加 ⇒ 会**低估**。两者可能相等，但**不保证相等**。
- 显示精度：**固定 6 位小数**（USD）。每个会话的金额先按 6 位四舍五入 ⇒ **合计 = 各会话显示值之和**
  （图形界面把逐条数字相加，结果与合计**逐位一致**）。
- 会话的 token **未知**时（例如该投影的 `tokenUsage` 行版本不认识、被行门丢弃）⇒ 该笔金额打 `none`，
  并在 `COST_NOTE session-cost-unavailable n=N` 里**说出有几笔算不了** ✗ 不假装 0 ✓。

## 3. `cost` 打印什么（标记行，冻结契约）

```
COST_META    scope=today|window days=… from=… to=… level=… priced=0|1 currency=USD tz=… truth=0 unknown_last=N model_unknown=N
COST_TOTAL   scope=today|window uncached=… cacheRead=… cacheWrite=… output=… tokens=… cost=…|none
COST_SESSION scope=window <id> model=…|unknown uncached=… cacheRead=… cacheWrite=… output=… cost=…|none
COST_BUDGET  limit=…|none spent=…|none over=0|1
COST_NOTE    price-missing | official-billing-not-compared | model-approximate | budget-scope-global | session-cost-unavailable n=N
COST_FAIL    <原因>
```

要点：

- **一个进程吐两块**：`scope=window`（当前窗口，含逐会话行）与 `scope=today`（今日，只有合计）。
  图形界面看板本来只跑一次 `overview`，成本块**搭同一次输出** ⇒ ✗ 不为成本再起进程 ✗。
- `truth=0` 的口径与看板其它卡片一致：**整会话累计，不是窗口内增量**。
- **窗口集合与 `sessions` 完全一致**：`COST_SESSION` 的 id 集合 == `SESSAGG_SESSION … bucket=own` 的 id 集合（逐 id 比对）。
- `unknown_last` = 没有 `lastPromptAt` 的会话数（**与所选窗口无关**，是全局事实）。
- `model_unknown` = 该集合里「最后使用模型」未知的会话数。
- `COST_NOTE` **可能同时出现多条**（顺序不保证，按「出现即累加」解析）。

## 4. 为什么不预置价、为什么不读 pi-ai 的价表

本机**确实存在**一份带缓存价的定价表（`@earendil-works/pi-ai` 的 `providers/data/*.json`，40+ 个 provider，
单位同为「美元/百万 token」）。我们**依然不读它**，三条理由：

1. **路径与安装方式强耦合**：它可能出现在 npm 全局、`profiles/node_modules`，或被打进桌面端的
   `app.asar`（**文件级读不到**）；换一种包管理器布局就又变了。读依赖内部文件很脆。
2. **覆盖缺口正中主力**：本机用得最多的模型走**手工声明路由**（`cordis.patch.yml` 里只有
   `id`/`contextWindow` 等，**没有 `cost`**），价表里**根本没有这些条目** ⇒ 拿它算钱会给主力会话算出
   0 元或算不出来，正是最不该错的那些。
3. **dsh 自己从不读它**：`dsh-llm-pi-ai` 源码原文写着 *"The harness never reads pi-ai's cost metadata"*。
   也就是说，把它当「官方价」没有依据。

⇒ 结论：**金额只能来自你自填的四个键**。什么时候填、填多少，由你按自己实际支付的价格决定。

## 5. 「今日」与预算

- 「今日」= 本地日 `00:00 → 次日 00:00`，按会话的 **`lastPromptAt`（最后一次用户提问）**归属。
- 因此会有一条**必然偏差**（界面今日卡上必须带这句脚注）：
  > 整会话累计，非今日增量 · 按最后提问归属（昨天提问、今天仍在用的会话不计入） · N 个无锚点会话被排除
- `COST_BUDGET` 的 `limit` / `spent` / `over` **恒按「今日 · `level=global`」**计算，
  **不随 `--level` 变化**：`parents_sub` 会把父会话与子代理**重复计入** ⇒ 会造出**假超限**。
  当你用了非 `global` 口径时，输出里会出现 `COST_NOTE budget-scope-global` 说明这一点。
- **超限只是提示，绝不阻止任何操作**——这不是收费系统，也不是配额系统。
- **零写入**：`cost` 全程只读会话投影与配置，**不写任何文件**，也不记录任何累计值（每次重算）。

## 6. 已知边界（写清楚，不藏）

- 金额是**估算**：以**官方账单/平台计费为准**。我们**不做**「与官方账单对账」（平台余额接口没有历史）。
- **不按模型分摊**：投影里 token 四个桶**没有 model 维度**，只有「会话最后使用模型」
  （`modelSelection.val.lastUsed`）⇒ 界面上那是**近似**，并给出 unknown 计数。会话内换过模型就会失真。
- **没有逐日真增量**：投影只有整会话累计（`totals`）+ 最后一步（`last`），没有时间序列。
  真值需要读原始日志全流 ⇒ 属后续版本。
- 界面只有 **USD** ✗ 不做人民币换算（汇率会漂，与「不猜」冲突）。
- 只读会话**投影**（明文 JSON），**不读对话正文**，**不联网**。

## 7. 相关配置键（四处同步：模型 / 编解码 / 校验白名单 / `config-get`）

| 键 | 含义 | 单位 | 0 的含义 |
|---|---|---|---|
| `sessions_price_in_per_mtok` | 输入（未命中缓存）单价 | 美元/百万 token | 未填 |
| `sessions_price_cache_read_per_mtok` | 缓存读单价 | 美元/百万 token | 未填 |
| `sessions_price_cache_write_per_mtok` | 缓存写单价 | 美元/百万 token | 未填 |
| `sessions_price_out_per_mtok` | 输出单价 | 美元/百万 token | 未填 |
| `sessions_budget_usd_per_day` | 每日预算上限 | 美元/天 | 未设（不作超限判定） |

四个单价键里的 `0` 与预算键的 `0` 都是「**没填**」的意思 —— 我们**不会**把它当成「免费」。
