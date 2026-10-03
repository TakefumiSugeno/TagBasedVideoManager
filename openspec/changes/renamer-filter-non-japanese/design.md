# Design

## Context

TagBasedVideoManager.Renamer は F# (.NET 10) + Avalonia.FuncUI (Elmish MVU) で構築されたデスクトップGUIアプリケーションです。
動画ファイルの抽出処理は `FileScanner.scanLongPaths` が担い、現在は「パス長 ≧ 閾値（初期値240）」を満たす動画ファイルを再帰走査して抽出しています。
本変更では、この抽出パイプラインに「ファイル名本体（ベース名）に日本語を含まないもの」というフィルタリング機能を追加し、UIツールバーから任意にON/OFFできるようにします。
動機と背景の詳細は `proposal.md` を参照してください。

## Goals / Non-Goals

**Goals:**

- ファイル名本体（拡張子を除くベース名）に対する高速・正確な日本語文字（ひらがな、カタカナ、CJK統合漢字）の判定ロジックの実装。
- `FileScanner.scanLongPaths` へのフィルタ条件引数追加と、パス長閾値とのAND条件判定の実装。
- Elmishアーキテクチャ（`Model`, `Msg`, `update`）におけるフィルタ状態管理（初期値 `false`、セッション中メモリ保持）。
- Avalonia.FuncUI ツールバー（Row 1）へのダークテーマ準拠チェックボックスUIコントロールの配置。
- 既存の非同期逐次AI提案パイプライン、即時IO描画、キャンセル処理、リネーム実行、Undo機能との完全な後方互換性維持。

**Non-Goals:**

- ディレクトリパス（フォルダ名）に含まれる日本語の判定（親フォルダ名に日本語が含まれていても、ファイル名自体が英数等であれば抽出対象とします）。
- 設定ファイル（`appsettings.json`）への永続化（現在のパス長閾値の一時変更仕様と同様、セッション中のみ保持とし、起動時は安全なデフォルト値 `false` で開始）。
- 日本語以外の個別言語（韓国語ハングル、キリル文字等）の識別・分類。

## Decisions

### 1. 日本語判定ロジックの実装方針

- **判定関数**:
  `FileScanner` モジュール内にプライベートヘルパー `containsJapanese (name: string) : bool` を定義。
- **正規表現**:
  .NET のコンパイル済み正規表現を使用します:
  ```fsharp
  let private japaneseRegex =
      System.Text.RegularExpressions.Regex(
          @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]",
          System.Text.RegularExpressions.RegexOptions.Compiled
      )
  ```
- **判定対象**:
  `Path.GetFileNameWithoutExtension(filePath)`。拡張子（`.mp4` 等）や親ディレクトリパスは除外してファイル名本体のみを評価します。
- **代替案との比較**:
  - `文字コード範囲ループ (name |> Seq.exists ...)`: 単純なUnicode範囲比較だとCJK互換漢字やブロック境界の漏れが発生しやすい。
  - `正規表現 (\p{...})`: .NETのUnicode標準ブロック指定を活用でき、`RegexOptions.Compiled` により走査時のオーバーヘッドも極小であるため採用。

### 2. スキャン関数のインターフェース設計

- **シグネチャ変更**:
  ```fsharp
  val scanLongPaths:
      targetDirectory: string ->
      threshold: int ->
      filterNonJapaneseOnly: bool ->
      Result<ScanCandidate list, RenamerError>
  ```
- **判定ロジック**:
  ```fsharp
  let isTarget =
      isVideoFile file
      && file.Length >= threshold
      && (not filterNonJapaneseOnly || not (containsJapanese (Path.GetFileNameWithoutExtension(file))))
  ```
  `isVideoFile` およびパス長判定を先に評価し、短絡評価（ショートサーキット）により不要な正規表現判定を回避します。

### 3. Elmish 状態管理設計

- **Model**:
  ```fsharp
  FilterNonJapaneseOnly: bool // 初期値: false (セッション中の一時変更)
  ```
- **Msg**:
  ```fsharp
  | ToggleFilterNonJapaneseOnly of bool
  ```
- **update**:
  ```fsharp
  | ToggleFilterNonJapaneseOnly isChecked ->
      { model with FilterNonJapaneseOnly = isChecked }, Cmd.none
  ```
  `ExecuteScanAndPropose` 実行時、`FileScanner.scanLongPaths` の第3引数に `model.FilterNonJapaneseOnly` を渡します。

### 4. UIコントロールの配置とデザイン

- **配置場所**:
  `Views.controlPanel` の Row 1（抽出基準数値ボックスの右側）。
- **コントロール**:
  `CheckBox.create` を使用し、FluentTheme のダークモードに統一したスタイルを適用。
  - テキスト: `ファイル名に日本語を含まないもののみ抽出`
  - 文字色: `textSub` (薄グレー)、フォントサイズ 11px
- **HTMLモックアップ**:
  `openspec/changes/renamer-filter-non-japanese/mockup.html` を作成し、チェックボックスの配置とON/OFFによる抽出候補のシミュレーションをインタラクティブに確認できるようにします。

## Risks / Trade-offs

- **[Risk] 大規模フォルダ走査時のパフォーマンス影響**
  → **Mitigation**: 動画拡張子判定およびパス長閾値判定を先にパスしたファイルに対してのみ `containsJapanese` を実行するため、数万ファイル存在するディレクトリでも判定回数は最小限に抑えられます。
- **[Risk] 記号や半角英数字に全角スペース等が混在する場合の誤判定**
  → **Mitigation**: 判定対象を明確に「ひらがな・カタカナ・CJK統合漢字」に限定しているため、半角記号（`_`, `-`, `.` 等）や数字を含む英数ファイル名は確実に「日本語を含まない」と判定されます。
