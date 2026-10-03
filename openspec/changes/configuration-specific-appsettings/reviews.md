# レビュー記録: configuration-specific-appsettings / propose

- **日時**: 2026-10-03
- **フェーズ**: propose
- **対象成果物**: `proposal.md`, `specs/companion-file-rename/spec.md`, `design.md`, `tasks.md`

---

## 1. User Agent レビュー

- **レビュアー**: User Agent
- **観点**: 要件妥当性・ビジネス価値・UX・受け入れ基準

### チェックリスト結果

| #   | 観点                                                      | 判定 | コメント                                                                                                                                                                                                          |
| --- | --------------------------------------------------------- | ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | What（何を変えるか）が明確かつ500語以内で記述されているか | ✅   | 環境別設定ファイル（`Development` / `Production`）の配置、MSBuild による常時全ファイルコピー、`Settings.fs` での環境解決とキー単位オーバーライドマージ、単体テスト追加・更新の4点が明瞭かつ簡潔にまとめられている |
| 2   | Why（なぜ変えるか・ビジネス価値）が明確か                 | ✅   | 開発時設定（ローカル検証パス等）のRelease成果物への混入リスク防止、Gitコミット時の機密・環境固有値の混入リスク防止、Microsoft公式の .NET 構成標準への準拠による開発生産性・運用性向上の価値が明確                 |
| 3   | Non-goals（やらないこと）が明記されているか               | ✅   | Webサーバー側（`src/TagBasedVideoManager`）の設定構造変更の除外、およびUI保存時の環境別ファイルへの逆書き込み除外が明確に境界づけられており、スコープの肥大化を防いでいる                                         |
| 4   | 受け入れ基準・完了の定義が具体的かつ検証可能か            | ✅   | デルタ仕様書において、Development/Production 各環境でのキー単位オーバーライド、未定義キーのベース維持、不在時の安全なフォールバック、環境変数指定時の挙動、ビルド時配置が WHEN/THEN 形式で網羅され検証可能        |

### 判定

- **LGTM**: true
- **次のアクション**: SE Agent レビューへ

---

## 2. SE Agent レビュー

- **レビュアー**: SE Agent
- **観点**: 技術的実現性・アーキテクチャ整合・影響範囲・非機能要件

### チェックリスト結果

| #   | 観点                                                                   | 判定 | コメント                                                                                                                                                                                            |
| --- | ---------------------------------------------------------------------- | ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 5   | 既存仕様（`openspec/specs/`）との矛盾がないか                          | ✅   | 既存仕様 `companion-file-rename` に対し delta spec が整合した形で MODIFIED 要件として定義。環境名解決メタ情報としての `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` 解決が明確に整理されている    |
| 6   | 影響範囲（影響を受けるコンポーネント・仕様・テスト）が整理されているか | ✅   | 影響ファイル（`Settings.fs`, `TagBasedVideoManager.Renamer.fsproj`, 設定ファイル群, `SettingsTests.fs`）が網羅され、Web側は Non-goals として明確にスコープ外と定義されている                        |
| 7   | 破壊的変更がある場合、移行計画が記載されているか                       | ✅   | 環境別設定ファイルが存在しない場合はベース `appsettings.json` のみで動作するため完全な後方互換性が担保。旧 `appsettings.Debug.json` / `appsettings.Release.json` からの移行・削除もタスクに組込済み |
| 8   | アーキテクチャ・データフロー・インターフェースの設計方針が妥当か       | ✅   | 組み込み既定値 → ベース `appsettings.json` → 環境別 `appsettings.{Environment}.json`（キー単位オーバーライド）→ `.env` 補完 というデータフローが明快で型安全                                        |
| 9   | タスク分解粒度が「2時間以内で完了する単位」になっているか              | ✅   | 各タスクが15〜45分程度で完了する粒度に適切に分解されている                                                                                                                                          |
| 10  | 依存関係のあるタスクの順序が明示されているか                           | ✅   | 1 (ファイル配置・ビルド設定) → 2 (テスト作成 Red) → 3 (実装・リファクタ Green) → 4 (全体検証) の順序で論理的に破綻がない                                                                            |
| 11  | テストファーストの手順（Red-Green-Refactor）が含まれているか           | ✅   | タスク 2.1, 2.2 で失敗するテスト（Red）を作成し、3.1 で実装 Green、3.2 でリファクタとする TDD 手順が厳格に定義されている                                                                            |
| 12  | 見積もり・スコープに過不足がないか                                     | ✅   | `TagBasedVideoManager.Renamer` の設定管理の環境別オーバーライドマージにフォーカスされており、過不足のない適切なスコープ                                                                             |
| 13  | パフォーマンス・セキュリティ・可用性等の非機能要件が考慮されているか   | ✅   | 起動時のみの処理でオーバーヘッドは極小。開発用パスやキーが本番成果物に混入するリスクを排除できセキュリティ向上。不在やパース異常時のフォールバックも考慮されている                                  |
| 14  | 運用・保守・監視の観点が含まれているか                                 | ✅   | .NET / ASP.NET Core の標準的な `Development` / `Production` 構成および `DOTNET_ENVIRONMENT` に準拠し保守性向上。.fsproj のワイルドカード指定（`appsettings*.json`）により将来の拡張性も確保         |

