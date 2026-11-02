<#
.SYNOPSIS
    OpenSpec SDD/TDD ワークフローのフェーズゲート（完了基準 / Definition of Done）を機械的に検証する。

.DESCRIPTION
    ドキュメント上のルール（ソフトガードレール）だけでは防げない「レビュー飛ばし」「未記入テンプレ放置」
    「テストの skip 化」「未完了タスクのまま Archive」等を機械的に検査し、未達項目があれば終了コード 1 で停止する。

    Gate 種別:
      - pre-propose (Gate 0): トピックブランチ（feature/*, fix/* 等）上にいるか、ベースブランチ直作業でないか
      - propose     (Gate 1): 必須計画成果物（proposal/specs/design/test_plan/tasks/mockup）と Propose レビュー LGTM の確認
      - apply       (Gate 2): Apply レビュー記録（LGTM・未記入プレースホルダなし）とテスト skip 放置がないことの確認
      - archive     (Gate 3): 全タスク [x] 完了、handover.md 記入済み、総括レビュー LGTM、DoD（テスト・skip放置0）の確認

.PARAMETER Gate
    検証するゲート名（pre-propose | propose | apply | archive）。

.PARAMETER ChangeName
    対象の OpenSpec change 名（pre-propose 以外では必須）。

.PARAMETER Level
    適用規模レベル（L1 | L2 | L3）。省略時は .openspec.yaml または proposal.md から自動判定（既定: L2）。

.PARAMETER TargetPath
    プロジェクトルートパス。既定: カレントディレクトリ。

.PARAMETER BaseBranch
    ベースブランチ名。既定: develop

.PARAMETER SkipGitCheck
    Git ブランチ検証をスキップする（テスト環境用）。

.EXAMPLE
    pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose
    pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName add-user-auth -Level L2
    pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply   -ChangeName add-user-auth
    pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName add-user-auth
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('pre-propose', 'propose', 'apply', 'archive')]
    [string]$Gate,

    [string]$ChangeName,

    [ValidateSet('', 'L1', 'L2', 'L3')]
    [string]$Level = '',

    [string]$TargetPath = '.',

    [string]$BaseBranch = 'develop',

    [switch]$SkipGitCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]

function Pass([string]$Message) {
    Write-Host "  [PASS] $Message" -ForegroundColor Green
}

function Fail([string]$Message) {
    $script:Failures.Add($Message)
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
}

function Warn([string]$Message) {
    $script:Warnings.Add($Message)
    Write-Host "  [WARN] $Message" -ForegroundColor Yellow
}

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

$root = (Resolve-Path -LiteralPath $TargetPath).Path
Write-Host "=== OpenSpec Phase Gate Verification: [$Gate] ===" -ForegroundColor Cyan

# --- Gate 0: Git ブランチ安全チェック -----------------------------------------
if (-not $SkipGitCheck) {
    $gitDir = Join-Path $root '.git'
    if (Test-Path -LiteralPath $gitDir) {
        $branch = (git -C $root branch --show-current 2>$null | Out-String).Trim()
        $forbidden = @('main', 'master', 'develop', $BaseBranch) | Select-Object -Unique
        if (-not $branch) {
            Fail "Git ブランチを特定できません（Detached HEAD の可能性があります）"
        }
        elseif ($forbidden -contains $branch) {
            Fail "ベースブランチ '$branch' 上で直接作業しています。feature/<name> または fix/<name> ブランチへ切り替えてください"
        }
        elseif ($branch -notmatch '^(feature|fix|refactor|chore)/.+') {
            Warn "ブランチ名 '$branch' が標準規約（feature/*, fix/*, refactor/*, chore/*）に従っていません"
        }
        else {
            Pass "トピックブランチ確認: $branch"
        }
    }
}

if ($Gate -eq 'pre-propose') {
    if ($script:Failures.Count -gt 0) {
        Write-Host "Gate [pre-propose] NG: $($script:Failures.Count) 件の違反があります。" -ForegroundColor Red
        exit 1
    }
    Write-Host "Gate [pre-propose] OK" -ForegroundColor Green
    exit 0
}

# --- ChangeName 必須確認 ------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ChangeName)) {
    throw "-Gate $Gate には -ChangeName <name> の指定が必須です。"
}

$changeDir = Join-Path $root "openspec/changes/$ChangeName"
if (-not (Test-Path -LiteralPath $changeDir -PathType Container)) {
    Fail "変更ディレクトリが存在しません: $changeDir"
    exit 1
}

# --- 適用規模レベル（L1/L2/L3）の解決 -----------------------------------------
$openspecYamlPath = Join-Path $changeDir '.openspec.yaml'
$openspecYaml = if (Test-Path -LiteralPath $openspecYamlPath) { Read-Utf8 $openspecYamlPath } else { '' }
$skipSpecs = $openspecYaml -match '(?m)^\s*skip_specs:\s*true\b'

$proposalPath = Join-Path $changeDir 'proposal.md'
$proposalText = if (Test-Path -LiteralPath $proposalPath) { Read-Utf8 $proposalPath } else { '' }

if (-not $Level) {
    if ($openspecYaml -match '(?m)^\s*level:\s*(L[123])\b') {
        $Level = $Matches[1]
    }
    elseif ($proposalText -match '\b(L[123])\b') {
        $Level = $Matches[1]
    }
    else {
        $Level = 'L2'
    }
}
Write-Host "  Change: $ChangeName / Level: $Level / skip_specs: $skipSpecs" -ForegroundColor Gray

# --- 共通関数: reviews.md の検証 ----------------------------------------------
function Assert-ReviewsValid([string]$PhaseLabel) {
    $reviewsPath = Join-Path $changeDir 'reviews.md'
    if (-not (Test-Path -LiteralPath $reviewsPath -PathType Leaf)) {
        Fail "reviews.md が存在しません（$PhaseLabel レビューが未実施です）"
        return
    }
    $rev = Read-Utf8 $reviewsPath

    # 旧表形式の未判定記号検査
    if ($rev.Contains('✅/❌/⚠️')) {
        Fail "reviews.md に未判定のチェックリスト項目（'✅/❌/⚠️'）が残っています"
    }
    # 新設問・破壊テストテンプレートの未記入プレースホルダ検査
    elseif ($rev -match '(?m)(:\s*|-\s*)<(具体的な確認内容|ファイル名|テスト名|実行したコマンド|未記入|指摘内容|対応方針の理由)[^>]*>') {
        Fail "reviews.md に未記入のプレースホルダ（'<...>'）が残っています"
    }
    else {
        Pass "reviews.md の全レビュー設問・確認項目が記入済み"
    }

    $lgtmMatches = [regex]::Matches($rev, '(?im)^-\s*\*\*LGTM\*\*:\s*(true|false)\b')
    if ($lgtmMatches.Count -eq 0) {
        Fail "reviews.md に '- **LGTM**: true' の判定記録がありません"
    }
    else {
        $lastLgtm = $lgtmMatches[$lgtmMatches.Count - 1].Groups[1].Value.ToLowerInvariant()
        if ($lastLgtm -ne 'true') {
            Fail "reviews.md の最新判定が LGTM: true ではありません（現在: $lastLgtm）"
        }
        else {
            Pass "reviews.md 最新レビュー判定: LGTM = true"
        }
    }
}

# --- Gate 1: Propose ゲート ---------------------------------------------------
if ($Gate -in @('propose', 'archive')) {
    if (-not (Test-Path -LiteralPath $proposalPath -PathType Leaf)) {
        Fail "proposal.md が存在しません"
    }
    else {
        Pass "proposal.md 存在確認"
    }

    $designPath = Join-Path $changeDir 'design.md'
    if ($Level -in @('L2', 'L3') -or -not $skipSpecs) {
        if (-not (Test-Path -LiteralPath $designPath -PathType Leaf)) {
            Fail "design.md が存在しません（Level $Level では必須）"
        }
        else {
            Pass "design.md 存在確認"
        }
    }

    $testPlanPath = Join-Path $changeDir 'test_plan.md'
    if ($Level -in @('L2', 'L3')) {
        if (-not (Test-Path -LiteralPath $testPlanPath -PathType Leaf)) {
            Fail "test_plan.md が存在しません（Level $Level ではテストケース・境界値定義が必須です）"
        }
        else {
            $tp = Read-Utf8 $testPlanPath
            if ($tp.Contains('<change-name>')) {
                Fail "test_plan.md に未記入のプレースホルダ '<change-name>' が残っています"
            }
            else {
                Pass "test_plan.md 存在・記入確認"
            }
        }
    }

    if (-not $skipSpecs) {
        $specsDir = Join-Path $changeDir 'specs'
        $specFiles = @(if (Test-Path -LiteralPath $specsDir) { Get-ChildItem -LiteralPath $specsDir -Recurse -Filter 'spec.md' -File })
        if ($specFiles.Count -eq 0) {
            Fail "specs/**/spec.md が存在しません（仕様変更がない場合は .openspec.yaml に 'skip_specs: true' を設定してください）"
        }
        else {
            Pass "デルタ仕様確認: $($specFiles.Count) ファイル"
        }
    }

    $tasksPath = Join-Path $changeDir 'tasks.md'
    if (-not (Test-Path -LiteralPath $tasksPath -PathType Leaf)) {
        Fail "tasks.md が存在しません"
    }
    else {
        $tasksText = Read-Utf8 $tasksPath
        if ($tasksText -notmatch '(?m)^\s*-\s*\[[ xX]\]\s+.+') {
            Fail "tasks.md にチェックボックス形式のタスク（- [ ] ...）が含まれていません"
        }
        else {
            Pass "tasks.md 存在・タスク定義確認"
        }
    }

    Assert-ReviewsValid 'Propose'
}

