# PgPlanCheck 機能実装プラン（ロードマップ）

作成日: 2026-06-07
対象: `C:\Users\machi\PgPlanCheck`（.NET 10 / Npgsql、SQL 実行プラン検証ツール）

現状のプロトタイプを土台に、機能を体系化した実装プラン。レビューのうえ、着手範囲・優先順位を決定する。

> 一部の機能はサーバの PostgreSQL バージョンに依存する（例: パラメータ化 SQL の `EXPLAIN (GENERIC_PLAN)` は PG16+、CTE インライン化は PG12+）。

---

## 現状（プロトタイプ）の整理

- 単一 `PlanAnalyzer` の switch 文でルールを実装している。→ ✅ **A1 で `IPlanRule` ＋レジストリに分解済み。**
- `BUFFERS` は取得済みだが未活用。→ ✅ **A2 で活用済み。**
- カタログ（`pg_indexes` / `pg_stats`）は未参照。→ ✅ **B1 で参照するよう実装済み。**
- 出力はコンソールのみ。→ ✅ **D1 で JSON/Markdown/SARIF/JUnit を追加済み。**
- 自動テストなし。→ ✅ **E1 で xUnit を追加済み。**

これを踏まえ、**①解析の拡張 → ②提案の高度化 → ③入出力 → ④CI 連携 → ⑤製品化** の 5 系統で整理する。

規模感の目安: **S** ≒ 半日 / **M** ≒ 1〜2 日 / **L** ≒ 数日。優先度: ◎ 高 / ○ 中 / △ 低。

---

## A. 解析エンジンの拡張（中核）

| ID | 機能 | 目的・効果 | 実装方針 | 主な変更 | 規模 | 優先 |
|---|---|---|---|---|---|---|
| A1 | **ルールのプラグイン化** ✅ 済み | 拡張・テスト・ON/OFF を容易に | `IPlanRule` インターフェース＋レジストリへ `PlanAnalyzer` を分解。各ルール 1 クラス | `PlanAnalyzer.cs` 分割, `Rules/*` 新規 | M | ◎ |
| A2 | **BUFFERS / 一時 I/O 解析** ✅ 済み | キャッシュミス・temp ファイル I/O を可視化（取得済みデータの活用） | `Shared Read` / `Temp Read/Written Blocks` を集計しルール化 | `ExplainModels.cs`, 新ルール | S | ◎ |
| A3 | **サージ可能性チェック** ✅ 済み | 索引が効かない書き方を検出 | 型不一致キャスト（`int=text`）、列への関数適用、`LIKE '%x'` 先頭ワイルドカードを Filter/Index Cond から判定 | 新ルール, `FilterColumnExtractor` 拡張 | M | ◎ |
| A4 | 追加ルール群 ✅ 済み | 検出網羅性向上 | 相関サブプラン多数実行 / Index-Only Scan の Heap Fetches（VACUUM 提案）/ 並列ワーカー不足（planned>launched）/ JIT オーバーヘッド / トリガ時間（DML）/ 直積（結合条件なし Nested Loop） | 新ルール複数 | L | ○ |
| A5 | 推定乖離の高度化 ✅ 済み | 誤検知削減・原因特定 | 祖先方向の LIMIT 考慮、相関列の特定 → `CREATE STATISTICS` を列名つきで提案 | `PlanAnalyzer`, 祖先追跡 | M | ○ |

## B. インデックス提案 / What-if の高度化

| ID | 機能 | 目的・効果 | 実装方針 | 規模 | 優先 |
|---|---|---|---|---|---|
| B1 | **カタログ参照** ✅ 済み | 重複提案の抑止・精度向上 | `pg_indexes` / `pg_stats`（n_distinct, correlation）を取得し、既存索引との重複回避・選択度評価 | M | ◎ |
| B2 | **複合索引の列順最適化** ✅ 済み | 実用的な DDL 生成 | 等値 → 範囲 → ORDER BY の順、`INCLUDE` で Index-Only 化を提案 | M | ○ |
| B3 | **HypoPG 連携** ✅ 済み | 大表でも瞬時・ゼロ書込で What-if | 拡張があれば仮想索引、無ければ現行（Tx 内実作成 → ROLLBACK）にフォールバック | M | ○ |
| B4 | **ワークロード索引アドバイザ** ✅ 済み | 複数 SQL 横断で最適索引集合を提案 | 全ファイルの提案を集約・重複統合・効果順に統合 DDL スクリプト出力 | L | △ |

## C. 入力・ワークフロー

