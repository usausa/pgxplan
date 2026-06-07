using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PgPlanCheck;

/// <summary>EXPLAIN (FORMAT JSON) の最上位要素。配列で返るためその 1 要素に対応。</summary>
public sealed class ExplainRoot
{
    [JsonPropertyName("Plan")] public PlanNode Plan { get; set; } = new();
    [JsonPropertyName("Planning Time")] public double? PlanningTime { get; set; }
    [JsonPropertyName("Execution Time")] public double? ExecutionTime { get; set; }

    [JsonPropertyName("JIT")] public JitInfo? Jit { get; set; }
    [JsonPropertyName("Triggers")] public List<TriggerInfo>? Triggers { get; set; }
}

/// <summary>JIT コンパイルの集計（--analyze 時。閾値超過のクエリで出力される）。</summary>
public sealed class JitInfo
{
    [JsonPropertyName("Functions")] public int? Functions { get; set; }
    [JsonPropertyName("Timing")] public JitTiming? Timing { get; set; }
}

public sealed class JitTiming
{
    [JsonPropertyName("Generation")] public double? Generation { get; set; }
    [JsonPropertyName("Inlining")] public double? Inlining { get; set; }
    [JsonPropertyName("Optimization")] public double? Optimization { get; set; }
    [JsonPropertyName("Emission")] public double? Emission { get; set; }
    [JsonPropertyName("Total")] public double? Total { get; set; }
}

/// <summary>トリガ実行の集計（DML を --analyze したとき）。</summary>
public sealed class TriggerInfo
{
    [JsonPropertyName("Trigger Name")] public string? Name { get; set; }
    [JsonPropertyName("Relation")] public string? Relation { get; set; }
    [JsonPropertyName("Time")] public double? Time { get; set; }   // ms
    [JsonPropertyName("Calls")] public double? Calls { get; set; }
}

/// <summary>実行プランの 1 ノード。EXPLAIN JSON のキー名に合わせてマッピングする。</summary>
public sealed class PlanNode
{
    [JsonPropertyName("Node Type")] public string NodeType { get; set; } = "";
    [JsonPropertyName("Parent Relationship")] public string? ParentRelationship { get; set; }
    [JsonPropertyName("Join Type")] public string? JoinType { get; set; }
    [JsonPropertyName("Relation Name")] public string? RelationName { get; set; }
    [JsonPropertyName("Schema")] public string? Schema { get; set; }
    [JsonPropertyName("Alias")] public string? Alias { get; set; }
    [JsonPropertyName("Index Name")] public string? IndexName { get; set; }

    [JsonPropertyName("Startup Cost")] public double StartupCost { get; set; }
    [JsonPropertyName("Total Cost")] public double TotalCost { get; set; }
    [JsonPropertyName("Plan Rows")] public double PlanRows { get; set; }
    [JsonPropertyName("Plan Width")] public int PlanWidth { get; set; }

    [JsonPropertyName("Actual Startup Time")] public double? ActualStartupTime { get; set; }
    [JsonPropertyName("Actual Total Time")] public double? ActualTotalTime { get; set; }
    [JsonPropertyName("Actual Rows")] public double? ActualRows { get; set; }
    [JsonPropertyName("Actual Loops")] public double? ActualLoops { get; set; }

    [JsonPropertyName("Filter")] public string? Filter { get; set; }
    [JsonPropertyName("Index Cond")] public string? IndexCond { get; set; }
    [JsonPropertyName("Join Filter")] public string? JoinFilter { get; set; }
    [JsonPropertyName("Output")] public List<string>? Output { get; set; }
    [JsonPropertyName("Rows Removed by Filter")] public double? RowsRemovedByFilter { get; set; }
    [JsonPropertyName("Rows Removed by Index Recheck")] public double? RowsRemovedByIndexRecheck { get; set; }

    [JsonPropertyName("Sort Key")] public List<string>? SortKey { get; set; }
    [JsonPropertyName("Sort Method")] public string? SortMethod { get; set; }
    [JsonPropertyName("Sort Space Used")] public double? SortSpaceUsed { get; set; }   // kB
    [JsonPropertyName("Sort Space Type")] public string? SortSpaceType { get; set; }

    [JsonPropertyName("Hash Batches")] public int? HashBatches { get; set; }
    [JsonPropertyName("Original Hash Batches")] public int? OriginalHashBatches { get; set; }
    [JsonPropertyName("Peak Memory Usage")] public int? PeakMemoryUsage { get; set; }  // kB

    [JsonPropertyName("Heap Fetches")] public double? HeapFetches { get; set; }
    [JsonPropertyName("Exact Heap Blocks")] public long? ExactHeapBlocks { get; set; }
    [JsonPropertyName("Lossy Heap Blocks")] public long? LossyHeapBlocks { get; set; }

    [JsonPropertyName("Workers Planned")] public int? WorkersPlanned { get; set; }
    [JsonPropertyName("Workers Launched")] public int? WorkersLaunched { get; set; }
    [JsonPropertyName("Subplan Name")] public string? SubplanName { get; set; }

    // ---- BUFFERS（--analyze 時のみ。値は子ノードを含む累計）----
    [JsonPropertyName("Shared Hit Blocks")] public long? SharedHitBlocks { get; set; }
    [JsonPropertyName("Shared Read Blocks")] public long? SharedReadBlocks { get; set; }
    [JsonPropertyName("Shared Dirtied Blocks")] public long? SharedDirtiedBlocks { get; set; }
    [JsonPropertyName("Shared Written Blocks")] public long? SharedWrittenBlocks { get; set; }
    [JsonPropertyName("Temp Read Blocks")] public long? TempReadBlocks { get; set; }
    [JsonPropertyName("Temp Written Blocks")] public long? TempWrittenBlocks { get; set; }
    [JsonPropertyName("I/O Read Time")] public double? IoReadTime { get; set; }    // ms
    [JsonPropertyName("I/O Write Time")] public double? IoWriteTime { get; set; }  // ms

    [JsonPropertyName("Plans")] public List<PlanNode>? Plans { get; set; }

    // ---- 解析用の派生プロパティ（JSON には含めない） ----

    [JsonIgnore] public double Loops => ActualLoops ?? 1d;

    /// <summary>対象オブジェクト名（スキーマ修飾、別名付き）。</summary>
    [JsonIgnore]
    public string? QualifiedRelation =>
        RelationName is null ? null
        : (Schema is null ? RelationName : $"{Schema}.{RelationName}");

    /// <summary>ノードを 1 行で説明するラベル。例: "Seq Scan on public.orders o"。</summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            var s = NodeType;
            if (JoinType is not null && NodeType.Contains("Join"))
                s = $"{JoinType} {NodeType}";
            if (QualifiedRelation is not null)
            {
                s += $" on {QualifiedRelation}";
                if (Alias is not null && Alias != RelationName) s += $" {Alias}";
            }
            else if (IndexName is not null)
            {
                s += $" using {IndexName}";
            }
            else if (SubplanName is not null)
            {
                s += $" ({SubplanName})";
            }
            return s;
        }
    }

    /// <summary>この 1 ノード単体の実行時間（ループ込み・子を含まない概算 ms）。</summary>
    [JsonIgnore]
    public double InclusiveTimeMs => (ActualTotalTime ?? 0d) * Loops;
}