# --- Gate 2: Apply ゲート -----------------------------------------------------
if ($Gate -in @('apply', 'archive')) {
    if ($Gate -eq 'apply') {
        Assert-ReviewsValid 'Apply'
    }

    # テストファイル内の skip / pending 放置検査（対象プロジェクト内の test* / spec* を走査。node_modules/bin/obj等は除外）
    $testDirs = @('test', 'tests') | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path -LiteralPath $_ }
    $skippedFound = New-Object System.Collections.Generic.List[string]
    foreach ($td in $testDirs) {
        $files = Get-ChildItem -LiteralPath $td -Recurse -File -Include '*.ts', '*.tsx', '*.js', '*.mjs', '*.cjs', '*.fs', '*.cs', '*.py', '*.ps1' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](node_modules|bin|obj|TestResults|\.git)[\\/]' }
        foreach ($f in $files) {
            $content = Read-Utf8 $f.FullName
            if ($content -match '\b(it|test|describe)\.skip\s*\(' -or
                $content -match '\b(xit|xdescribe|xtest)\s*\(' -or
                $content -match '\[(Fact|Theory)\s*\([^\]]*Skip\s*=' -or
                $content -match '(?m)^\s*It\s+["''].+["'']\s+-Skip\b') {
                $skippedFound.Add($f.FullName.Substring($root.Length).TrimStart('\', '/'))
            }
        }
    }
    if ($skippedFound.Count -gt 0) {
        Fail "Skip / Ignore 状態のテストが放置されています: $($skippedFound -join ', ')"
    }
    else {
        Pass "Skip / Ignore テスト放置なし"
    }
}