| ID | 機能 | 目的・効果 | 実装方針 | 規模 | 優先 |
|---|---|---|---|---|---|
| C1 | **複数入力**（ディレクトリ/glob/stdin/`--sql`） ✅ 済み | 実運用の一括検査 | 入力抽象化＋バッチ集計 | S | ◎ |
| C2 | **パラメータ化 SQL 対応** ✅ 済み（--param） | `$1` を含む SQL の検証 | `EXPLAIN (GENERIC_PLAN)`（PG16+）or `--param` 値指定 or PREPARE | M | ○ |
| C3 | セットアップ文の同一 Tx 実行 | 一時表/SET 前提のクエリ検証 | 先行 DDL/SET を同じ ROLLBACK Tx 内で適用してから解析 | S | △ |
| C4 | **接続プロファイル/設定ファイル**（ルール選択 ✅ 一部済み） | 環境切替・閾値/ルール設定 | `appsettings.json` / YAML, `.pgpass` / env, SSL 対応 | M | ○ |

## D. 出力・CI 連携

| ID | 機能 | 目的・効果 | 実装方針 | 規模 | 優先 |
|---|---|---|---|---|---|
| D1 | **構造化出力**（JSON/Markdown/SARIF/JUnit） ✅ 済み | CI・他ツール連携 | `IReportWriter` 抽象化＋`--format` | M | ◎ |
| D2 | **CI ゲート** ✅ 済み | PR で性能劣化を阻止 | `--fail-on warning`、ベースライン（既知問題の除外）、GitHub アノテーション | S | ○ |
| D3 | プラン可視化 | レビュー共有 | Graphviz / draw.io 図 or explain 系フォーマット出力 | M | △ |

## E. 品質・製品化

| ID | 機能 | 目的・効果 | 実装方針 | 規模 | 優先 |
|---|---|---|---|---|---|
| E1 | **単体テスト（DB 不要）** ✅ 済み | 安心して拡張（A1 の前提相棒） | 固定 EXPLAIN JSON フィクスチャ → Finding 検証（xUnit） | M | ◎ |
| E2 | 結合テスト | 実挙動の回帰防止 | Testcontainers で使い捨て PG | M | △ |
| E3 | **グローバルツール化** | 配布容易化 | `dotnet tool`（`pgplancheck`）/ 単一ファイル発行 | S | ○ |
| E4 | 多言語化 / ロギング / 堅牢化 | 運用品質 | メッセージ資源化（ja/en）、タイムアウト/キャンセル、巨大プラン対応 | M | △ |

---

## 推奨フェーズ分割

- ✅ **Phase 1 — 足場と即効ルール（実装済み 2026-06-07）**: A1（プラグイン化）＋ E1（テスト）を相棒で入れ、A2 / A3 と B1 を追加。最小コストで検出精度が大きく向上。→ 詳細は「Phase 1 実装メモ」を参照。
- ✅ **Phase 2 — 実運用ワークフロー（実装済み 2026-06-07）**: C1（一括）＋ D1（JSON/SARIF）＋ D2（CI ゲート）。チーム導入できる形に。→ 詳細は「Phase 2 実装メモ」を参照。
- ✅ **Phase 3 — 提案の高度化（実装済み 2026-06-07）**: B2 / B3 / B4、A4 / A5、C2。→ 詳細は「Phase 3 実装メモ」を参照。
- **Phase 4 — 製品化**: E3（ツール化）、C4（設定）、D3、E2 / E4。

---

## Phase 1 実装メモ（2026-06-07 実装済み）

A1 / E1 / A2 / A3 / B1 を実装した。ビルド・テストともにグリーン（29 ケース）。

### 追加・変更
- ✅ **A1 ルールのプラグイン化**: `PlanAnalyzer` を「登録ルールを実行して指摘を集約するオーケストレータ」に変更。検出ロジックは `Rules/` 配下の各 `IPlanRule` 実装へ 1 クラス 1 責務で分割し、`RuleRegistry.Default` に登録。`PlanContext`（プラン木・カタログ・LIMIT 有無）をルールへ受け渡す。ルール集合は差し替え可能（ON/OFF・テスト）。
- ✅ **A2 BUFFERS / 一時 I/O**: `ExplainModels` に Shared/Temp ブロックと I/O 時間を追加。`BufferUsageRule` が一時ファイル spill（work_mem 超過）とディスク読み込み過多（キャッシュミス）を集計（`--analyze` 時のみ）。
- ✅ **A3 サージ可能性（sargability）**: `FilterColumnExtractor.DetectSargability` で「型キャスト・列への関数適用・先頭ワイルドカード LIKE」を検出。`SargabilityRule` が指摘し、Seq Scan ルールは非効率条件のとき索引提案を A3 に委譲。構造判定のため検証専用モードでも動作。
- ✅ **B1 カタログ参照**: `Catalog/` に `ICatalog` / `CatalogSnapshot` / `PgCatalogLoader` を追加。`pg_index` / `pg_stats` を読み出し、既存インデックスと重複する索引提案を抑止し、列統計（n_distinct）で選択度を補足・低選択度の等値条件には部分インデックスを提示。取得失敗時は解析を継続。
- ✅ **E1 単体テスト**: `tests/PgPlanCheck.Tests`（xUnit、DB 不要）。固定 EXPLAIN JSON フィクスチャで各ルール・委譲・カタログ連携を検証。

