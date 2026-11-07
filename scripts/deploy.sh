#!/usr/bin/env bash
# =============================================================================
# scripts/deploy.sh: Linux 向け自動デプロイスクリプト
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# 安全な .env パーサ関数
parse_env() {
    local env_file="$1"
    [ -f "${env_file}" ] || return 0
    while IFS= read -r line || [ -n "$line" ]; do
        line="$(echo "${line}" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
        [ -z "${line}" ] && continue
        case "${line}" in
            \#*) continue ;;
        esac
        if [[ "${line}" =~ ^([a-zA-Z_][a-zA-Z0-9_]*)=(.*)$ ]]; then
            local key="${BASH_REMATCH[1]}"
            local val="${BASH_REMATCH[2]}"
            if [[ "${val}" =~ ^\"(.*)\"$ ]]; then
                val="${BASH_REMATCH[1]}"
            elif [[ "${val}" =~ ^\'(.*)\'$ ]]; then
                val="${BASH_REMATCH[1]}"
            fi
            if [ -z "${!key+x}" ]; then
                export "${key}=${val}"
            fi
        fi
    done < "${env_file}"
}

# .env ファイルの安全な読み込み
parse_env "${REPO_ROOT}/.env"

PORT="${PORT:-5620}"
DATA_DIR="${DATA_DIR:-${REPO_ROOT}/data}"
CONTAINER_NAME="tag-based-video-manager"
IMAGE_NAME="tag-based-video-manager:latest"
OLD_VIDEO_DIR="${VIDEO_DIR:-}"
VIDEO_DIRS="${VIDEO_DIRS:-}"

# 早期入力検証（ビルドやコンテナ停止の前に実行）
if [ -z "${VIDEO_DIRS}" ]; then
    if [ -n "${OLD_VIDEO_DIR}" ]; then
        echo "エラー: 旧環境変数 VIDEO_DIR ('${OLD_VIDEO_DIR}') が指定されています。VIDEO_DIRS へ移行してください。設定例: VIDEO_DIRS=\"/path/to/dir1;/path/to/dir2\"" >&2
        exit 1
    fi
    echo "エラー: 環境変数 VIDEO_DIRS が設定されていません。設定例: VIDEO_DIRS=\"/path/to/dir1;/path/to/dir2\"" >&2
    exit 1
fi

# セミコロンで分割して各ディレクトリを検証
IFS=';' read -ra DIR_ARRAY <<< "${VIDEO_DIRS}"
MOUNT_ARGS=()
CONTAINER_VIDEO_DIRS_LIST=()
SEEN_LEAVES=()

for raw_dir in "${DIR_ARRAY[@]}"; do
    # トリム
    dir="$(echo "${raw_dir}" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
    [ -z "${dir}" ] && continue

    # ドライブ直下 / ルート判定
    if [ "${dir}" = "/" ] || [[ "${dir}" =~ ^/+$ ]]; then
        echo "エラー: ルートディレクトリ '${dir}' はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください。" >&2
        exit 1
    fi

    dir_trimmed="$(echo "${dir}" | sed -e 's:/*$::')"
    if [ -z "${dir_trimmed}" ]; then
        echo "エラー: ルートディレクトリ '${dir}' はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください。" >&2
        exit 1
    fi

    # 存在チェック
    if [ ! -d "${dir}" ]; then
        echo "エラー: 指定された動画ディレクトリが存在しません: ${dir}" >&2
        exit 1
    fi

    # フォルダ名 (leaf) の取得
    leaf="$(basename "${dir_trimmed}")"
    if [ "${leaf}" = "." ] || [ "${leaf}" = ".." ]; then
        echo "エラー: フォルダ名（マウント先）を決定できないか、パストラバーサル（. や ..）が含まれています: ${dir}" >&2
        exit 1
    fi
    leaf_lower="$(echo "${leaf}" | tr '[:upper:]' '[:lower:]')"

    # 重複チェック (大文字小文字を区別せず)
    if [ "${#SEEN_LEAVES[@]}" -gt 0 ]; then
        for seen in "${SEEN_LEAVES[@]}"; do
            if [ "${seen}" = "${leaf_lower}" ]; then
                echo "エラー: マウント先のフォルダ名 '${leaf}' が重複しています (${dir})。異なるフォルダ名を指定してください。" >&2
                exit 1
            fi
        done
    fi
    SEEN_LEAVES+=("${leaf_lower}")

    container_path="/app/videos/${leaf}"
    MOUNT_ARGS+=(-v "${dir}:${container_path}:ro")
    CONTAINER_VIDEO_DIRS_LIST+=("${container_path}")
done

if [ "${#CONTAINER_VIDEO_DIRS_LIST[@]}" -eq 0 ]; then
    echo "エラー: 有効な動画ディレクトリが指定されていません。設定例: VIDEO_DIRS=\"/path/to/dir1;/path/to/dir2\"" >&2
    exit 1
fi

CONTAINER_VIDEO_DIRS_STR="$(IFS=';'; echo "${CONTAINER_VIDEO_DIRS_LIST[*]}")"

mkdir -p "${DATA_DIR}"

echo "=== TagBasedVideoManager デプロイ開始 ==="
echo "ポート: ${PORT}"
echo "動画ディレクトリ: ${VIDEO_DIRS}"
echo "データディレクトリ: ${DATA_DIR}"

# 1. コンテナランタイム自動判別 (docker -> podman)
RUNTIME=""
if command -v docker >/dev/null 2>&1; then
    RUNTIME="docker"
elif command -v podman >/dev/null 2>&1; then
    RUNTIME="podman"
else
    echo "エラー: docker または podman が見つかりません。" >&2
    exit 1
fi
echo "検出されたランタイム: ${RUNTIME}"

# 2. ポート事前チェック
if command -v lsof >/dev/null 2>&1; then
    if lsof -i ":${PORT}" >/dev/null 2>&1; then
        echo "警告: ポート ${PORT} は既に使用されています。" >&2
    fi
elif command -v nc >/dev/null 2>&1; then
    if nc -z localhost "${PORT}" >/dev/null 2>&1; then
        echo "警告: ポート ${PORT} は既に使用されています。" >&2
    fi
fi

# 3. イメージビルド
echo "--- コンテナイメージをビルド中 ---"
${RUNTIME} build -t "${IMAGE_NAME}" -f "${REPO_ROOT}/Dockerfile" "${REPO_ROOT}"

# 4. 既存コンテナの停止 & 削除
echo "--- 既存コンテナの停止・削除 ---"
${RUNTIME} stop "${CONTAINER_NAME}" 2>/dev/null || true
${RUNTIME} rm -f "${CONTAINER_NAME}" 2>/dev/null || true

# 5. GPU 判定
GPU_ARGS=()
if command -v nvidia-smi >/dev/null 2>&1; then
    GPU_ARGS=(--gpus all)
elif [ -e /dev/dri ]; then
    GPU_ARGS=(--device /dev/dri:/dev/dri)
fi

# 6. 新コンテナ起動
echo "--- 新コンテナを起動中 ---"
${RUNTIME} run -d \
    --name "${CONTAINER_NAME}" \
    -p "${PORT}:5620" \
    "${MOUNT_ARGS[@]}" \
    -v "${DATA_DIR}:/app/data" \
    -e "PORT=5620" \
    -e "VIDEO_DIRS=${CONTAINER_VIDEO_DIRS_STR}" \
    -e "THUMBNAIL_DIR=/app/data/thumbnails" \
    -e "DATABASE_PATH=/app/data/metadata.db" \
    "${GPU_ARGS[@]+"${GPU_ARGS[@]}"}" \
    --restart unless-stopped \
    "${IMAGE_NAME}"

# 7. ヘルスチェック (最大30秒)
echo "--- ヘルスチェック実行中 (ポート ${PORT}) ---"
HEALTH_URL="http://localhost:${PORT}/"
MAX_RETRIES=10
RETRY_COUNT=0
IS_HEALTHY=false

while [ "${RETRY_COUNT}" -lt "${MAX_RETRIES}" ]; do
    sleep 3
    RETRY_COUNT=$((RETRY_COUNT + 1))
    if command -v curl >/dev/null 2>&1; then
        if curl -s -f -o /dev/null "${HEALTH_URL}"; then
            IS_HEALTHY=true
            break
        fi
    elif command -v wget >/dev/null 2>&1; then
        if wget -q -O /dev/null "${HEALTH_URL}"; then
            IS_HEALTHY=true
            break
        fi
    fi
    echo "待機中... (${RETRY_COUNT}/${MAX_RETRIES})"
done

if [ "${IS_HEALTHY}" = true ]; then
    echo "=== デプロイ成功！TagBasedVideoManager は正常に稼働しています (${HEALTH_URL}) ==="
    exit 0
else
    echo "エラー: ヘルスチェックに失敗しました。タイムアウトしました。" >&2
    echo "--- 直近のコンテナログ ---" >&2
    ${RUNTIME} logs --tail 50 "${CONTAINER_NAME}" || true
    exit 1
fi
