# 多语言覆盖自检：比对代码里用到的 Loc.T/Loc.F 原文 与各语言字典的关键字
# 用法: powershell -ExecutionPolicy Bypass -File tools\check-i18n.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'src'

function Unesc([string]$s) {
    return [System.Text.RegularExpressions.Regex]::Unescape($s)
}

# 1) 代码中实际用到的文案（支持 "a" + "b" 拼接）
$used = New-Object System.Collections.Generic.HashSet[string]
Get-ChildItem $src -Filter *.cs | Where-Object { $_.Name -notlike 'Lang*.cs' } | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    $ms = [regex]::Matches($text, 'Loc\.(?:T|F)\(\s*((?:(?:"(?:[^"\\]|\\.)*")\s*\+?\s*)+)')
    foreach ($m in $ms) {
        $body = $m.Groups[1].Value
        $parts = [regex]::Matches($body, '"(?:[^"\\]|\\.)*"')
        $sb = ''
        foreach ($p in $parts) { $sb += (Unesc $p.Value.Substring(1, $p.Value.Length - 2)) }
        [void]$used.Add($sb)
    }
    # Fail 的条件分支会把不同原文转交给 Loc.T。
    foreach ($m in [regex]::Matches($text, 'Fail\(([\s\S]*?),\s*out error\)')) {
        foreach ($p in [regex]::Matches($m.Groups[1].Value, '"(?:[^"\\]|\\.)*"')) {
            [void]$used.Add((Unesc $p.Value.Substring(1, $p.Value.Length - 2)))
        }
    }
}

# 2) 各语言字典的 key
function Get-TableKeys([string]$path) {
    $text = [IO.File]::ReadAllText($path)
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($text, '\{\s*"((?:[^"\\]|\\.)*)"\s*,')) {
        [void]$set.Add((Unesc $m.Groups[1].Value))
    }
    return , $set
}

$langs = @{}
Get-ChildItem $src -Filter 'Lang*.cs' | ForEach-Object {
    $code = $_.BaseName.Substring(4)
    $langs[$code] = Get-TableKeys $_.FullName
}

"代码中实际使用文案: {0} 条" -f $used.Count

$bad = 0
# 3) 代码用到但字典里没有 -> 该语言会回退显示中文
foreach ($code in ($langs.Keys | Sort-Object)) {
    $missing = @($used | Where-Object { -not $langs[$code].Contains($_) } | Sort-Object)
    if ($missing.Count -gt 0) {
        $bad++
        "[{0}] 缺少 {1} 条:" -f $code, $missing.Count
        $missing | ForEach-Object { "    " + ($_ -replace "`r?`n", "\n") }
    }
}

# 4) 字典里有但代码已经不用了 -> 冗余
foreach ($code in ($langs.Keys | Sort-Object)) {
    $extra = @($langs[$code] | Where-Object { -not $used.Contains($_) } | Sort-Object)
    if ($extra.Count -gt 0) {
        "[{0}] 冗余 {1} 条 (代码中已无此文案, 可删):" -f $code, $extra.Count
        $extra | ForEach-Object { "    " + ($_ -replace "`r?`n", "\n") }
    }
}

if ($bad -eq 0) { "全部语言覆盖完整。" } else { "有 {0} 个语言存在缺口。" -f $bad; exit 1 }
