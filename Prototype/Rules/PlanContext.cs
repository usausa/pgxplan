using System.Collections.Generic;
using System.Linq;
using PgPlanCheck.Catalog;

namespace PgPlanCheck.Rules;

/// <summary>プラン木の 1 ノードと、その親への参照。</summary>
public readonly record struct NodeRef(PlanNode Node, PlanNode? Parent);

/// <summary>
/// 各ルールへ渡す解析コンテキスト。プラン木を平坦化したノード列と、
/// 解析に必要な付随情報（実測モードか・カタログ・LIMIT 有無）をまとめて保持する。
/// </summary>
public sealed class PlanContext
{
    /// <summary>EXPLAIN の最上位要素。</summary>
    public required ExplainRoot Root { get; init; }

    /// <summary>--analyze 実行か（実測値が利用可能か）。</summary>
    public required bool Analyzed { get; init; }

    /// <summary>プラン木を先行順で平坦化したノード列（親参照つき）。</summary>
    public required IReadOnlyList<NodeRef> Nodes { get; init; }

    /// <summary>既存インデックス・統計を参照するカタログ（未取得時は <see cref="NullCatalog"/>）。</summary>
    public ICatalog Catalog { get; init; } = NullCatalog.Instance;

    /// <summary>プランに LIMIT を含むか（過大推定の誤検知抑制に使う）。</summary>
    public bool PlanHasLimit { get; init; }

    /// <summary>各ノード → 親ノードの対応（祖先方向の探索に使う）。</summary>
    public required IReadOnlyDictionary<PlanNode, PlanNode?> Parents { get; init; }

    /// <summary>指定ノードの祖先を、親から根へ向かって列挙する。</summary>
    public IEnumerable<PlanNode> Ancestors(PlanNode node)
    {
        var cur = Parents.TryGetValue(node, out var p) ? p : null;
        while (cur is not null)
        {
            yield return cur;
            cur = Parents.TryGetValue(cur, out var pp) ? pp : null;
        }
    }

    /// <summary>ルート要素からコンテキストを構築する。</summary>
    public static PlanContext Build(ExplainRoot root, bool analyzed, ICatalog? catalog = null)
    {
        var nodes = PlanWalker.Walk(root.Plan).ToList();
        var parents = new Dictionary<PlanNode, PlanNode?>(ReferenceEqualityComparer.Instance);
        foreach (var nr in nodes) parents[nr.Node] = nr.Parent;

        return new PlanContext
        {
            Root = root,
            Analyzed = analyzed,
            Nodes = nodes,
            Catalog = catalog ?? NullCatalog.Instance,
            PlanHasLimit = nodes.Any(n => n.Node.NodeType == "Limit"),
            Parents = parents,
        };
    }
}

/// <summary>プラン木を先行順（親→子）で走査するユーティリティ。</summary>
public static class PlanWalker
{
    public static IEnumerable<NodeRef> Walk(PlanNode node, PlanNode? parent = null)
    {
        yield return new NodeRef(node, parent);
        if (node.Plans is null) yield break;
        foreach (var child in node.Plans)
            foreach (var d in Walk(child, node))
                yield return d;
    }
}
