# PgPlanCheck（検証用プロトタイプ）

SQL ファイルを読み込み、指定 PostgreSQL で **EXPLAIN（実行プラン）** を取得して、
性能上の **警告** と **改善案** を出力するコンソールアプリです。

- **C# / .NET 10**, Npgsql 10
- **実 DB を更新しない**ことを前提に設計（後述）

---

## 1. 設計方針：DB を更新せず「検証だけ」する

EXPLAIN には 2 つの使い方があり、いずれも**実データを永続的に変更しません**。

| モード | フラグ | クエリ実行 | DB 更新 | 取得できる情報 |
|---|---|---|---|---|
| **検証専用**（既定） | （なし） | しない | 一切なし（READ ONLY Tx） | 構文・テーブル/列の存在・型チェック＋**推定**プラン |
| 実測 | `--analyze` | する→**ROLLBACK** | 永続化されない | **実測**行数・実時間・外部ソート/ハッシュ分割の実検出 |
| 効果試算 | `--what-if` | （+ 仮 index 作成→ROLLBACK） | 永続化されない | 提案インデックス適用後のコスト/時間 |

- **既定の「検証専用」** はプレーン `EXPLAIN`。クエリを *実行しない* ので SELECT すら走らず、副作用ゼロ。
  それでも EXPLAIN はプラン作成時に**構文・列の存在・型を検証**するため、SQL の誤りはここで検出できます。
- `--analyze` / `--what-if` は実行を伴いますが、必ずトランザクション内で `ROLLBACK` するため
  `INSERT/UPDATE/DELETE` や一時インデックスの作成も**永続化されません**。

---

## 2. ビルドと実行

```powershell
# ビルド
dotnet build

# テスト用データ（explain_demo スキーマ）とサンプル SQL を作成
dotnet run -- --init-demo

# 検証専用（実行なし・推定プランのみ）
dotnet run -- samples/01_missing_index.sql

# 実測（ANALYZE。ROLLBACK するため DB は不変）
dotnet run -- samples/01_missing_index.sql --analyze

# 提案インデックスの効果を試算（仮作成→ROLLBACK）
dotnet run -- samples/01_missing_index.sql --analyze --what-if

# 単体テスト（DB 不要・xUnit）
dotnet test tests/PgPlanCheck.Tests
```

### 接続オプション（既定は確認環境）

| オプション | 既定値 |
|---|---|
| `--host` | `192.168.100.90` |
| `--port` | `5432` |
| `--db`   | `test` |
| `--user` | `test` |
| `--password` | `test` |

終了コード: 重大/警告/検証エラーがあれば `1`、問題なしなら `0`（CI 判定に利用可）。`--fail-on none|info|warning|critical` でしきい値を調整できます。

### 入力・出力（C1 / D1 / D2）

- **複数入力**: ファイル / ディレクトリ（再帰 `*.sql`）/ glob（`samples/*.sql`）/ 標準入力（`-`）/ インライン（`--sql "..."`）。
- **構造化出力**: `--format console|json|markdown|sarif|junit|cobertura`、`-o <file>` でファイル出力（省略時は標準出力）。`cobertura` は各 SQL 文を 1 行に見立て、問題のない文を「カバー済み」として SQL 健全度をカバレッジ化（CI 連携用）。
- **CI ゲート**: `--baseline <file>`（既知問題の除外）、`--write-baseline <file>`、`--github-annotations`（GitHub Actions アノテーション）。
- **パラメータ化 SQL**: `$1` を含む文は `--param <値>` を順番に指定（例: `--param 12345`）。実測（`--analyze`）も可能。
- **索引アドバイス**: 複数 SQL 横断の統合インデックス推奨を `--advice-sql <file>` で SQL スクリプト出力（B4）。What-if は HypoPG 拡張があれば仮想索引で試算（B3）。
- **ルール選択**: `--config <file>`（JSON）で使うルールを有効/無効化。`enabled` 指定時はホワイトリスト、未指定なら全ルールから `disabled` を除外。

```jsonc
// rules.json — jit/トリガのルールを無効化する例
{ "rules": { "disabled": ["jit-overhead", "trigger-time"] } }
```

```powershell
# ディレクトリ一括 → SARIF を成果物に、警告以上で CI を失敗させる
dotnet run -- samples/ --analyze --format sarif -o report.sarif --fail-on warning
```

---

## 3. 検出ルール（ヒューリスティック）

