# Proposal: 稼働アプリケーションバージョンの画面表示とセマンティックバージョニング規則の制定

## Metadata

- **適用規模レベル**: L2（通常変更: 新機能追加・API新設・UIバッジ追加・仕様策定）
- **変更種別**: feat
- **関連Issue**: [#35 [機能改善]稼働アプリケーションバージョンを画面ヘッダに表示する](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/35)

---

## Why

現在、TagBasedVideoManager（Webアプリ）および AI File Renamer（デスクトップアプリ）には、稼働中のアプリケーションバージョンを確認する画面表示および管理ルールが存在していません。
このため、Docker コンテナの更新やローカルでのバージョンアップ後に、ユーザーや開発者が「現在どのバージョンが稼働しているか」を一目で識別できず、不具合報告や問い合わせ時のトラブルシューティングが困難になっています。

本変更により、Webアプリの画面ヘッダーおよびデスクトップアプリのウィンドウタイトルバーに稼働バージョンを常時表示し、あわせて OpenSpec の変更規模（L1/L2/L3）と連動する明確なセマンティックバージョニング（SemVer）採番・付与ルールを制定します。

---

## What Changes

1. **セマンティックバージョニング（SemVer 2.0.0）付与ルールの制定**:
   - `X.Y.Z` 形式の採番ルールを定義。
     - **X（メジャー）**: 互換性のない破壊的変更（L3）、または**ユーザーからの明示的な指示**（Y, Z は 0 にリセット）。
     - **Y（マイナー）**: 後方互換性のある新機能追加・機能改善・仕様変更（L2）（Z は 0 にリセット）。
     - **Z（パッチ）**: 不具合修正（バグ修正）・typo修正・軽微な調整（L1）。
   - Webアプリとデスクトップアプリで独立してバージョンを管理。
   - 初期バージョンは両アプリとも **`0.1.0`**（画面表記は `v0.1.0`）とする。

2. **バックエンド API エンドポイントの新設 (`GET /api/version`)**:
   - `src/TagBasedVideoManager/TagBasedVideoManager.fsproj` の `<Version>0.1.0</Version>` を正本（Single Source of Truth）とし、起動中のアセンブリ情報からバージョンを取得して JSON（`{"version": "0.1.0"}`）で返却。

3. **Web フロントエンド画面ヘッダーへのバージョンバッジ追加**:
   - `index.html` のヘッダー内アプリタイトル（`Tag-Based Video Manager`）の右横に、静的ピルバッジ（`v0.1.0`）を配置。
   - モバイル画面においてタイトルが折り返された場合でも、末尾に自然に寄り添うレイアウト（`inline-flex` / `flex-wrap`）とする。
   - ブラウザタブのタイトル（`<title>`）にも `Tag-Based Video Manager (v0.1.0)` として反映。

4. **デスクトップアプリ (AI File Renamer) へのバージョン表示追加**:
   - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` に `<Version>0.1.0</Version>` を設定。
   - OSウィンドウタイトルバー（`base.Title`）に `TagBasedVideoManager - AI File Renamer v0.1.0` と表示。画面内UI領域を圧迫しない。

5. **OpenSpec 仕様書と運用ルールの更新**:
   - 新規仕様書 `openspec/specs/application-versioning/spec.md` を作成。
   - `AGENTS.md` および `openspec/config.yaml` に SemVer 採番・インクリメント運用ルールを追記。

---

## Capabilities

### New Capabilities

- `application-versioning`: Webアプリおよびデスクトップアプリのバージョン採番・管理規則（SemVer）、バージョン取得API（`GET /api/version`）、および画面表示（Webヘッダーバッジ・デスクトップタイトルバー）の仕様を定義。

### Modified Capabilities

<!-- 既存ケイパビリティの要件変更なし -->

---

## Non-goals（やらないこと）

- バージョンバッジのクリックによる詳細ポップアップや更新チェック機能（将来の拡張とし、今回は静的表示のみとする）。
- Git コミットハッシュやビルド日時の画面常時表示（SemVer 番号のみのクリーンな表示に限定）。
- Webアプリとデスクトップアプリのバージョン連動・強制同期（それぞれ独立して進化させる）。

---

## Acceptance Criteria（受け入れ基準）

### Scenario 1: WebアプリのバージョンAPIと画面ヘッダー表示

- **GIVEN** Webバックエンドが起動している
- **WHEN** クライアントが `GET /api/version` にリクエストを発行した時
- **THEN** HTTP 200 と共に `{"version": "0.1.0"}` が返却されること
- **WHEN** ユーザーがブラウザで Web 画面を開いた時
- **THEN** 画面ヘッダーのアプリタイトル横に `v0.1.0` のバッジが表示され、ブラウザタブの `<title>` にもバージョンが含まれること
- **WHEN** モバイル幅（< 768px）で閲覧した時
- **THEN** タイトルが改行されてもバッジがレイアウト崩れを起こさず視認できること

### Scenario 2: デスクトップアプリのウィンドウタイトル表示

- **GIVEN** AI File Renamer（デスクトップアプリ）を起動した時
- **WHEN** メインウィンドウが表示された時
- **THEN** ウィンドウのタイトルバーに `TagBasedVideoManager - AI File Renamer v0.1.0` と表示されること

### Scenario 3: SemVer 規則の遵守

- **GIVEN** OpenSpec による変更管理において
- **WHEN** L2（通常変更）が適用された時、対象アプリの `Y`（マイナー）がインクリメントされ `Z` が 0 にリセットされること
- **WHEN** ユーザーから明示的なメジャーアップ指示があった時、または L3 変更時、`X`（メジャー）がインクリメントされ `Y`, `Z` が 0 にリセットされること

---

## Impact

- **バックエンド**: `src/TagBasedVideoManager/TagBasedVideoManager.fsproj`（`<Version>` 設定）、`HttpHandlers.fs`（`GET /api/version` 追加）。
- **フロントエンド**: `src/TagBasedVideoManager/wwwroot/index.html`（ヘッダーバッジ追加、`<title>` 更新）、`app.js`（`fetch('/api/version')` 呼び出しとバインド）。
- **デスクトップアプリ**: `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`（`<Version>` 設定）、`Program.fs`（`base.Title` にアセンブリバージョン反映）。
- **OpenSpec**: `openspec/specs/application-versioning/spec.md` 新設、`openspec/config.yaml` / `AGENTS.md` 運用ルール追記。
- **破壊的変更 (Breaking Change)**: なし。