# --- Gate 3: Archive ゲート（Definition of Done） -----------------------------
if ($Gate -eq 'archive') {
    $tasksPath = Join-Path $changeDir 'tasks.md'
    if (Test-Path -LiteralPath $tasksPath -PathType Leaf) {
        $tasksText = Read-Utf8 $tasksPath
        $unchecked = [regex]::Matches($tasksText, '(?m)^\s*-\s*\[\s\]\s+.+').Count
        $checked = [regex]::Matches($tasksText, '(?m)^\s*-\s*\[[xX]\]\s+.+').Count
        if ($unchecked -gt 0) {
            Fail "tasks.md に未完了タスク（- [ ]）が $unchecked 件残っています"
        }
        elseif ($checked -eq 0) {
            Fail "tasks.md に完了済みタスク（- [x]）が 1 件もありません"
        }
        else {
            Pass "tasks.md 全 $checked タスク完了（未完了 0 件）"
        }
    }

    $handoverPath = Join-Path $changeDir 'handover.md'
    if (-not (Test-Path -LiteralPath $handoverPath -PathType Leaf)) {
        Fail "handover.md が存在しません（Archive 前に申し送り事項の作成が必須です）"
    }
    else {
        $ho = Read-Utf8 $handoverPath
        if ($ho.Contains('<change-name>') -or $ho.Contains('<YYYY-MM-DD>')) {
            Fail "handover.md に未記入のプレースホルダ（<change-name> / <YYYY-MM-DD>）が残っています"
        }
        else {
            Pass "handover.md 作成・記入確認"
        }
    }

    # 4ロール総括レビュー判定確認（LGTM: true、未記入項目なし）
    Assert-ReviewsValid 'Archive'
}

# --- 結果サマリ ---------------------------------------------------------------
if ($script:Failures.Count -gt 0) {
    Write-Host "=== Gate [$Gate] FAILED ($($script:Failures.Count) 件の未達項目) ===" -ForegroundColor Red
    exit 1
}

Write-Host "=== Gate [$Gate] PASSED ===" -ForegroundColor Green
exit 0
