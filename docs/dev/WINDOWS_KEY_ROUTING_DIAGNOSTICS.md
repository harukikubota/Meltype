# Windows キー経路のログ採取

この調査版は、キーを取る条件や変換処理を変えずに、TSF と Meltype キーボードの経路を記録する。
Windows 実機は検証専用として、通常のビルド・インストール・公式アンインストールで使う。
検証用フラグがある間は、キー値・本文を含む送受信 JSON・UIA 情報を省略せず記録する。
これは今回の検証専用ブランチの採取仕様であり、製品のログ方針や通常ログの RecordText 設定とは別に扱う。

基点は upstream main の `6ee6a0bc`。ブランチ名は `investigate-windows-key-routing`。

## 1. Windows で調査ブランチを取得する

Windows 10 / 11 **x64**、Git、.NET 10 SDK、Windows Desktop Runtime 10、
Visual Studio Build Tools の「C++ によるデスクトップ開発」（MSVC x64/x86 と Windows SDK）、
Inno Setup 6 が必要。TIP ビルドの対象は x64/x86。

Windows の既存の Meltype リポジトリのルートで実行する。リモートは URL で指定しているので、既存のリモート名に依存しない。
初回だけ、取得したブランチからローカルブランチを作る。

```powershell
git fetch https://github.com/harukikubota/Meltype.git investigate-windows-key-routing
if ($LASTEXITCODE -ne 0) { throw '調査ブランチの取得に失敗しました。' }
git switch -c investigate-windows-key-routing FETCH_HEAD
if ($LASTEXITCODE -ne 0) { throw 'ブランチの切り替えに失敗しました。' }
git rev-parse --short HEAD
```

既にローカルに調査ブランチがある場合は、切り替えてから pull する。

```powershell
git switch investigate-windows-key-routing
if ($LASTEXITCODE -ne 0) { throw 'ブランチの切り替えに失敗しました。' }
git pull --ff-only https://github.com/harukikubota/Meltype.git investigate-windows-key-routing
if ($LASTEXITCODE -ne 0) { throw '調査ブランチの更新に失敗しました。' }
git rev-parse --short HEAD
```

出力されたコミット ID を結果メモに控える。以降のコマンドはこのブランチのルートで実行する。

## 2. 変換ヘルパーを用意する

`native\mozc\bin` にヘルパー・依存 DLL・`VC-RUNTIME.txt` が揃っていれば、このダウンロードは不要。
ない場合は最新リリースの Windows ZIP から取り出す。ここで使う ZIP はヘルパーの入手元であり、
ZIP 内の Meltype.exe は起動しない。Mozc のソースビルドは不要。

```powershell
$release = Invoke-RestMethod 'https://api.github.com/repos/yksr-melt/Meltype/releases/latest'
$asset = @($release.assets | Where-Object { $_.name -like 'Meltype-*-windows-portable.zip' })
if ($asset.Count -ne 1) { throw 'Windows portable ZIP が一意に見つかりません。Releases を確認してください。' }
$deps = Join-Path $env:TEMP ('Meltype-routing-deps-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $deps | Out-Null
Invoke-WebRequest $asset[0].browser_download_url -OutFile "$deps\release.zip" -UseBasicParsing
Expand-Archive "$deps\release.zip" "$deps\release"
$helper = @(Get-ChildItem "$deps\release" -Recurse -File -Filter meltype_mozc_helper.exe)
if ($helper.Count -ne 1) { throw 'Mozc ヘルパーが一意に見つかりません。' }
New-Item -ItemType Directory -Force native\mozc\bin | Out-Null
Copy-Item (Join-Path $helper[0].DirectoryName '*') native\mozc\bin -Force
if (-not (Test-Path native\mozc\bin\VC-RUNTIME.txt)) { throw 'VC-RUNTIME.txt がありません。' }
"取得したリリース: $($release.tag_name)"
Get-FileHash native\mozc\bin\meltype_mozc_helper.exe -Algorithm SHA256
```