### ルール一覧（`IPlanRule.Id`）
`seq-scan` / `sargability`(A3) / `external-sort` / `hash-batches` / `bitmap-lossy` / `index-filter` / `nested-loop` / `buffer-usage`(A2) / `estimation-error` / `bottleneck`

### テスト実行
```powershell
dotnet test tests/PgPlanCheck.Tests
```

### 既知の制約
- 列・sargability 抽出は正規表現ベースのため、複数引数関数の非先頭列や複雑な式は取りこぼす場合がある。
- カタログのインデックス列は通常列のみ（式インデックスの式部分は対象外）。

---

## Phase 2 実装メモ（2026-06-07 実装済み）

C1 / D1 / D2 を実装した。ビルド・テストともにグリーン（52 ケース）。実機（192.168.100.90/test）で JSON/SARIF 出力・CI ゲート（exit 1）・GitHub アノテーションを確認済み。

### 追加・変更
- ✅ **C1 複数入力**: `Input/InputResolver` がファイル / ディレクトリ（再帰 *.sql）/ glob（`*` `?`）/ stdin（`-`,`--stdin`）/ インライン（`--sql`、複数可）を `SqlSource` 一覧へ展開。全入力をバッチ処理する。
- ✅ **D1 構造化出力**: `Reporting/` に `AnalysisReport` モデルと `IReportWriter`＋`--format`（console/json/markdown/sarif/junit）。処理は `AnalysisEngine` に集約し、console は従来どおり色付き逐次出力（+What-if）、構造化形式は標準出力（または `-o` でファイル）へ。人間向けメッセージは構造化時に標準エラーへ退避し、stdout をレポート専用にする。
- ✅ **D2 CI ゲート**: `--fail-on none|info|warning|critical`（既定 warning。検証エラーも失敗扱い）。`--baseline <file>`（フィンガープリント JSON）で既知問題を除外、`--write-baseline <file>` でスナップショット。`--github-annotations` で `::warning::`/`::error::`/`::notice::` を出力。
- ✅ **E1 追加テスト**: 各ライター（JSON/SARIF/JUnit/Markdown）・CI ゲート・フィンガープリント/ベースライン往復・入力解決を DB 非依存で検証（合計 52 ケース）。

### 新オプション
`--format` / `-o,--output` / `--no-color` / `--fail-on` / `--baseline` / `--write-baseline` / `--github-annotations` / `--sql` / `--stdin`(`-`)

### CI 例
```powershell
# SQL をディレクトリ一括で検査し、警告以上で失敗。SARIF を成果物として出力。
dotnet run -- sql/ --analyze --format sarif -o pgplancheck.sarif --fail-on warning
```

### 既知の制約
- glob は単一ディレクトリ内の `*`/`?`（`**` 再帰は未対応。ディレクトリ指定なら再帰 *.sql）。
- What-if は console 形式のみ（構造化出力には含めない）。

---

## Phase 3 実装メモ（2026-06-07 実装済み）

A4 / A5 / B2 / B3 / B4 / C2 を実装した。ビルド・テストともにグリーン（65 ケース）。実機（192.168.100.90/test）で B2 の列順最適化・B4 の統合推奨・C2（--param）を確認済み。

