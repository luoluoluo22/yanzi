$token = if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { "" }
$headers = @{
    "Authorization" = "token $token"
    "Accept"        = "application/vnd.github.v3+json"
    "User-Agent"    = "PowerShell"
}

$releaseUrl = "https://api.github.com/repos/luoluoluo22/yanzi/releases/tags/v0.3.6"
$release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -Method Get
$releaseId = $release.id

$b64 = "eyJuYW1lIjoi54eV5a2QIFlhbnppIHYwLjMuNiIsImJvZHkiOiIjIOeHleWtkCBZYW56aSB2MC4zLjYg5pu05paw5YaF5a65XG5cbioq6Ieq5Yqo5pu05paw5L2T6aqj5YWo6Z2i5Y2H57qnKipcbi0g44CQ5p6B6YCf6ZWc5YOP6YCa6YGT44CR6Ieq5Yqo5pu05paw6YeN5p6E5Li6IFNwbGl0QnV0dG9uIOWIhua裂5oyJ6ZKu6K6+6K6h77yM6buY6K6k54K55Ye75Y2z5Y+v5L2/55So6auY6YCf6ZWc5YOP5rqQ77yBZ2hmYXN0LnRvcO+8ieaehemAn+ajgOa1i+W5tuiHquWKqOWQr+WKqOS4i+i9ve+8jOWRiuWllue9kee7nOazouWKqOWvvOiHtOeahOS4i+i9veWksei0peaIlOWNoemhu+OAolxuLSDjgJHopb/mtLvmupDliIfmjaLjgJFJ5Zyo5pu05paw5oyJ6ZKu5Y+z5L6n5o+Q5L6b5LiL5ouJ6I+c5Y2V77yM5Y+v5Zyo4oCc6ZWc5YOP5pu05pawICjpu5jorqTmjqjotLUp4oCd5LiO4oCcR2l0SHVi5pu05pawICjlrrDmnafnm7Tov54p4oCd5LmL6Ze06Ieq55Sx5oyJ6ZyA5YfiltersKCi0g44CQ5a6e5pe25LiL6L296L+b5bqm5p2h44CR5paw5aKe6Ieq5Yqo5pu05paw5LiL6L296L+b5bqm5p2h5LiO55m+5YiG5q+U5pWw5YC85Y+N6aaI77yM5ZCO5Y+w5aKe6YeP5YyF5LiL6L295LiO57uE6KOF6L+H56iL5LiA55uu5LqG54S244GCCioq8J+Wse+4jyDpuKDmoIfliafmtLLkuI7lv6vmjbflpIfor5HluLrlvLoqKgotIOOAkOaWsOWinteCtrlCt+W3pumUrumbhumAgOOAkeWcqOiuvue9rueVjOmdoueahOm4oOagh+inpuWPkemAiemhuSBO5Lit77yM5paw5aKe4oCcQ3RybCvltoZp6ZKu56e75Yqo4oCd6ZSu55eb57uE5ZCI5pSv5oyB77yM5LiL5ouJ6YCJ6aG55LiO5Lit6ZKu56e75Yqo5L+d5oyB5a6M5YWo5LiA6Ie077yI5YyF5ZCr77ya56aB55So44CB6IOM5YyF44CB54eV546v44CB54eV5bmV44CB56qX5Y+j5o6S5YiX44CB6byg5qCH5omL5Yq/77yJ44GCCi0g44CQ5omL5Yq/5bqV5bGC6LCD5bqm5L+u5aSN44CR5L+u5aSN5LqG5bqV5bGC5omL5Yq/5pyN5Yqh5Zyo6Z2e5qCH5YeG5Trigger6ZKu5LiL55qE5rOo5YaM5b2S5LiA5YyW5LiO54mp55CG5oyJ6ZKu54q25oCB5Yik5a6a6Zeu6aKY77yM56Gu5L+dIEN0cmwrbWluzro56ZKu56e75Yqo57uY5Yi26byg5qCH5omL5Yq/55m+5YiG55m+56iz5a6a5ZON5bqU5LiO6K+G5Yir44GCCi0tLQoy5LiA6ZSu5a6J6KOF5YyF77yaWWFuemktd2luLVNldHVwLTAuMy42LmV4ZSJ9"
$bytes = [System.Convert]::FromBase64String($b64)

$patchUrl = "https://api.github.com/repos/luoluoluo22/yanzi/releases/$releaseId"
$res = Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method Patch -ContentType "application/json; charset=utf-8" -Body $bytes
Write-Host "Patch result: $($res.name)"