Zenzai を使う比較では、同梱用バイナリ・モデルも用意する（既にある場合はハッシュ確認後に再利用する）。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\zenzai\Get-Zenzai.ps1
if ($LASTEXITCODE -ne 0) { throw 'Zenzai の準備に失敗しました。' }
```

## 3. 通常ビルド・インストールを実行する

トレイから今の Meltype を終了する。診断フラグはまだ作らず、先にビルド・通常インストールを完了させる。
既存の通常インストールを上書きし、設定は通常のインストーラーの扱いに任せる。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Meltype.ps1
if ($LASTEXITCODE -ne 0) { throw '通常インストールに失敗しました。' }
```

インストール時の IME 登録の UAC は許可する。`-NoIme` は付けない。
`Build-Package.ps1` は TIP ビルド失敗時にもキーボード方式だけで続行するため、
インストール時に「Meltype IME をビルドできない」と表示されたら、C++ のビルドエラーを確認する。
インストール後はビルド出力・通常インストール先・登録先で TIP のハッシュを照合する。

```powershell
$installed = Join-Path $env:LOCALAPPDATA 'Programs\Meltype'
$registered = Join-Path $env:ProgramFiles 'Meltype\tip'
foreach ($arch in 'x64', 'x86') {
    $expected = (Get-Content "native\tip\bin\$arch\MeltypeTipImpl.dll.sha256" -Raw).Trim()
    $installedId = (Get-Content "$installed\tip\$arch\MeltypeTipImpl.dll.sha256" -Raw).Trim()
    $registeredId = (Get-Content "$registered\$arch\MeltypeTipImpl.dll.sha256" -Raw).Trim()
    if ($expected -ne $installedId -or $expected -ne $registeredId) {
        throw "$arch の TIP が今回のビルドと一致しません。採取を始めないでください。"
    }
    $loadedFile = "$registered\$arch\impl\$registeredId\MeltypeTipImpl.dll"
    if (-not (Test-Path $loadedFile)) { throw "$arch の登録先 impl がありません。" }
    "$arch impl=$registeredId"
}
Get-Process Meltype | Select-Object Id, Path
# Path が $installed\Meltype.exe であること。
```

登録先のファイル一致は、入力先プロセスがロードした版の証明にはならない。
次節の再起動と `Activate impl=...` の照合まで行う。

## 4. ログを有効にする

専用フラグは `%LOCALAPPDATA%\Meltype\key-routing-log`。
通常の「ログ / 判定理由」や `tip-log`、`MELTYPE_TIP_LOG` の設定は不要。
通常のログ画面の記録も、判定理由の補足として一緒に保存してよい。

```powershell
$logDir = Join-Path $env:LOCALAPPDATA 'Meltype'
New-Item -ItemType Directory -Force $logDir | Out-Null
New-Item -ItemType File -Force "$logDir\key-routing-log" | Out-Null
```

フラグは各プロセスが最初に参照した値を保持する。**Windows を再起動**し、Meltype と入力先アプリ、
Explorer を新しいプロセスにする。採取終了時もフラグ削除後に再起動する。

再起動後、トレイの動作モードを **Meltype IME** にして、Win+Space で Meltype を選ぶ。
メモ帳の空欄で `kakunin` を手で打って Space、Enter と操作する。次の両ファイルができることを確認する。

```powershell
$logDir = Join-Path $env:LOCALAPPDATA 'Meltype'
Get-Item "$logDir\key-routing-tip.log", "$logDir\key-routing-managed.log" |
    Select-Object Name, Length, LastWriteTime
Select-String -Path "$logDir\key-routing-tip.log" -Pattern 'Activate|OnTestKeyDown|OnKeyDown|HandleKey'
Select-String -Path "$logDir\key-routing-managed.log" -Pattern 'TSF core-send|TSF core-result'
```

`Activate` の `impl` を前節の x64 または x86 のハッシュと照合する。
ログが片方しかない、`stale=1` が出る、`Send transport ... ok=0` が続く場合は、
インストール先・ロードした DLL・Meltype の起動状態を先に確認する。
調査中に自動更新が入った場合も、ハッシュを再確認し、条件が変わった区間を分ける。

## 5. ケースを短く分けて採取する

