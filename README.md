# Gemini API Key 測試頁面

.NET 10 ASP.NET Core Razor Pages 本機工具。首頁頂部的「API Key 測試頁面」連結導向 `/ApiKeyTest`，可送出一段文字並查看 Gemini 的純文字回覆。每次請求獨立，不保存歷史，也不自動重試。

## 本機使用

1. 在專案根目錄的 `gemini-api-key.txt`，將 `YOUR_GEMINI_API_KEY` 替換成一組真實 Gemini API Key，不加引號，不使用 `key=` 或 JSON。`#` 開頭的提示行可保留。
2. 執行以下命令，再開啟 `http://localhost:5277/ApiKeyTest`。

```sh
dotnet restore TestAPIKeyCalling.csproj
dotnet build TestAPIKeyCalling.csproj --no-restore
dotnet run --project TestAPIKeyCalling.csproj --no-build --launch-profile http
```

也可使用既有 VS Code F5 設定。這次未實際操作 VS Code F5。

輸入會送到 Google Gemini。最多接受 4,000 個 UTF-16 單位，部分 emoji 計為兩個單位；原生表單送出時換行為 CRLF，計兩個單位。合法輸入不修剪或截斷。回覆只顯示純文字，保留空行，不解析 HTML 或 Markdown。

模型由 `appsettings.json` 的 `Gemini:Model` 指定，預設為 `gemini-2.5-flash`。修改模型後重新啟動。模型不可用時會顯示提示，不切換其他模型。依 [Google 模型說明](https://ai.google.dev/gemini-api/docs/models/gemini-2.5-flash)，2.5 模型的存取可能受到限制。

## Key 保護

- Key 檔案只在本機保存。開發時已先驗證 `/gemini-api-key.txt` 忽略規則，再建立提示與占位檔案；建立時使用僅擁有者可讀寫的權限。已有檔案不覆寫。
- Key 未被 Git 追蹤，並明確排除建置／發佈複製；檔案不在 `wwwroot`，不能透過網站下載。
- 每次合法送出由後端重新讀檔，修改 Key 不需重啟。讀檔失敗只提示檔名與修正方向，不顯示檔案內容或實體路徑。
- 頁面沒有 Key 輸入、編輯或顯示欄位。瀏覽器只提交輸入與防偽權杖，不直接呼叫 Gemini。
- Key 只放在後端 `x-goog-api-key` 標頭，不放在網址或請求本文。HTTP 用戶端停用內建日誌並設定標頭遮蔽；自訂日誌只記錄錯誤類別、HTTP 狀態及耗時，不附例外、輸入或回覆。
- HTTP 不跟隨重新導向，避免認證標頭流向其他位址。若遠端回覆意外包含完整 Key，拒絕顯示該回覆。
- 缺檔時只提示建立格式，應用程式不自動建立 Key 檔案。不要將真實 Key 放在聊天、截圖、測試資料、程式或其他設定檔。

本工具沿用 localhost 啟動設定，僅供本機自用。不要改成公開或多人共用服務。

## 實作與驗證

`Services/` 分離輸入驗證、Key 讀取與 Gemini 呼叫；PageModel 處理表單及畫面狀態。呼叫遵照 [generateContent 官方介面](https://ai.google.dev/api/generate-content)，60 秒逾時，支援瀏覽器中止取消。只組合第一個候選的一般文字，排除思考及遭阻擋內容；截斷回覆保留文字並提示不完整。

不依賴測試框架的驗證程式使用暫存檔、假 Key、替換的 HTTP handler 與真正的 loopback Razor Pages HTTP 伺服器，不向 Gemini 發送請求。失敗回傳非零退出碼，例外與秘密值不列印。

```sh
dotnet run --project Verification/Verification.csproj
```

成功摘要：

```text
Summary: 87/87 checks passed.
```

另提供純模擬的瀏覽器驗證伺服器；此模式位於獨立驗證程式，正式應用程式沒有模擬端點或切換開關。

```sh
dotnet run --project Verification/Verification.csproj -- --serve
```

開啟 `http://127.0.0.1:5288/ApiKeyTest` 可測試一般回覆；輸入 `simulate:quota`、`simulate:script`、`simulate:truncated`、`simulate:leading-newline` 可分別檢查配額錯誤、標記純文字顯示、截斷及開頭空行。

選用的 Playwright CLI 回歸流程（在另一個終端執行，需 Node.js/npm 與 Chrome）：

```sh
npx --package @playwright/cli playwright-cli -s=gemini-check open http://127.0.0.1:5288/ApiKeyTest --browser chrome
browser_check_code="$(cat Verification/browser-checks.js)"
npx --package @playwright/cli playwright-cli -s=gemini-check run-code "$browser_check_code"
```

瀏覽器腳本最後回傳 `passed: true`、`consoleErrors: 0`、`pageErrors: 0`，並將合成資料截圖存至 `/private/tmp/gemini-api-key-test-mobile.png` 與 `/private/tmp/gemini-api-key-test-desktop.png`。不會擷取真實 Key。

## 本次驗收紀錄

以下為 2026-10-13 實際執行結果，沒有真實 Gemini 請求或真實 Key 驗收。

| 規格案例 | 驗證與結果 |
| --- | --- |
| AC-01～02 | HTTP 與瀏覽器驗證導覽、初次空欄位與唯讀結果；通過 |
| AC-03 | 模擬成功回覆、輸入與換行保留；通過。真實 Key／Gemini 成功仍未驗證 |
| AC-04～06 | 瀏覽器驗證處理中停用、重複提交、空白、emoji 與原生表單換行邊界；後端另以 HTTP 拒絕超長；通過 |
| AC-07～10 | 暫存檔驗證缺檔、註解、占位、多組、無法讀取、UTF-8/BOM/LF/CRLF、控制字元及重新讀檔；通過 |
| AC-11～13 | 模擬 HTTP 錯誤、網路、逾時、取消、阻擋、無文字、異常 JSON、思考排除、片段順序與截斷；通過 |
| AC-14～15 | HTTP 與瀏覽器驗證標記不執行、開頭空行保留、成功後失敗清空結果；通過 |
| AC-16～17 | 真正框架拒絕缺少／無效防偽權杖；Trace 診斷、HTML 與瀏覽器請求不含假 Key，瀏覽器只存取本機；通過 |
| AC-18 | 忽略且未追蹤；檢查 `bin/`、`obj/` 與實際發佈輸出，不含 Key 檔案或驗證程式；通過 |
| AC-19～20 | 模擬指定模型、404 不切換、缺省／不合法模型、獨立請求及每次重新讀 Key；通過。遠端模型可用性未驗證 |

執行命令及結果：

```sh
dotnet restore TestAPIKeyCalling.csproj
dotnet build TestAPIKeyCalling.csproj --no-restore
dotnet build Verification/Verification.csproj --no-restore
dotnet format TestAPIKeyCalling.csproj --verify-no-changes --no-restore
dotnet format Verification/Verification.csproj --verify-no-changes --no-restore
dotnet publish TestAPIKeyCalling.csproj --no-restore --output /private/tmp/gemini-publish-check
git check-ignore -v gemini-api-key.txt
git ls-files -- gemini-api-key.txt
git -c core.whitespace=cr-at-eol diff --check
```

建置為零警告／零錯誤，還原、兩個專案的格式驗證與發佈成功；Key 忽略規則生效，追蹤清單無該檔案。Git 空白檢查使用 `cr-at-eol` 對應專案要求的 CRLF，不更改 Git 設定。

正式 `http` 啟動設定也已實際啟動於 `http://localhost:5277`，確認測試頁 GET 回傳 200 與禁止快取標頭，含合法防偽權杖的空白 POST 正確顯示驗證提示；兩者均不呼叫 Gemini。

起始專案的三個 PageModel 僅整理行尾及 using 分組，以符合既有 `.editorconfig`。原始 `SpecFile/spec1.md` 保持不變。程式經獨立唯讀審查，修正原生表單換行長度與結果框首行換行問題，再通過回歸驗證。本次未提交或推送 GitHub。