### 判定

- **LGTM**: true
- **次のアクション**: Propose合意完了、Apply フェーズへ進行可能

---

# レビュー記録: configuration-specific-appsettings / apply / 1.1

- **日時**: 2026-10-03
- **フェーズ**: apply
- **タスク**: 1.1 `src/TagBasedVideoManager.Renamer/appsettings.Development.json` および `appsettings.Production.json` を作成し、旧Debug/Release設定から移行・削除して JSON 構造を適合させる
- **レビュアー**: PG Agent
- **対象成果物**: `src/TagBasedVideoManager.Renamer/appsettings.Development.json`, `src/TagBasedVideoManager.Renamer/appsettings.Production.json`

## チェックリスト結果

| #   | 観点                                        | 判定 | コメント                                                                                                     |
| --- | ------------------------------------------- | ---- | ------------------------------------------------------------------------------------------------------------ |
| 1   | 設計書（`design.md`）と実装が整合しているか | ✅   | `appsettings.Development.json` と `appsettings.Production.json` が配置され旧ファイルから正しく移行・削除     |
| 2   | インターフェース・データ構造が設計通りか    | ✅   | `Domain.fs` の `RenamerSettings` および `NamingRule` の各フィールド定義と完全一致                            |
| 3   | 依存関係の方向性・責務分離が適切か          | ✅   | ベース設定と環境固有設定の役割が明確に分離されている                                                         |
| 4   | 命名規則・コード品質                        | ✅   | .NET 標準の環境名規約（`Development` / `Production`）に準拠したファイル名および camelCase プロパティ名で統一 |
| 5   | 境界値・異常系・エラー処理                  | ✅   | Production 設定で `apiKey` を null、`targetDirectory` を空文字に設定し初期境界値が適切に表現されている       |
| 6   | フォーマット・規約                          | ✅   | Prettier によるフォーマット済み                                                                              |

## 判定

- **LGTM**: true
- **次のアクション**: タスク 1.2 へ進む

---

# レビュー記録: configuration-specific-appsettings / apply / 1.2

- **日時**: 2026-10-03
- **フェーズ**: apply
- **タスク**: 1.2 `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` のビルド定義を修正し、`appsettings*.json` がビルド時にすべて出力ディレクトリへ配置されるようにする
- **レビュアー**: PG Agent
- **対象成果物**: `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`

## チェックリスト結果

| #   | 観点                                        | 判定 | コメント                                                                                                                                            |
| --- | ------------------------------------------- | ---- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | 設計書（`design.md`）と実装が整合しているか | ✅   | `design.md` の MSBuild 定義（`<Content Include="appsettings*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>`）と一致 |
| 2   | インターフェース・データ構造が設計通りか    | ✅   | MSBuild プロジェクト構成として標準的な glob パターンで記述されている                                                                                |
| 3   | 依存関係の方向性・責務分離が適切か          | ✅   | 出力アセットの配置管理責務がプロジェクトファイル内で適切に定義され、特定構成分岐が解消されている                                                    |
| 4   | 命名規則・コード品質                        | ✅   | MSBuild 標準タグおよび .NET 標準のファイルパターン（`appsettings*.json`）が用いられ極めて明瞭                                                       |
| 5   | 境界値・異常系・エラー処理                  | ✅   | Debug / Release いずれのビルドでも `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json` が配置されることを実機確認済み |
| 6   | フォーマット・規約                          | ✅   | XML のインデント・構文が既存ファイルと完全に整合                                                                                                    |

## 判定

- **LGTM**: true
- **次のアクション**: タスク 2.1 へ進む