検証専用機で全量採取する。通過したキー・キー解放・注入入力も記録するため、操作の成否と時刻を残す。

### Microsoft 公式アプリの単独キー比較

英字キーは **Clipchamp の S**、Space は **Clipchamp と Edge**、機能キーは **Explorer の F2 と Edge の F11** で比較する。
S は Shift を押さない物理 S キー。かな入力でも押す位置を同じにする。

| ID | アプリと準備 | 単独キー | 期待する動作 |
|---|---|---|---|
| C1 | Clipchamp の個人向け無料プランでテスト用の動画をタイムラインに置き、クリップを選択。再生位置をクリップの途中へ | S | クリップを分割する。次の試行前に Ctrl+Z で戻す |
| C2 | Clipchamp でタイトルや字幕の入力欄からフォーカスを外し、タイムラインをクリック | Space | 再生／一時停止 |
| B1 | Edge で縦に長い通常ページを開き、本文の余白をクリック。検索欄・リンク・ボタンにはフォーカスを置かない | Space | 約1画面下へスクロール |
| B2 | 同じ Edge のページ | F11 | 全画面の ON/OFF |
| E2 | Explorer でテスト用ファイルを選択 | F2 | ファイル名編集を開始。次の入力欄の比較にも使う |

Clipchamp の分割・再生は個人向け無料プランの基本編集で試せる。Windows アプリを使い、アカウントへのサインインと素材の準備を済ませる。
Microsoft の公式説明: [Clipchamp のキー割当](https://support.microsoft.com/en-us/clipchamp/keyboard-shortcuts-for-clipchamp)、
[無料プラン](https://support.microsoft.com/en-us/clipchamp/what-is-clipchamp)、
[Edge のキー割当](https://support.microsoft.com/en-us/edge/keyboard-shortcuts-in-microsoft-edge)、
[Explorer のキー割当](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows)。

最初は未確定入力のない状態で、次の表を埋める。各セルに「成功／不動作／別動作」と時刻を記録する。

| 入力フロント | 指定したモード | フォーカス後の実際のモード | C1: S | C2: Space | B1: Space | B2: F11 | E2: F2 |
|---|---|---|---|---|---|---|---|
| Meltype IME (TSF) | 日本語 | | | | | | |
| Meltype IME (TSF) | 英数 | | | | | | |
| Meltype キーボード | 日本語 | | | | | | |
| Meltype キーボード | 英数 | | | | | | |
| Microsoft IME | 日本語 | | | | | | |
| Microsoft IME | OFF | | | | | | |

「入力方式」をローマ字／かなの意味でも比較する場合は、その設定ごとに同じ表を作る。
フロント (TSF/Keyboard) と入力方式 (ローマ字/かな) を混同しない。
非入力欄では OS が IME を無効化する場合があるため、指定した状態とフォーカス後の実際の状態を分ける。
選択できなければ「選択不可／自動 OFF」と記録する。Microsoft IME の比較中は Meltype キーボードを終了する。

### 入力開始・既存の再現ケース

各ケースの前後に時刻・ケース ID・入力経路・表示モードをメモする。
`Get-Date -Format o` の時刻と、ログの `tick`（起動からのミリ秒）、PID・入力先スレッド・`instance` で突き合わせる。
キーはまず手でゆっくり打ち、必要なら同じケースを通常の速さで繰り返す。

| ID | 操作 | 画面で記録すること |
|---|---|---|
| N1 | メモ帳の空欄で `kakunin`、Space、Enter | 先頭文字を含めて変換されるか、確定結果 |
| S1 | Google スプレッドシートの空セルを1回クリックし、編集に入らず `kakunin`、Space、Enter | 最初の k が別に確定しないか。最初のキー直後と最後の表示 |
| S2 | 別の空セルで F2 を押して編集に入り、同じ入力 | S1 との差。数式バー内で入力した場合は別ケースとする |
| E1 | 空の検証用フォルダーに `aaa.txt` `bbb.txt` `uuu.txt` を作り、一覧の aaa を選択して u | uuu が選択されるか。左上などに変換表示が出るか |
| E2 | 検証用ファイルで F2、`kakunin`、Space。記録後 Esc で取り消す | 本物のファイル名編集欄では変換できるか |
| X1 | Win+X の後 U を1回。メニューの反応を記録して Esc で閉じる | U のアクセラレータが動くか。シャットダウン等の項目は実行しない |
| V1 | 動画プレーヤーをクリックしてフォーカスし Space | 再生・一時停止か、スクロールか、変換表示か。サイト名・フォーカス位置も記録 |
| N2 | メモ帳で非変換中の Space、次に `nihongo` の変換中の Space、Enter | 空白入力と変換操作の区別 |
| N3 | メモ帳で短い長押し、続いて Shift/Ctrl/Alt を押して離してから通常入力 | 重複・欠落、離した後の修飾キー残留 |

E1 の検証用ファイルは新しい空フォルダーで作り、既存ファイルは使わない。
Win+X の項目や動画の操作は OS 言語・プレーヤーによって異なるため、Microsoft IME / IME OFF でも同じ場所を比較する。

まず **Meltype IME（TSF）の日本語モード**で採取し、再現したケースと N1/N2 を次の条件で比較する。

1. Meltype IME の英数モード。ログの `language` と `open` を記録する。
2. トレイから **Meltype キーボード**に変更。ログに `Keyboard route`・`Keyboard core-result` が出ることを確認する。
3. Microsoft IME の日本語モード。
4. Microsoft IME を OFF にした直接入力。Microsoft IME の日本語モードとは別に記録する。

比較 3・4 では Meltype キーボードを終了し、Microsoft IME と同時に動かさない。
Meltype 内の英数モードは、Microsoft IME への切替や IME OFF と同じではない。
各ケースの開始時に未確定文字をなくし、空セル・空欄・同じ選択位置に戻す。

Google スプレッドシートで再現しなければ LibreOffice Calc で S1/S2 を追加する。
**Sheets/Calc で正常でも Excel の解決とは扱わない。**
Office を持つ担当者には Excel のセル選択/F2 の比較と、
Word の空文書・文中で `hokano` を打つケースを渡す。
元の報告の `kakuninn`・`eigakan`・`huzaketa` も入力条件を分けて確認する。

## 6. 保存・停止

採取後は2秒以上待ち、次の2ファイルだけを結果メモ・必要な画面記録と一緒に保存する。
判定理由も必要なら、通常のログ画面の記録を添える。専用ログの JSON には本文・候補・周辺文字も含まれる。

```powershell
$logDir = Join-Path $env:LOCALAPPDATA 'Meltype'
$capture = Join-Path ([Environment]::GetFolderPath('Desktop')) ('Meltype-key-routing-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $capture | Out-Null
Start-Sleep -Seconds 2
Copy-Item "$logDir\key-routing-tip.log", "$logDir\key-routing-managed.log" $capture
Get-ChildItem $capture | Select-Object Name, Length
Remove-Item "$logDir\key-routing-log"
$capture
```

フラグ削除後に Windows を再起動し、専用ログが増えないことを確認する。
ログにサイズ上限による停止・行長の切り詰め・キュー数による破棄は設けていない。
OS が配送しないイベントやディスク書き込み失敗までは記録できないため、画面結果も残す。

別の検証へ移るときは、ログを保存してから**現行の公式アンインストーラー**を使う。
今回の main は旧 `Uninstall-Meltype.ps1` / `uninstall.ps1` ではなく Inno Setup に移行している。
トレイの「アンインストール...」または Windows の「設定」→「アプリ」から Meltype を削除する。
標準登録されたアンインストーラーを PowerShell から呼ぶ場合は次を使う。

```powershell
$entry = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype_is1'
$uninstaller = $entry.UninstallString.Trim('"')
if (-not (Test-Path -LiteralPath $uninstaller)) { throw '公式アンインストーラーが見つかりません。' }
Start-Process -FilePath $uninstaller -Wait
```

IME 登録解除の案内に従い、旧 DLL を読み込んだプロセスも終了させるため Windows を再起動する。
次の検証版も通常の `Install-Meltype.ps1` で入れる。独自の配置先・独自の登録解除手順は使わない。

結果メモには次を残す。

- 調査ブランチのコミット ID、impl のハッシュ、インストール日時
- Windows の版・ビルド・x64、アプリ名と版、JIS/US、画面数・拡大率
- 実際のフロント、各ケースの日本語/英数/IME OFF、アプリ別設定、ライブ変換、Mozc/Zenzai の有無と版
- ケース ID・開始/終了時刻・キー列・期待・実際・再現回数。最初の1文字だけ変換から外れたか
- ログ ON/OFF での再現差。TIP 側は同期でファイルに書くため、ログによってタイミングが変わる可能性がある

## ログの読み方と限界

| ログ | 分かること |
|---|---|
| TIP の `OnTestKeyDown` / `OnKeyDown` | `eaten` と `reason`。begin 行の -1 は未判定。前後の ctx / identity でコンテキスト切替を確認 |
| `OnTestKeyUp` / `OnKeyUp` / `OnPreservedKey` | 押下から引き継いだ消費状態。修飾キーは mods（Shift=1、Ctrl=2、Alt=4、Win=8） |
| `HandleKey key` | 受信した VK・文字コード・scan。Test/Down/Up の各行にも VK・scan を記録。preserved key の scan は登録キーからの生成値 |
| `GetSelection` / `RequestEditSession` / `StartComposition` / `SetText` / `SetSelection` | HRESULT とコンテキスト。RequestEditSession の受付結果と sessionHr は分けて読む。非同期受付は実行完了の証明ではない |
| `Send` と `TSF core-send/result` | instance / clientThread が対応。通信失敗か、Core が consumed=false としたか、commit/view があったか |
| `Reinject` / `IsReinjected` | 再注入要求、SendInput の受付数、TIP が再注入を検出したか。marked=false は時間での推定であり物理キーと断定しない |
| `hook` / `Keyboard route` / `Keyboard capture` | 押下/解放、物理・注入の区別、停止/通過/キュー投入の理由。通過・消費を問わず VK・scan・flags・注入マーカー・OS の時刻を記録 |
| `focus-event/result` / `focus controlType` | HWND、フォーカスの更新、UIA ControlType、入力欄/パスワード判定。UIA の要素名・クラス名も記録 |
| `Keyboard core-result` / `Keyboard commit` / `SendInput` | Core の消費結果、確定経路、OS の注入受付結果 |

`Send request/response` と `event-json/result-json` に、実際の要求・応答を全文保存する。
既存処理が取得した周辺文字・選択・候補・タイトル等も含まれる。
採取のためにパスワード欄の本文を新たに取得したり、Core の秘密欄処理を変更したりはしないが、
フックや TIP に届いたキー値は欄の種類によらず記録する。

`SendInput` 成功や TIP での再注入検出は、**対象アプリのコマンド実行まで証明しない**。
Microsoft IME の内部や、TSF に届く前に OS が処理したキーもこのログだけでは分からない。
画面での結果と対にして判断する。管理者権限・保護画面の TIP はログ出力対象外。

## 調査の根拠

- [#985](https://github.com/yksr-melt/Meltype/issues/985): Excel のセル選択からの入力開始
- [#1958](https://github.com/yksr-melt/Meltype/issues/1958): Excel/Word、先頭文字と Word のカーソル位置
- [#1956](https://github.com/yksr-melt/Meltype/issues/1956): フォームのフロント名と診断欄 Tsf が不一致
- [#818](https://github.com/yksr-melt/Meltype/issues/818): Explorer 一覧の文字キー
- [#819](https://github.com/yksr-melt/Meltype/issues/819): Win+X 後の U（Win+X 自体とは分ける）

main では `TextService::WouldEat` にメニュー・修飾キー・入力欄無効化の通過条件がある。
また capture に一致するキーや変換中の経路は、メニュー判定より前に分岐する。
この順序が各症状の原因かは未確定で、ログの reason と画面結果で確認する。
Excel の編集開始時のコンテキスト切替と、非テキスト領域の横取りは別の仮説として扱う。
