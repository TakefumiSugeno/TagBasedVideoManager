# Design: renamer-rule-manager-ux

## Context

- `src/TagBasedVideoManager.Renamer/` は Elmish MVU アーキテクチャおよび Avalonia.FuncUI を採用しています。
- 現在、ファイル走査完了時（`ScanCompleted`）において、画面表示用の `model.Candidates` は `FileScanner.sortCandidates model.SortCriterion` によりソート（既定: パス長降順）されている一方、非同期パイプライン（`createAsyncPipelineCmd`）にはディスク列挙順の未ソート `candidates` が渡されているため、処理進行と画面表示順が乖離しています。
- 命名規則マネージャーは `ruleManagerModal`（幅640px, 高さ560px固定）として実装され、ルールの編集機能がなく、プロンプト入力欄が48px固定で長文の視認・編集が著しく制限されています。また、ラベルが「対象パターン:」となっており、ファイル抽出フィルタと誤認されています。

## Goals / Non-Goals

**Goals:**

- 非同期パイプライン（`ddgs` 検索およびLLM提案）の処理順を、画面に表示されている候補のソート順と完全同期させる。
- 命名規則マネージャーモーダルを広域（幅960px, 高さ680px）の左右2ペインレイアウト（左: ルール一覧、右: ルール編集・詳細）に再構築する。
- 既存ルールの選択編集（「✏️ 編集」）、同一順序位置での上書き保存、および編集キャンセルの状態遷移を実装する。
- プロンプト指示文入力欄を複数行で広々と見渡せるテキストエリア（高さ280px以上確保）とする。
- 「対象パターン」を「命名パターン (テンプレート):」へと表記・ウォーターマークを適正化する。

**Non-Goals:**

- 命名パターンによるファイル拡張子・ワイルドカードの抽出走査制御（案Aに基づき、AIへのフォーマット指示として扱う）。
- OS別ウィンドウ（マルチウィンドウ）化（同一ウィンドウ内の広域モーダルで対応）。

## Decisions

### 1. パイプライン処理順と画面ソート順の完全同期

- **決定**:
  `ScanCompleted` ハンドラにおいて、`candidates` から `initialProposals` を生成してソートした後、そのソートされたパス順と同一順序で並んだ `candidates` リストを `createAsyncPipelineCmd` に渡す。
  ```fsharp
  let initialProposals =
      candidates
      |> List.map Proposal.createInitial
      |> FileScanner.sortCandidates model.SortCriterion

  // 画面のソート順（initialProposals）と完全一致する並び順の candidates を生成
  let sortedCandidates =
      let candidateMap = candidates |> List.map (fun c -> c.FullPath, c) |> Map.ofList
      initialProposals
      |> List.choose (fun p -> Map.tryFind p.OriginalFullPath candidateMap)

  // sortedCandidates を createAsyncPipelineCmd に渡す
  ```
- **代替案と理由**:
  - パイプライン内部で `Candidates` の状態を都度再検索する案: MVUの非同期コマンド境界を跨ぎ副作用が発生するため却下。起動時にソート済みリストを渡す方式が最も堅牢かつ純粋。

### 2. 命名規則マネージャーの状態管理（編集・新規・キャンセル）

- **決定**:
  - `model.EditingRule: NamingRule option` を編集状態のSingle Source of Truthとして維持。
  - 新規作成時は空テンプレート（`Id = ""` または 新規GUID）、既存編集時は既存ルールの複製をセット。
  - メッセージ定義:
    - `StartEditRule of string`: 指定されたルールIdを読み込み、編集モードに切り替え。
    - `CancelEditRule`: 編集モードを終了し、空の新規作成テンプレートにリセット。
    - `SaveEditingRule`: `EditingRule` が既存のルール一覧に存在するか判定し、存在する場合は同一位置（Order維持）で置換、存在しない場合は末尾に追加して `appsettings.json` に永続化。
- **代替案と理由**:
  - モーダルを「一覧画面」と「編集画面」で別画面に遷移させる案: 画面切り替えのコストが高く一覧を見ながらの編集ができないため、左右2ペインで同一画面上に配置する方式を採用。

### 3. 左右2ペインのモーダルUIレイアウト

- **決定**:
  - モーダル外枠: 幅 960px, 高さ 680px（Windows 11 Fluent Dark調）。
  - 左ペイン（幅 340px）:
    - ルール一覧を縦スクロールカード表示。
    - 各カードに [▲] [▼] [✏️ 編集] [🗑 削除] のアイコンボタンを配置。
    - 編集中のルールカードにはアクセントカラー枠線と「編集中」バッジを表示。
    - 下部に「➕ 新規ルールを作成」ボタンを常時配置。
  - 右ペイン（可変幅・広域エディタ）:
    - ヘッダーに現在の状態表示（「✏️ ルール編集中: 〇〇」または「➕ 新規ルールの作成」）。
    - ルール名（TextBox）、命名パターン（TextBox: ウォーターマーク `{Code}_{Summary}_{Actor}` ※拡張子不要）、Web検索トグル（CheckBox）。
    - プロンプト指示文（TextBox: `height 280.0`, `acceptsReturn true`, `textWrapping TextWrapping.Wrap`）。
    - フッターに「✕ 編集をキャンセル」（編集中のみ有効）と「💾 この内容で保存」ボタン。

## Risks / Trade-offs

- **[Risk] 解像度の低い画面環境（WXGA等）でのモーダルのはみ出し**:
  - → **Mitigation**: `Border.maxWidth 960.0`, `Border.maxHeight 680.0` としつつ、親ウィンドウ内での配置マージンを確保し、左ペイン・右ペインともにスクロール対応（`ScrollViewer`）とすることで視認性を担保する。
- **[Risk] ルール編集中に保存せずモーダルを閉じた場合の挙動**:
  - → **Mitigation**: モーダル閉じる操作（`CloseRuleManager`）時に `EditingRule` をクリアし、安全に未保存変更を破棄して待機状態に戻す。
