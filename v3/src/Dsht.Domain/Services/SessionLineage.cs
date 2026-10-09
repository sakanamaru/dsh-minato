using System;
using System.Collections.Generic;

namespace Dsht.Domain.Services
{
    /// <summary>血缘边输入（纯数据 ✓）。由 CLI 从桥插件快照 lineage 段解析后注入（领域层不碰磁盘/时钟 ✓✓）。
    /// 判定口径写死（看板施工规格 §B / §11.7-B）：
    ///   · 子代理 ⟺ Origin=="subagent"（首帧 header origin ✓ 283/283 与投影 identity 等价已验 ✓）
    ///   · fork = 有 Parent 且 Origin!="subagent"（dsh fork 复制会话 ✓ 实测 18 个 ✓）
    ///   · 根 = Parent 为空
    ///   ✗ delegationDepth 禁判层级（实测 113/282 不一致 ✓）—— depth 一律自走链计算（根=0）。</summary>
    public sealed class LineageEdge
    {
        public string Id = "";
        public string Origin = "";      // 原样透传；空 = header 没有 origin 字段
        public string Parent = "";      // 空 = 无父；已去 "session-" 前缀
        /// <summary>多父防御（§B.3 ✓）：parent 解析出 &gt;1 个 id（防御数组形态；当前写入器只写单值）
        /// ⇒ 拒绝归类该会话（只进全局）+ 调用方打 SESSAGG_GUARD multiparent=&lt;id&gt;。</summary>
        public bool Multiparent;
    }

    /// <summary>单个会话的血缘计算结果（纯数据 ✓）。</summary>
    public sealed class LineageInfo
    {
        public string Id = "";
        /// <summary>自走链深度（根=0 ✓）；-1 = unknown（在环上 ⇒ 链无终点 ✗ 不猜 ✓）。</summary>
        public int Depth = -1;
        public bool HasDepth { get { return Depth >= 0; } }
        /// <summary>直接子会话 id（确定性排序：按发现序 ✓ 与输入边序一致）。</summary>
        public List<string> Children = new List<string>();
        /// <summary>血缘后代闭包（不含己 ✓ 任意深度 ✓ 环上按访问集截断）。</summary>
        public HashSet<string> Descendants = new HashSet<string>(StringComparer.Ordinal);
        public bool InCycle;
        public bool IsOrphan;           // 父不在已知会话集 ⇒ 只进全局（§B.3 ✓）
    }

    /// <summary>整库血缘图 + 异常清单（纯数据 ✓）。</summary>
    public sealed class LineageGraph
    {
        /// <summary>参加建图的会话（= 有血缘边且非多父）。key = 会话 id。</summary>
        public Dictionary<string, LineageInfo> Nodes = new Dictionary<string, LineageInfo>(StringComparer.Ordinal);
        /// <summary>环上成员 id（每个打一行 SESSAGG_GUARD cycle=&lt;id&gt; ✓）。</summary>
        public List<string> CycleIds = new List<string>();
        /// <summary>孤儿边（child, parent）：父不在已知会话集（§B.3 ✓ 只进全局 ✓）。</summary>
        public List<KeyValuePair<string, string>> Orphans = new List<KeyValuePair<string, string>>();
        /// <summary>fork 数 = 有父 + origin!="subagent" + 非孤儿 + 非多父（§B.5 差值文案的 n ✓
        /// —— 文案说「它们的 parentSession 指向别的会话但 origin 不是 subagent」⇒ 只数**配得上这句解释**的会话；
        /// 孤儿/多父由各自的 stderr 行单独报告 ✓ 互不吞并 ✓）。</summary>
        public int ForkCount;
    }

    /// <summary>血缘闭包计算（纯函数 ✓ 零 IO/零时钟 ✓✓ 看板第三批 §11.7-B）。
    /// 防御纪律（§B.3 逐字）：多父拒绝归类 · 环访问集停止 · 孤儿只进全局 · 任意深度用显式栈 ✗ 递归 · 闭包后序累加+记忆化。</summary>
    public static class SessionLineage
    {
        /// <summary>子代理判定（唯一来源 ✓ GUI/CLI 都不许另写一份 ✓）。</summary>
        public static bool IsSubAgentOrigin(string origin)
        {
            return origin == "subagent";
        }