### 追加・変更
- ✅ **A4 追加ルール群**: `Rules/` に 6 ルールを追加 — 直積（`cartesian-join`）/ 相関サブプラン（`correlated-subplan`）/ Index-Only の Heap Fetches（`index-only-heap-fetch`→VACUUM 提案）/ 並列ワーカー不足（`parallel-workers`）/ JIT オーバーヘッド（`jit-overhead`）/ トリガ時間（`trigger-time`）。`ExplainModels` に JIT・Triggers・Join Filter・Output を追加。
- ✅ **A5 推定乖離の高度化**: 過大推定の除外を「LIMIT が当該ノードの祖先にある場合」に限定（`PlanContext.Ancestors`）。過小推定かつ複数列条件なら列相関とみなし `CREATE STATISTICS (dependencies, ndistinct) ON ...` を列名つきで提案。
- ✅ **B2 複合索引の列順最適化**: `CompositeIndexAdvisor` で「等値 → 範囲 → ORDER BY」順にキー列を生成（`FilterColumnExtractor.ClassifyPredicates`）。出力列は `INCLUDE` 候補として Index-Only 化を提示。Seq Scan 提案へ統合。
- ✅ **B3 HypoPG 連携**: `PgExplainRunner.HasHypoPgAsync` / `RunWithHypotheticalAsync`。What-if 時に拡張があれば仮想索引で推定（ゼロ書込・コストのみ）、無ければ現行（Tx 内実作成→ROLLBACK）へフォールバック。
- ✅ **B4 ワークロード索引アドバイザ**: `WorkloadAdvisor` が全 SQL の索引提案を集約・接頭辞統合し、出現回数順に提示。console / JSON / Markdown に「推奨インデックス」を出力、`--advice-sql <file>` で適用スクリプトを書き出し。
- ✅ **C2 パラメータ化 SQL**: `--param <値>`（複数可）を `$1, $2, …` に順番でバインド（`NpgsqlDbType.Unknown` で対象列の型へ強制）。実測（--analyze）も可能。未指定時は案内メッセージを表示して当該文をスキップ。
- **堅牢化（副産物）**: EXPLAIN 失敗時のロールバックを `catch` 内で握りつぶすよう修正。プロトコルエラーでもクラッシュせず「検証エラー」として記録するようにした。

### ルール一覧（Phase 1–3, `IPlanRule.Id`）
`seq-scan` / `sargability` / `external-sort` / `hash-batches` / `bitmap-lossy` / `index-filter` / `nested-loop` / `cartesian-join` / `correlated-subplan` / `index-only-heap-fetch` / `parallel-workers` / `jit-overhead` / `trigger-time` / `buffer-usage` / `estimation-error` / `bottleneck`

### 既知の制約
- **C2 GENERIC_PLAN（値なし推定）は未提供**: Npgsql は単一文に simple プロトコルを使わず `$n` にバインドを要求するため、値なしの汎用プラン取得ができなかった。現状は `--param` で値を与える方式に統一。将来 simple プロトコル経路が用意できれば PG16+ で対応予定。
- **B3 HypoPG** は拡張未導入の環境ではフォールバック（実索引作成→ROLLBACK）で動作。
- B2 の列抽出・B4 の DDL 解析は正規表現ベースのため、複雑な式・関数索引は対象外。

---

## 追加対応メモ（2026-06-07）

- ✅ **Cobertura 出力**: `--format cobertura` を追加（D1 拡張）。各 SQL 文を 1 行に見立て、警告/重大/検証エラーの無い文を「カバー済み(hits=1)」とし、CI（GitLab/Jenkins）で SQL 健全度をカバレッジとして可視化できる。既存の `junit` と合わせて CI レポートに利用可能。
- ✅ **ルール設定ファイル**: `--config <file>`（JSON）で使用ルールを選択できる（C4 の設定ファイルの一部）。形式は `{ "rules": { "enabled": [...], "disabled": [...] } }`。`enabled` 指定時はホワイトリスト、未指定時は全ルールから `disabled` を除外。未知 ID は警告。`RuleRegistry.Resolve` → `new PlanAnalyzer(rules)` に注入し、A1 の ON/OFF を実運用化した。

---

## 確認したい論点（着手指示の材料）

1. **主用途**はどれを重視するか（優先度が変わる）。
   - (a) CI で性能劣化を止める「ゲート」 → D1/D2/C1/E1 を最優先
   - (b) 開発者/DBA の「対話的チューニング支援」 → A2/A3/B1/B3 を最優先
   - (c) 既存大量 SQL の「棚卸し・索引設計」 → C1/B4 を最優先
2. **製品化の度合い**: 社内ツールとして配布（E3/E1 必須）か、当面プロトタイプ拡張か。
3. **対象 PG バージョン**（複数可）: GENERIC_PLAN（C2）・HypoPG（B3）・JIT/CTE 系ルールの可否に影響。確認環境の実バージョンは採取可能。
4. **出力形式の要否**: CI 連携なら SARIF / JUnit / Markdown のどれが必要か。

> 着手範囲（例:「Phase 1 の A1+E1+A2 から」）や上記論点への回答が決まり次第、詳細な実装プラン（設計・タスク分解・着手順）を作成する。
