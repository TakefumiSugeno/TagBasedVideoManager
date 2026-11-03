# Spec Delta: web-branding-and-pwa

## Purpose

Webブラウザおよびモバイル端末におけるアプリケーション識別性を高めるため、ブランドシンボルに基づくファビコン・アプリアイコン群の配信と、Web App Manifestによるホーム画面追加・全画面スタンドアロン起動機能を提供する。

## ADDED Requirements

### Requirement: Webアプリアイコンおよびファビコンの配信

システムは、Webフロントエンドのブランディング用アセットとして、SVGおよびマルチサイズICO形式のファビコン、ならびに高解像度PNG形式のアプリアイコン（180x180, 192x192, 512x512）を静的ファイルとして配信しなければならない（SHALL）。

#### Scenario: ブラウザからのファビコンリクエスト

- **WHEN** クライアントが `/favicon.ico` または `/favicon.svg` にアクセスした時
- **THEN** HTTP 200 OK と適切な画像コンテンツ（image/x-icon または image/svg+xml）が返却されること。

#### Scenario: iOS端末からのホーム画面追加アイコン参照

- **WHEN** iOS Safari 等のクライアントが `/apple-touch-icon.png` を参照した時
- **THEN** HTTP 200 OK と 180x180 ピクセル以上の PNG 画像が返却されること。

### Requirement: Web App Manifest によるスタンドアロン起動の提供

システムは、Web アプリケーションのメタデータおよび表示設定を定義した `manifest.webmanifest`（または `manifest.json`）を提供し、モバイル端末のホーム画面からアドレスバーのないスタンドアロン全画面モードで起動可能にしなければならない（SHALL）。

#### Scenario: マニフェストファイルの取得

- **WHEN** クライアントがマニフェストURLへリクエストした時
- **THEN** HTTP 200 OK と共に `name`, `short_name`, `icons`, `display: "standalone"`, `theme_color` を含む有効な JSON マニフェストが返却されること。