        /// <summary>建图。edges = 全部血缘边；knownIds = 已知会话集（投影 ∪ lineage 键 ∪ 面板会话 —— 由调用方给全 ✓）。
        /// 输入边里 id 重复 ⇒ 后到者忽略（快照同 id 双写 = 数据已坏 ✗ 不猜哪条对 ✓ 按先到为准 + 不炸 ✓）。</summary>
        public static LineageGraph Build(IList<LineageEdge> edges, ISet<string> knownIds)
        {
            LineageGraph g = new LineageGraph();
            if (edges == null) return g;
            // —— 第一遍：收合法节点（多父 ⇒ 拒绝归类 ✓ 不进图 ✓ 由调用方读 edge.Multiparent 打 GUARD 行 ✓）——
            for (int i = 0; i < edges.Count; i++)
            {
                LineageEdge e = edges[i];
                if (e == null || e.Id.Length == 0 || e.Multiparent) continue;
                if (g.Nodes.ContainsKey(e.Id)) continue;
                LineageInfo n = new LineageInfo();
                n.Id = e.Id;
                g.Nodes[e.Id] = n;
            }
            // —— 第二遍：挂边 + 孤儿/fork 归类 ——
            Dictionary<string, LineageEdge> byId = new Dictionary<string, LineageEdge>(StringComparer.Ordinal);
            for (int i = 0; i < edges.Count; i++)
            {
                LineageEdge e = edges[i];
                if (e == null || e.Id.Length == 0 || e.Multiparent || !g.Nodes.ContainsKey(e.Id)) continue;
                if (byId.ContainsKey(e.Id)) continue;
                byId[e.Id] = e;
                if (e.Parent.Length == 0) continue;   // 根 ✓
                bool parentKnown = knownIds != null && knownIds.Contains(e.Parent);
                if (!parentKnown)
                {
                    // 孤儿（§B.3 ✓）：父不在本库 ⇒ 只进全局 ⇒ 不挂边（挂了会把外部父拉进闭包 ✗）
                    g.Nodes[e.Id].IsOrphan = true;
                    g.Orphans.Add(new KeyValuePair<string, string>(e.Id, e.Parent));
                    continue;
                }
                if (!IsSubAgentOrigin(e.Origin)) g.ForkCount++;   // §B.5 的 n（纯 fork ✓ 孤儿已在上面分流 ✓）
                LineageInfo parentNode;
                if (g.Nodes.TryGetValue(e.Parent, out parentNode)) parentNode.Children.Add(e.Id);
                // 父只有血缘边、不在投影面板 ⇒ parentNode 不存在 ✓ 但边仍记到子节点上（深度可走链 ✓ 闭包从父侧用不到 ✓）
            }
            // —— 深度：自走 parent 链（显式栈/迭代 ✗ 递归 ✓）；撞环 ⇒ 路径上全员 InCycle + depth 留 -1 ——
            Dictionary<string, int> depthMemo = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < edges.Count; i++)
            {
                LineageEdge e = edges[i];
                if (e == null || !g.Nodes.ContainsKey(e.Id)) continue;
                DepthOf(e.Id, byId, g, depthMemo);
            }
            // —— 后代闭包：后序累加 + 记忆化（显式栈 ✓ 环由访问集截断 ✓）——
            Dictionary<string, HashSet<string>> memo = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, LineageInfo> kv in g.Nodes)
            {
                kv.Value.Descendants = ClosureOf(kv.Key, g, memo);   // ★ 算完必须写回节点（第三批契约测试抓到的漏写 ✗✓）
            }
            return g;
        }

        /// <summary>迭代求深度；环检测 = 本次路径访问集。撞环 ⇒ 路径上所有节点 InCycle（它们的 depth 都不可信 ✓）。</summary>
        private static int DepthOf(string id, Dictionary<string, LineageEdge> byId, LineageGraph g, Dictionary<string, int> memo)
        {
            int m;
            if (memo.TryGetValue(id, out m)) return m;
            List<string> path = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string cur = id;
            int depth = -1;
            while (true)
            {
                if (memo.TryGetValue(cur, out m)) { depth = m + 1; break; }
                if (seen.Contains(cur))
                {
                    // 环（§B.3 ✓）：访问集停止 ⇒ 本次路径全员标环 + 记 GUARD 清单（去重 ✓）
                    for (int i = 0; i < path.Count; i++)
                    {
                        LineageInfo n;
                        if (g.Nodes.TryGetValue(path[i], out n)) n.InCycle = true;
                        if (!g.CycleIds.Contains(path[i])) g.CycleIds.Add(path[i]);
                    }
                    LineageInfo self;
                    if (g.Nodes.TryGetValue(cur, out self)) self.InCycle = true;
                    if (!g.CycleIds.Contains(cur)) g.CycleIds.Add(cur);
                    depth = -1;
                    break;
                }
                seen.Add(cur);
                path.Add(cur);
                LineageInfo node;
                if (!g.Nodes.TryGetValue(cur, out node) || node.IsOrphan) { depth = 0; break; }  // 孤儿 ⇒ 按根对待（全局口径 ✓ depth 0 = 它自己能走到的顶端 ✓）
                LineageEdge e;
                if (!byId.TryGetValue(cur, out e) || e.Parent.Length == 0) { depth = 0; break; }
                cur = e.Parent;
            }
            // 回写路径（depth<0 = 环 ⇒ 全部留 unknown ✓）
            // ★ 必须**倒序回写**（2026-10-09 验收④真实库抓到的顺序依赖 bug ✗✓）：
            //   path[0] = 查询起点（最深）· path[last] = 最浅 —— 终值 depth 属于 path[last]，
            //   从末端往起点**递增**才对；正序回写会在「子先于父入 memo」时把深度整链**颠倒**
            //   （真实库目录序不保证父先子后 ⇒ 183 个 depth=2 被错算 ✗ 独立 JS 引擎对拍抓出 ✓）。
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (depth >= 0)
                {
                    memo[path[i]] = depth;
                    LineageInfo n;
                    if (g.Nodes.TryGetValue(path[i], out n)) n.Depth = depth;
                    depth++;
                }
            }
            LineageInfo first;
            return memo.TryGetValue(id, out m) ? m : (g.Nodes.TryGetValue(id, out first) ? first.Depth : -1);
        }

        /// <summary>后代闭包（后序 + 记忆化 ✓）。环成员：访问集截断 ⇒ 闭包只含截断前可达节点（诚实地报残缺 ✓ GUARD 行已打 ✓）。</summary>
        private static HashSet<string> ClosureOf(string id, LineageGraph g, Dictionary<string, HashSet<string>> memo)
        {
            HashSet<string> hit;
            if (memo.TryGetValue(id, out hit)) return hit;
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            // 显式栈 DFS（✗ 递归 ✓）；visiting = 当前 DFS 栈上的节点（防环 ✓）
            List<KeyValuePair<string, int>> stack = new List<KeyValuePair<string, int>>();   // (节点, 下一个子索引)
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            LineageInfo start;
            if (!g.Nodes.TryGetValue(id, out start)) { memo[id] = result; return result; }
            stack.Add(new KeyValuePair<string, int>(id, 0));
            visiting.Add(id);
            while (stack.Count > 0)
            {
                int top = stack.Count - 1;
                KeyValuePair<string, int> frame = stack[top];
                LineageInfo node = g.Nodes[frame.Key];
                if (frame.Value < node.Children.Count)
                {
                    // 进子节点前先把子节点本体并进结果 ✓
                    string child = node.Children[frame.Value];
                    stack[top] = new KeyValuePair<string, int>(frame.Key, frame.Value + 1);
                    if (!g.Nodes.ContainsKey(child)) continue;
                    if (visiting.Contains(child)) continue;   // 环截断 ✓
                    result.Add(child);
                    stack.Add(new KeyValuePair<string, int>(child, 0));
                    visiting.Add(child);
                }
                else
                {
                    // 后序：子树全部走完 ⇒ 把各子的记忆化闭包并进结果 ✓
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        HashSet<string> sub;
                        if (memo.TryGetValue(node.Children[i], out sub))
                        {
                            foreach (string d in sub) result.Add(d);
                        }
                    }
                    visiting.Remove(frame.Key);
                    stack.RemoveAt(top);
                }
            }
            result.Remove(id);   // 闭包**永不含己**（字段注释口径 ✓）：环成员经由邻居的记忆化闭包会绕回自己 ⇒ 剔除 ✓（第三批契约测试抓 ✓）
            memo[id] = result;
            return result;
        }

        /// <summary>层级筛选（§B 口径写死 ✓ 第三批 §11.7-D ✓）。
        /// global       = 全部会话 ✓
        /// parents      = 仅父会话（无 parentSession ✓ 血缘未知/多父/孤儿 ⇒ 无法证明 ⇒ 排除 ✗ 不猜 ✓）
        /// parents_sub  = 全部 −（有父 且 origin!="subagent"）− 孤儿 − 多父 − 血缘未知
        ///              = 根 + 非孤儿子代理（fork 不算子代理 ✗ 但计入父的 own+sub ✓ §B 原话 ✓）。</summary>
        public static bool LevelAccept(string level, LineageEdge edge, LineageInfo node)
        {
            if (level == "global") return true;
            if (edge == null || edge.Multiparent) return false;      // 血缘未知/多父 ⇒ 两档血缘口径都排除 ✓
            if (level == "parents") return edge.Parent.Length == 0;
            if (level == "parents_sub")
            {
                if (edge.Parent.Length == 0) return true;                                   // 根 ✓
                if (node != null && node.IsOrphan) return false;                            // 孤儿 ⇒ 只进全局 ✓
                return IsSubAgentOrigin(edge.Origin);                                       // 非孤儿子代理 ✓（fork ✗）
            }
            return false;
        }
    }
}
