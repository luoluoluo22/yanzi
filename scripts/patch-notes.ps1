$token = $env:GITHUB_TOKEN
$headers = @{
    "Authorization" = "token $token"
    "Accept"        = "application/vnd.github.v3+json"
}

$releaseId = "358429943"

$notes = @"
# 鐕曞瓙 Yanzi v0.2.24 鍙岀鏇存柊鍐呭

### 鉁?鐢佃剳绔笌鎵嬫満绔叏骞虫粦鍗忓悓
- **銆愭闈㈣矾寰勮В鏋愩€?* 淇鐢佃剳绔?Desktop 鐩稿璺緞瑙ｆ瀽锛坄ResolveFsPath`锛夛紝褰诲簳瑙ｅ喅鎵嬫満绔墦寮€妗岄潰鏂囨湰涓庡浘鐗囨枃浠舵彁绀轰笉瀛樺湪鐨勯棶棰樸€?- **銆愯法绔枃浠舵煡鐪嬩笌楂樻竻棰勮銆?* 鏀寔鍦ㄦ墜鏈虹鐩存帴鍦ㄧ嚎鎵撳紑骞剁紪杈戠數鑴戠鏂囨湰鏂囦欢锛屽浘鐗囨枃浠舵彁渚?Base64 鍘熺敓娓叉煋楂樻竻寮圭獥棰勮銆?- **銆愭櫤鑳芥枃浠剁被鍨嬪浘鏍囥€?* 鍩轰簬鎵╁睍鍚嶈嚜鍔ㄥ尮閰嶆樉绀烘枃鏈€佸浘鐗囥€佹枃浠跺す绛夌幇浠ｆ枃浠剁被鍨嬪浘鏍囥€?
### 馃摫 鎵嬫満绔?UI 涓庝氦浜掍綋楠岄噸鏋?- **銆愬叏灞忓榻愮簿缇庡竷灞€銆?* 閲嶆瀯鈥滅數鑴戔€漈ab 鏍囬銆佸瓙 Tab 鏍忎笌鈥滅數鑴戞墿灞曗€濇悳绱㈡ Padding 杈硅窛锛屼笌鈥滅嚂骞曗€濈瓑椤剁骇 Tab 椤甸潰 `20dp` 鍐呯缉杈硅窛 100% 涓ヤ笣鍚堢紳銆?- **銆愭墿灞曠綉鏍间腑杞村眳涓€?* 鎵╁睍鍥炬爣缃戞牸鍦ㄤ腑杞寸嚎涓婄粷瀵瑰眳涓绉帮紝琛岄棿璺濆噺鍗婏紝鍛堢幇鏇村姞绱у噾楂樼骇鐨勫崱鐗囩晫闈€?- **銆愯亰澶╂粴灞忎笌鍘嗗彶娓呯┖銆?* 淇鑱婂ぉ Tab 椤堕儴 Header 涓庡簳閮?Input 杈撳叆鏍忓浐瀹氶敋瀹氾紝鏀寔闀挎寜鑱婂ぉ鍖哄煙涓€閿揩鎹锋竻绌哄叏閮ㄥ巻鍙茶褰曘€?- **銆愭枃浠剁鐞?Popup 涓婁紶銆?* 鍘婚櫎鍐椾綑璺緞鎻忚堪涓庢枃瀛楋紝鎼滅储鏍忓乏渚ф柊澧?`+` 蹇嵎涓婁紶 Popup 鑿滃崟锛堟敮鎸佹枃浠躲€佺収鐗囦笌瀹炴椂鎷嶇収涓婁紶锛夈€?
### 鈿?绋冲畾鎬т笌缃戠粶骞跺彂閫氫俊寮哄寲
- **銆愪簯绔腑缁цВ鍖呬紭鍖栥€?* 閲嶆瀯 Payload 鎻愮偧鏈哄埗锛屽畬缇庤В鍐冲湪闈炲眬鍩熺綉浜戠涓户缃戠粶鐜涓嬬殑 PowerShell 涓庢枃浠跺垪琛ㄦ彁鍙栧欢杩熶笌鍗￠】銆?- **銆愯鍥剧敓鍛藉懆鏈熷畨鍏ㄣ€?* 褰诲簳瑙ｅ喅 Android 绔噸鏋勮繃绋嬩腑鍥犻噸澶嶆寕杞?View 寮曡捣鐨勮繍琛屾椂宕╂簝銆?
---
*Yanzi v0.2.24 Windows 鐢佃剳绔?& Android 鎵嬫満绔弻绔悓鏃跺彂甯?
"@

$jsonString = '{"name":"鐕曞瓙 Yanzi v0.2.24","body":' + ($notes | ConvertTo-Json) + '}'
$bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonString)

Invoke-RestMethod -Uri "https://api.github.com/repos/luoluoluo22/yanzi/releases/$releaseId" -Method Patch -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
Write-Host "Successfully patched release notes to Chinese!"