| 種別 | 重大度 | 主な根拠 | 改善案 |
|---|---|---|---|
| シーケンシャルスキャン | 警告/重大 | Filter で大量行を除外する全件走査 | フィルタ列への `CREATE INDEX`（自動生成） |
| ディスク外部ソート | 警告 | `Sort Method: external …` | `work_mem` 増加／ORDER BY 列へのインデックス |
| ハッシュ結合のディスク分割 | 警告 | `Hash Batches > 1` | `work_mem` 増加 |
| ビットマップの lossy 化 | 警告 | `Lossy Heap Blocks > 0` | `work_mem` 増加 |
| 推定行数と実測の乖離 | 警告/情報 | 推定/実測が 10 倍以上（LIMIT 由来の過大推定は除外） | `ANALYZE` / 拡張統計 `CREATE STATISTICS` |
| ネステッドループ過多 | 警告/重大 | 内側の実行回数が多い | 結合キーへのインデックス／ハッシュ結合化 |
| インデックス後の大量フィルタ | 情報 | Index 走査後 Filter で大量除外 | フィルタ列を含む複合インデックス |
| インデックス非効率な書き方 | 警告 | 型不一致キャスト／列への関数適用／先頭ワイルドカード LIKE（A3） | 型を列に合わせる／式インデックス／`pg_trgm` GIN |
| 一時ファイル I/O・キャッシュミス | 警告/情報 | Temp ブロック書き出し／Shared Read 過多（BUFFERS, A2） | `work_mem` 増加／絞り込みインデックス |
| 直積・相関サブプラン他（A4） | 警告/情報 | 結合条件なし NL／相関 SubPlan 多数／Index-Only の Heap Fetches／並列不足／JIT／トリガ時間 | 結合条件の追加／JOIN 化／`VACUUM`／設定見直し |
| 最も時間を要したノード | 情報 | 自己時間の集中（`--analyze` 時） | 着手の優先順位付け |

`--analyze` 指定時のみ得られる情報（実測行数・実時間・ソート/ハッシュの実挙動）に依存するルールがあります。
検証専用モードでは推定値ベースで弱め（情報レベル）に提示します。

---

## 4. 検証結果（確認環境 192.168.100.90 / test）

`--init-demo` で `explain_demo.orders`（50 万件）/ `customers`（5 万件）を作成し、サンプルを実行。

- **01_missing_index**: `status='pending'`(約2%) の全件走査を検出 → `CREATE INDEX … (status)` 提案。
  `--what-if` で **コスト ▼22% / 実行時間 29.3ms→7.6ms（▼74%）** を試算（実 DB は不変を確認済み）。
- **02_join_and_filter**: `amount>9500` の全件走査を検出 → `CREATE INDEX … (amount)` 提案。
- **03_external_sort**: `note` 全件ソートで `external merge`（約 40MB をディスク使用）を検出 → `work_mem` 提案。
- **04_indexed_lookup**: 主キー検索（Index Scan）で **警告なし**（正常系）。
- **99_invalid**: 存在しない列を参照 → `42703 column "customrr_id" does not exist`（位置・ヒント付き）を検出。

---

## 5. 構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | エントリポイント／入力解決・出力・CI ゲートの全体フロー |
| `AnalysisEngine.cs` | 入力を順に EXPLAIN・解析し結果モデルを構築（A1/C1） |
| `Input/*.cs` | 複数入力の解決（ファイル/ディレクトリ/glob/stdin/--sql、C1） |
| `Reporting/*.cs` | 構造化出力（JSON/MD/SARIF/JUnit/Cobertura）・CI ゲート・ベースライン・ワークロード索引推奨（D1/D2/B4） |
| `Config/*.cs` | 設定ファイル（使用ルールの有効/無効）（C4 一部） |
| `AppOptions.cs` | 引数解析・接続文字列・ヘルプ |
| `SqlSplitter.cs` | SQL を文単位に分割（文字列/コメント/ドル引用符対応）・EXPLAIN 可否判定 |
| `PgExplainRunner.cs` | EXPLAIN 実行（Tx 内 ROLLBACK・READ ONLY・What-if 仮インデックス） |
| `ExplainModels.cs` | EXPLAIN JSON のモデル |
| `PlanAnalyzer.cs` | 登録ルールを実行して指摘を集約するオーケストレータ（A1） |
| `Rules/*.cs` | 各検出ルール（`IPlanRule`）・レジストリ・解析コンテキスト（A1） |
| `Catalog/*.cs` | 既存インデックス・列統計の参照（`pg_indexes`/`pg_stats`、B1） |
| `CompositeIndexAdvisor.cs` | 複合インデックスの列順最適化（等値→範囲→ORDER BY、INCLUDE）（B2） |
| `FilterColumnExtractor.cs` | Filter からインデックス対象列・sargability 非効率・述語種別を抽出（A3/B2） |
| `ReportPrinter.cs` | コンソール整形出力（色付き・プラン木・What-if 比較） |
| `DemoSeeder.cs` | テストデータ生成・サンプル SQL 出力 |
| `tests/PgPlanCheck.Tests` | 単体テスト（xUnit・DB 不要、E1） |

---

## 6. 後片付け / 既知の制約

- テストデータ削除: `DROP SCHEMA explain_demo CASCADE;`
- 制約（プロトタイプ）: 列抽出は正規表現ベースの簡易実装。複合条件・式インデックスの提案は限定的。
  パラメータ化クエリ（`$1` 等）の EXPLAIN は型解決のため別途対応が必要。並列プランの自己時間は概算。
