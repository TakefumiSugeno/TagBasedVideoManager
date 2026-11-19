#!/usr/bin/env bash
# =============================================================================
# scripts/deploy.sh: Linux 向け自動デプロイスクリプト
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# appsettings*.json 設定解決
ENVIRONMENT="${ENVIRONMENT:-${ASPNETCORE_ENVIRONMENT:-Production}}"
SRC_DIR="${REPO_ROOT}/src/TagBasedVideoManager"
BASE_JSON="${SRC_DIR}/appsettings.json"
ENV_JSON="${SRC_DIR}/appsettings.${ENVIRONMENT}.json"

PARSER=""
if command -v python3 >/dev/null 2>&1; then
    PARSER="python3"
elif command -v jq >/dev/null 2>&1; then
    PARSER="jq"
fi

# 空白文字のみをトリムする共通関数
trim_whitespace() {
    local s="$1"
    s="${s#"${s%%[![:space:]]*}"}"
    s="${s%"${s##*[![:space:]]}"}"
    printf '%s' "${s}"
}

# appsettings*.json から設定値と設定元ファイル名を解決する（グローバル変数 APPSETTING_VAL / APPSETTING_SRC に設定）
APPSETTING_VAL=""
APPSETTING_SRC="既定値"
read_appsettings_with_source() {
    local key="$1"
    local default_val="$2"
    local found_val=""
    local json_file=""
    local file_label=""

    APPSETTING_VAL="${default_val}"
    APPSETTING_SRC="既定値"

    for json_file in "${ENV_JSON}" "${BASE_JSON}"; do
        [ -f "${json_file}" ] || continue
        file_label="$(basename "${json_file}")"

        if [ "${PARSER}" = "python3" ]; then
            found_val="$(python3 -c "
import sys, json
try:
    with open(sys.argv[1], 'r', encoding='utf-8-sig') as f:
        data = json.load(f)
    if not isinstance(data, dict):
        raise ValueError('トップレベルが JSON オブジェクトではありません')
    k = sys.argv[2]
    if k in data and data[k] is not None and str(data[k]).strip() != '':
        print(data[k])
except Exception as e:
    sys.stderr.write(f'JSON構文エラー: {sys.argv[1]}: {e}\n')
    sys.exit(2)
" "${json_file}" "${key}")" || {
                local exit_code=$?
                exit "${exit_code}"
            }
            if [ -n "${found_val}" ]; then
                APPSETTING_VAL="${found_val}"
                APPSETTING_SRC="${file_label}"
                return 0
            fi
        elif [ "${PARSER}" = "jq" ]; then
            if [ -z "$(trim_whitespace "$(cat "${json_file}")")" ]; then
                echo "JSON構文エラー: ${json_file}: ファイルが空です。" >&2
                exit 2
            fi
            if ! jq -e 'type == "object"' "${json_file}" >/dev/null 2>&1; then
                echo "JSON構文エラー: ${json_file}: 有効な JSON オブジェクトではありません。" >&2
                exit 2
            fi
            found_val="$(jq -r --arg k "${key}" '.[$k] // empty' "${json_file}" 2>/dev/null)" || {
                local exit_code=$?
                echo "JSON構文エラー: ${json_file}" >&2
                exit "${exit_code}"
            }
            if [ -n "$(trim_whitespace "${found_val}")" ]; then
                APPSETTING_VAL="${found_val}"
                APPSETTING_SRC="${file_label}"
                return 0
            fi
        else
            echo "エラー: appsettings の読み込みには python3 または jq が必要です。python3 または jq をインストールしてください。" >&2
            exit 1
        fi
    done
}

# セグメント境界の '..' パストラバーサル有無を判定する（'v1..v2' は許可）
has_traversal_segment() {
    local target="$1"
    [[ "${target}" =~ (^|[\\/])\.\.($|[\\/]) ]]
}

# Windows ドライブパス (例: D:\data, D:data) か判定する
is_windows_drive_path() {
    local target="$1"
    [[ "${target}" =~ ^[a-zA-Z]: ]]
}

# GNU realpath -m 非依存の Linux パス正規化（python3 優先＋純粋 Bash フォールバック）
normalize_linux_path() {
    local raw="$1"
    # 先頭の複数スラッシュを単一化（os.path.normpath が POSIX の // を残す仕様を回避）
    local collapsed
    collapsed="$(printf '%s' "${raw}" | sed -E 's:^/+:/:')"

    if command -v python3 >/dev/null 2>&1; then
        python3 -c 'import os,sys; print(os.path.normpath(sys.argv[1]))' "${collapsed}"
        return 0
    fi

    # 純粋 Bash による正規化（. と空セグメントを除去して / で再結合）
    local -a segments=()
    local -a clean_segments=()
    local seg=""
    IFS='/' read -ra segments <<< "${collapsed}"
    for seg in "${segments[@]}"; do
        if [ -z "${seg}" ] || [ "${seg}" = "." ]; then
            continue
        fi
        clean_segments+=("${seg}")
    done

    if [ "${#clean_segments[@]}" -eq 0 ]; then
        printf '/'
        return 0
    fi

    local joined=""
    for seg in "${clean_segments[@]}"; do
        joined="${joined}/${seg}"
    done
    printf '%s' "${joined}"
}

# WSL UNC パス (\\wsl.localhost\<Distro>\<subpath> / \\wsl$\...) を Linux パスへ解決する
# 戻り値はグローバル変数 RESOLVED_UNC_LINUX / RESOLVED_UNC_DISTRO に格納する
RESOLVED_UNC_LINUX=""
RESOLVED_UNC_DISTRO=""
resolve_unc_path_for_linux() {
    local raw="$1"
    local label="$2"
    RESOLVED_UNC_LINUX=""
    RESOLVED_UNC_DISTRO=""

    if [[ "${raw}" =~ ^(\\\\|//)([wW][sS][lL]\.[lL][oO][cC][aA][lL][hH][oO][sS][tT]|[wW][sS][lL]\$)([\\/].*)?$ ]]; then
        local rest="${BASH_REMATCH[3]:-}"
        rest="${rest//\\//}"
        rest="${rest#/}"
        rest="${rest%/}"

        if [ -z "${rest}" ] || [[ "${rest}" != */* ]]; then
            echo "エラー: ${label} の WSL UNC パスにディストロ名またはサブパスが不足しています: ${raw}。Linux パス（例: /home/<user>/tbvm/data または ./data）を指定してください。" >&2
            exit 1
        fi

        local distro="${rest%%/*}"
        local subpath="${rest#*/}"
        subpath="${subpath#/}"
        subpath="${subpath%/}"

        if [ -z "${distro}" ] || [ -z "${subpath}" ]; then
            echo "エラー: ${label} の WSL UNC パスが不正です: ${raw}。Linux パス（例: /home/<user>/tbvm/data または ./data）を指定してください。" >&2
            exit 1
        fi

        if has_traversal_segment "${subpath}"; then
            echo "エラー: ${label} にパストラバーサル（..）が含まれています: ${raw}" >&2
            exit 1
        fi

        local current_distro="${WSL_DISTRO_NAME:-}"
        if [ -z "${current_distro}" ]; then
            echo "エラー: 非 WSL 環境（WSL_DISTRO_NAME 未設定）では WSL UNC パス '${raw}' を解決できません。Linux パス（例: /home/<user>/tbvm/data または ./data）を指定してください。" >&2
            exit 1
        fi

        local distro_lower="${distro,,}"
        local current_lower="${current_distro,,}"
        if [ "${distro_lower}" != "${current_lower}" ]; then
            echo "エラー: WSL UNC パス '${raw}' のディストロ '${distro}' は現在の WSL ディストロ '${current_distro}' と一致しません。正しい Linux パス（例: /home/<user>/tbvm/data または ./data）を指定してください。" >&2
            exit 1
        fi

        RESOLVED_UNC_DISTRO="${distro}"
        RESOLVED_UNC_LINUX="$(normalize_linux_path "/${subpath}")"
        return 0
    fi

    if [[ "${raw}" =~ ^(\\\\|//) ]]; then
        echo "エラー: ${label} に非 WSL のネットワーク UNC パスは指定できません: ${raw}。Linux パス（例: /home/<user>/tbvm/data または ./data）を指定してください。" >&2
        exit 1
    fi

    return 1
}

# DATA_DIR および VIDEO_DIRS 共通のホストディレクトリ検証・解決関数
# 戻り値はグローバル変数 RESOLVED_DIR_PATH / RESOLVED_DIR_DISTRO に格納する
RESOLVED_DIR_PATH=""
RESOLVED_DIR_DISTRO=""
resolve_host_dir_for_linux() {
    local target="$1"
    local label="$2"
    RESOLVED_DIR_PATH=""
    RESOLVED_DIR_DISTRO=""

    if is_windows_drive_path "${target}"; then
        echo "エラー: Linux 環境では ${label} に Windows ドライブパス '${target}' を使用できません。Linux パス（例: /home/<user>/tbvm/data または ./data）を指定してください。" >&2
        exit 1
    fi

    if has_traversal_segment "${target}"; then
        echo "エラー: ${label} にパストラバーサル（..）が含まれています（無効なフォルダ名です）: ${target}。例: '/home/<user>/tbvm/data' または './data'" >&2
        exit 1
    fi

    if [ "${target}" = "/" ] || [[ "${target}" =~ ^/+$ ]]; then
        echo "エラー: ${label} にルートディレクトリ '${target}' はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください（例: '/home/<user>/tbvm/data' または './data'）。" >&2
        exit 1
    fi

    # 先頭3本以上のスラッシュ（例: ///home/user/data）は単一スラッシュ始まりへ正規化し、非 WSL UNC との誤認を防ぐ
    if [[ "${target}" =~ ^/{3,}[^/] ]]; then
        target="$(printf '%s' "${target}" | sed -E 's:^/+:/:')"
    fi

    # 入力末尾が '.' または '..' の場合は明示拒否
    local raw_trimmed="${target%/}"
    local raw_leaf="${raw_trimmed##*/}"
    if [ "${raw_leaf}" = "." ] || [ "${raw_leaf}" = ".." ]; then
        echo "エラー: フォルダ名（マウント先）を決定できないか、パストラバーサル（. や ..）が含まれています: ${target}" >&2
        exit 1
    fi

    local resolved=""
    if [[ "${target}" =~ ^(\\\\|//) ]]; then
        resolve_unc_path_for_linux "${target}" "${label}"
        RESOLVED_DIR_DISTRO="${RESOLVED_UNC_DISTRO}"
        resolved="${RESOLVED_UNC_LINUX}"
    elif [[ "${target}" != /* ]]; then
        # 相対パスは実行時 CWD ではなく REPO_ROOT 基準で解決・正規化
        resolved="$(normalize_linux_path "${REPO_ROOT}/${target}")"
    else
        resolved="$(normalize_linux_path "${target}")"
    fi

    if [ -z "${resolved}" ] || [ "${resolved}" = "/" ]; then
        echo "エラー: ${label} にルートディレクトリ '/' 単体はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください（例: '/home/<user>/tbvm/data' または './data'）。" >&2
        exit 1
    fi

    if [ -e "${resolved}" ] && [ ! -d "${resolved}" ]; then
        echo "エラー: ${label} にディレクトリではなく通常ファイルが指定されています: ${resolved}。ディレクトリパスを指定してください（例: './data'）。" >&2
        exit 1
    fi

    RESOLVED_DIR_PATH="${resolved}"
}

# 既存コンテナの /app/data バインドマウント元（Source）を取得する
get_existing_container_data_mount() {
    local runtime="$1"
    local container_name="$2"
    local inspect_out=""
    local inspect_rc=0

    if command -v timeout >/dev/null 2>&1; then
        inspect_out="$(timeout 5 "${runtime}" inspect "${container_name}" 2>/dev/null)" || inspect_rc=$?
        if [ "${inspect_rc}" -eq 124 ]; then
            echo "警告: 既存コンテナ '${container_name}' の '${runtime} inspect' が 5 秒でタイムアウトしました。マウント元変更チェックをスキップします。" >&2
            return 0
        fi
    else
        inspect_out="$("${runtime}" inspect "${container_name}" 2>/dev/null)" || inspect_rc=$?
    fi

    if [ "${inspect_rc}" -ne 0 ] || [ -z "$(trim_whitespace "${inspect_out}")" ]; then
        return 0
    fi

    if command -v python3 >/dev/null 2>&1; then
        python3 -c '
import sys, json
raw = sys.stdin.read()
try:
    data = json.loads(raw)
except Exception as e:
    sys.stderr.write(f"警告: 既存コンテナ inspect JSON の解析に失敗しました: {e}\n")
    sys.exit(0)
item = data[0] if isinstance(data, list) and len(data) > 0 else (data if isinstance(data, dict) else None)
if not isinstance(item, dict) or not isinstance(item.get("Mounts"), list):
    sys.exit(0)
for m in item["Mounts"]:
    if isinstance(m, dict) and m.get("Destination") == "/app/data":
        src = m.get("Source")
        if src:
            print(src)
        break
' <<< "${inspect_out}"
    elif command -v jq >/dev/null 2>&1; then
        local src=""
        src="$(jq -r 'first((if type=="array" then .[0] else . end).Mounts[]? | select(.Destination == "/app/data") | .Source) // empty' <<< "${inspect_out}" 2>/dev/null)" || {
            echo "警告: 既存コンテナ inspect JSON の解析に失敗しました。" >&2
            return 0
        }
        printf '%s' "${src}"
    fi
}

# マウント元比較用にスラッシュ・末尾スラッシュを正規化する（WSL UNC のディストロ名のみ小文字化し Linux パス部は大小文字を維持）
normalize_mount_source_for_compare() {
    local s="$1"
    s="$(trim_whitespace "${s}")"
    s="${s//\\//}"
    if [[ "${s}" == //* ]] && [[ "${s}" != ///* ]]; then
        s="//$(printf '%s' "${s#//}" | sed -E 's:/+:/:g')"
        if [[ "${s}" =~ ^//([wW][sS][lL]\.[lL][oO][cC][aA][lL][hH][oO][sS][tT]|[wW][sS][lL]\$)/([^/]+)(/.*)?$ ]]; then
            local distro_lower="${BASH_REMATCH[2],,}"
            local sub="${BASH_REMATCH[3]:-}"
            s="//wsl.localhost/${distro_lower}${sub}"
        fi
    else
        s="$(printf '%s' "${s}" | sed -E 's:/+:/:g')"
    fi
    if [ "${#s}" -gt 1 ]; then
        s="${s%/}"
    fi
    printf '%s' "${s}"
}

# 優先順位: 環境変数 -> appsettings.{env}.json -> appsettings.json -> 既定値
if [ -z "$(trim_whitespace "${PORT:-}")" ]; then
    read_appsettings_with_source "PORT" "5620"
    PORT="${APPSETTING_VAL}"
fi

RAW_DATA_DIR=""
DATA_DIR_SOURCE="既定値"
if [ -n "$(trim_whitespace "${DATA_DIR:-}")" ]; then
    RAW_DATA_DIR="${DATA_DIR}"
    DATA_DIR_SOURCE="環境変数"
else
    read_appsettings_with_source "DATA_DIR" "./data"
    RAW_DATA_DIR="${APPSETTING_VAL}"
    DATA_DIR_SOURCE="${APPSETTING_SRC}"
fi

if [ -z "$(trim_whitespace "${VIDEO_DIRS:-}")" ]; then
    read_appsettings_with_source "VIDEO_DIRS" ""
    VIDEO_DIRS="${APPSETTING_VAL}"
fi

CONTAINER_NAME="tag-based-video-manager"
IMAGE_NAME="tag-based-video-manager:latest"
OLD_VIDEO_DIR="${VIDEO_DIR:-}"

# 早期入力検証（ビルドやコンテナ停止の前に実行）
if [ -z "$(trim_whitespace "${VIDEO_DIRS}")" ]; then
    if [ -n "${OLD_VIDEO_DIR}" ]; then
        echo "エラー: 旧環境変数 VIDEO_DIR ('${OLD_VIDEO_DIR}') が指定されています。VIDEO_DIRS へ移行してください。設定例: VIDEO_DIRS=\"/path/to/dir1;/path/to/dir2\"" >&2
        exit 1
    fi
    echo "エラー: 環境変数 VIDEO_DIRS が設定されていません。設定例: VIDEO_DIRS=\"/path/to/dir1;/path/to/dir2\"" >&2
    exit 1
fi

# セミコロンで分割して各動画ディレクトリを検証・解決
IFS=';' read -ra DIR_ARRAY <<< "${VIDEO_DIRS}"
MOUNT_ARGS=()
CONTAINER_VIDEO_DIRS_LIST=()
SEEN_LEAVES=()

for raw_dir in "${DIR_ARRAY[@]}"; do
    dir="$(trim_whitespace "${raw_dir}")"
    [ -z "${dir}" ] && continue

    resolve_host_dir_for_linux "${dir}" "VIDEO_DIRS"
    dir="${RESOLVED_DIR_PATH}"

    if [ ! -d "${dir}" ]; then
        echo "エラー: 指定された動画ディレクトリが存在しません: ${dir}" >&2
        exit 1
    fi

    # フォルダ名 (leaf) の取得
    leaf="$(basename "${dir}")"
    if [ "${leaf}" = "." ] || [ "${leaf}" = ".." ]; then
        echo "エラー: フォルダ名（マウント先）を決定できないか、パストラバーサル（. や ..）が含まれています: ${dir}" >&2
        exit 1
    fi
    leaf_lower="$(printf '%s' "${leaf}" | tr '[:upper:]' '[:lower:]')"

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

# DATA_DIR の早期検証・正規化・事前作成（ビルド・停止より前）
DATA_DIR_TRIMMED="$(trim_whitespace "${RAW_DATA_DIR}")"
if [ -z "${DATA_DIR_TRIMMED}" ]; then
    DATA_DIR_TRIMMED="./data"
    DATA_DIR_SOURCE="既定値"
fi

resolve_host_dir_for_linux "${DATA_DIR_TRIMMED}" "DATA_DIR"
DATA_DIR_WSL_DISTRO="${RESOLVED_DIR_DISTRO}"
DATA_DIR_RESOLVED="${RESOLVED_DIR_PATH}"

if ! mkdir -p "${DATA_DIR_RESOLVED}"; then
    echo "エラー: DATA_DIR ディレクトリを作成できませんでした: ${DATA_DIR_RESOLVED}" >&2
    exit 1
fi
DATA_DIR="${DATA_DIR_RESOLVED}"

echo "=== TagBasedVideoManager デプロイ開始 ==="
echo "ポート: ${PORT}"
echo "動画ディレクトリ: ${VIDEO_DIRS}"
echo "DATA_DIR 設定元: ${DATA_DIR_SOURCE} (生値: '${RAW_DATA_DIR}')"
if [ -n "${DATA_DIR_WSL_DISTRO}" ]; then
    echo "DATA_DIR WSLディストロ: ${DATA_DIR_WSL_DISTRO}"
fi
echo "DATA_DIR HostPath: ${DATA_DIR}"
echo "DATA_DIR MountSource: ${DATA_DIR}"

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

# 2. 既存コンテナの /app/data マウント元変更チェック（ビルド・停止・削除より前に実行）
EXISTING_DATA_MOUNT="$(get_existing_container_data_mount "${RUNTIME}" "${CONTAINER_NAME}")"
if [ -n "${EXISTING_DATA_MOUNT}" ]; then
    NORM_EXISTING="$(normalize_mount_source_for_compare "${EXISTING_DATA_MOUNT}")"
    NORM_NEW="$(normalize_mount_source_for_compare "${DATA_DIR}")"
    if [ "${NORM_EXISTING}" != "${NORM_NEW}" ]; then
        cat >&2 << EOF
警告: 既存コンテナ '${CONTAINER_NAME}' の /app/data マウント元が今回と異なります。
  旧マウント元: ${EXISTING_DATA_MOUNT}
  新マウント先: ${DATA_DIR}
※旧ディレクトリ内の既存データ（metadata.db / thumbnails）は削除されず、旧パス側にそのまま残存しています。
  - 旧データをそのまま使い続ける場合: src/TagBasedVideoManager/appsettings.Production.json の "DATA_DIR" に旧パスを設定してください（例: "DATA_DIR": "${EXISTING_DATA_MOUNT}"）。
  - 新ディレクトリへ既存データを移行する場合: 以下を実行して既存データをコピーしてください:
    cp -a "${EXISTING_DATA_MOUNT}/." "${DATA_DIR}/"
EOF
    fi
fi

# 3. ポート事前チェック
if command -v lsof >/dev/null 2>&1; then
    if lsof -i ":${PORT}" >/dev/null 2>&1; then
        echo "警告: ポート ${PORT} は既に使用されています。" >&2
    fi
elif command -v nc >/dev/null 2>&1; then
    if nc -z localhost "${PORT}" >/dev/null 2>&1; then
        echo "警告: ポート ${PORT} は既に使用されています。" >&2
    fi
fi

# 4. イメージビルド
echo "--- コンテナイメージをビルド中 ---"
${RUNTIME} build -t "${IMAGE_NAME}" -f "${REPO_ROOT}/Dockerfile" "${REPO_ROOT}"

# 5. 既存コンテナの停止 & 削除
echo "--- 既存コンテナの停止・削除 ---"
${RUNTIME} stop "${CONTAINER_NAME}" 2>/dev/null || true
${RUNTIME} rm -f "${CONTAINER_NAME}" 2>/dev/null || true

# 6. GPU 判定
GPU_ARGS=()
if command -v nvidia-smi >/dev/null 2>&1; then
    GPU_ARGS=(--gpus all)
elif [ -e /dev/dri ]; then
    GPU_ARGS=(--device /dev/dri:/dev/dri)
fi

# 7. 新コンテナ起動
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

# 8. ヘルスチェック (最大30秒)
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
