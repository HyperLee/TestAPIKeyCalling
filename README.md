# TestAPIKeyCalling — Gemini API Key 測試工具

本專案是以 **.NET 10 與 ASP.NET Core Razor Pages** 開發的本機網頁工具，用來測試 Gemini Developer API 的 Key 與文字生成流程。使用者在瀏覽器輸入文字，後端從本機檔案讀取單一 API Key，呼叫 Gemini，將文字回覆顯示在頁面下方。

文件以目前程式碼為準。[原始規格書](SpecFile/spec1.md)保留需求與驗收背景，其中「尚未實作」與「後續開發」描述屬於規格制定時的歷史狀態；目前測試頁、Key 讀取服務、Gemini 呼叫與獨立驗證程式均已實作。

## 目錄

- [功能與使用範圍](#功能與使用範圍)
- [快速開始](#快速開始)
- [頁面操作與輸入限制](#頁面操作與輸入限制)
- [模型設定](#模型設定)
- [專案結構與元件責任](#專案結構與元件責任)
- [請求流程與回應處理](#請求流程與回應處理)
- [API Key 檔案與保護](#api-key-檔案與保護)
- [錯誤排查](#錯誤排查)
- [開發與偵錯](#開發與偵錯)
- [驗證程式與瀏覽器模擬](#驗證程式與瀏覽器模擬)
- [驗證紀錄與限制](#驗證紀錄與限制)

## 功能與使用範圍

目前提供以下功能：

- 共用頂部導覽列的「API Key 測試頁面」入口，導向 `/ApiKeyTest`。
- 上方可編輯的「輸入內容」框、送出按鈕、狀態提示，以及下方唯讀的「回傳結果」框。
- 前後端輸入驗證、處理中停用送出，以及成功、截斷與失敗狀態。
- 從專案根目錄讀取單組 Key；每次合法請求重新讀取，方便更換 Key 後再次測試。
- 非同步、非串流文字生成，依錯誤類別提供繁體中文提示。
- 不使用測試框架的驗證程式，以及供瀏覽器回歸使用的模擬伺服器。

每次測試只傳送當次輸入，不保存聊天歷史、不帶入先前問答，也不自動重試。結果以純文字呈現，保留換行，可捲動、選取與複製，不解析 HTML 或 Markdown。

此工具以 localhost 啟動，適合本機自用。目前沒有登入、多人管理、多組 Key、資料庫、模型選單、圖片／音訊／檔案上傳、工具呼叫或用量統計。若要公開或多人使用，需要另行設計存取控制與用量限制。

## 快速開始

### 1. 準備環境

需要：

- .NET 10 SDK，可使用 `dotnet --version` 確認目前 SDK。
- 可連線至 Google Gemini 的網路環境。
- 一組可使用目標模型的 Gemini Developer API Key。
- 瀏覽器；VS Code 為選用開發工具。

以下命令均從含有 `TestAPIKeyCalling.csproj` 的專案根目錄執行。

### 2. 設定本機 Key 檔案

先確認專案已有根目錄限定的忽略規則，且 Key 檔未被追蹤：

```sh
git check-ignore -v gemini-api-key.txt
git ls-files -- gemini-api-key.txt
```

第一個命令應指出 `.gitignore` 中的 `/gemini-api-key.txt` 規則，第二個命令應沒有輸出。已被追蹤的檔案不會因新增忽略規則而自動取消追蹤；若第二個命令列出檔名，先處理追蹤狀態，再填入真實 Key。

若 `gemini-api-key.txt` 不存在，請用編輯器在專案根目錄建立 UTF-8 檔案；若已存在，直接編輯既有檔案。格式如下：

```text
# 請將下一行替換為一組 Gemini API Key，不需加引號。
YOUR_GEMINI_API_KEY
```

將占位文字替換成自己的 Key，只保留一組有效值。不加引號，不使用 `key=` 或 JSON；`#` 開頭的註解行可以保留。Key 檔應限制為擁有者可讀寫；macOS 可在檔案建立後執行：

```sh
chmod 600 gemini-api-key.txt
```

應用程式不會自動建立此檔案。不要把真實 Key 放入程式碼、設定檔、README、聊天、截圖或測試資料。

### 3. 還原、建置與啟動

```sh
dotnet restore TestAPIKeyCalling.csproj
dotnet build TestAPIKeyCalling.csproj --no-restore
dotnet run --project TestAPIKeyCalling.csproj --no-build --launch-profile http
```

啟動後開啟：

- 首頁：`http://localhost:5277/`
- 測試頁：`http://localhost:5277/ApiKeyTest`

也可從首頁頂部點擊「API Key 測試頁面」。修改程式後，使用 `--no-build` 啟動前須重新建置。

HTTPS 啟動方式：

```sh
dotnet run --project TestAPIKeyCalling.csproj --no-build --launch-profile https
```

此 profile 設定 `https://localhost:7187` 與 `http://localhost:5277`；HTTPS 測試頁為 `https://localhost:7187/ApiKeyTest`。若本機尚未信任 ASP.NET Core 開發憑證，可使用 `dotnet dev-certs https --trust` 後再啟動。HTTP 與 HTTPS profile 均使用 `Development` 環境。

### 4. 送出第一個測試

1. 在上方輸入框輸入文字，例如「請用繁體中文簡短介紹你能協助的事情」。
2. 點擊「送出測試」。合法輸入會送至 Gemini，按鈕暫時停用並顯示「處理中…」。
3. 完成後查看狀態訊息與下方結果框。
4. 若失敗，依提示修正 Key、模型或網路，再送出測試。

Key 檔修改後不需重啟；模型設定修改後需要重新啟動。

## 頁面操作與輸入限制

| 狀態 | 畫面與行為 |
| --- | --- |
| 初次開啟 | 輸入與結果為空，按鈕可用，不呼叫 Gemini |
| 驗證失敗 | 保留輸入、清除舊結果、顯示提示，不呼叫 Gemini |
| 處理中 | 清除舊結果、停用送出按鈕、顯示「處理中…」 |
| 成功 | 顯示回覆與「測試成功」，按鈕恢復可用 |
| 截斷 | 保留取得的文字，提示內容可能不完整 |
| 失敗 | 結果為空，狀態區顯示錯誤，允許再次測試 |

輸入不可為空字串、純空白或只有換行，最多 **4,000 個 UTF-16 code unit**。後端依 .NET `string.Length` 計算；前端則先把換行換算成原生表單提交時的 CRLF 長度再驗證。

| 範例 | 計算方式 |
| --- | --- |
| 一般中文字或 ASCII 字元 | 通常每個字元占一個 UTF-16 單位 |
| `😀` | 占兩個 UTF-16 單位，2,000 個恰為 4,000 單位 |
| 表單中的一次換行 | 提交為 CRLF，占兩個 UTF-16 單位 |
| 3,998 個 `a` 加一次換行 | 共 4,000 單位，可送出 |
| 3,999 個 `a` 加一次換行 | 共 4,001 單位，拒絕送出 |

合法內容不修剪或默默截斷；長度計算包含前後空白。JavaScript 用於操作體驗與同頁重複提交防護，後端仍獨立驗證。

停用按鈕只防止同頁處理中的重複提交，不提供跨分頁去重。這是原生表單 POST，重新整理結果頁並確認瀏覽器的重新提交提示，可能再次呼叫 API。

## 模型設定

[appsettings.json](appsettings.json) 保存非機密的模型名稱：

```json
{
  "Gemini": {
    "Model": "gemini-2.5-flash"
  }
}
```

這是設定片段，修改時保留檔案中的其他設定。程式透過 ASP.NET Core 設定系統讀取 `Gemini:Model`，啟動時建立 `GeminiSettings`，不提供頁面上的模型選單或設定熱更新。

- 設定不存在時，使用程式預設值 `gemini-2.5-flash`。
- 明確設定空字串或空白時視為設定錯誤，不回退至預設值。
- 第一個字元必須是 ASCII 英文字母或數字；其餘只能是 ASCII 英文字母、數字、`-`、`_` 或 `.`。
- 不接受 URL、`models/` 前綴、其他路徑或空白。
- 修改模型後重新啟動；模型不可用時顯示錯誤，不自動切換其他模型。

預設值是目前專案設定，不代表所有 Key 均有存取權，也不保證遠端模型持續可用。本文件未驗證目前 Google 端的模型存取狀態。

## 專案結構與元件責任

```text
TestAPIKeyCalling/
├── Program.cs                    # 應用程式入口、服務註冊與 middleware
├── TestAPIKeyCalling.csproj       # net10.0 與輸出排除規則
├── appsettings.json              # 非機密模型與一般設定
├── Pages/
│   ├── Index.cshtml               # 起始首頁
│   ├── ApiKeyTest.cshtml          # 測試表單與結果畫面
│   ├── ApiKeyTest.cshtml.cs       # GET／POST 與畫面狀態
│   ├── Privacy.cshtml             # 起始隱私頁
│   ├── Error.cshtml               # 共用錯誤頁
│   └── Shared/                    # 共用 layout 與驗證片段
├── Services/
│   ├── FileApiKeyReader.cs        # Key 檔讀取與格式驗證
│   ├── InputValidation.cs         # 共用輸入規則
│   ├── GeminiService.cs           # 遠端呼叫與回應解析
│   └── GeminiResult.cs            # 介面、設定、結果與錯誤型別
├── wwwroot/
│   ├── js/api-key-test.js         # 前端驗證與送出狀態
│   ├── css/                       # 自訂樣式
│   └── lib/                       # 既有第三方前端套件
├── Verification/                 # 獨立驗證程式與瀏覽器腳本
├── Properties/launchSettings.json # HTTP／HTTPS 本機 profile
├── .vscode/                      # 建置與 F5 設定
├── SpecFile/spec1.md              # 原始需求規格
└── gemini-api-key.txt             # 本機自行設定，不納入 Git 或輸出
```

`bin/` 與 `obj/` 是產生的建置檔案；上方 Key 檔為本機設定用途，可能尚未存在。

| 元件 | 責任與主要介面 |
| --- | --- |
| `Program` | 註冊 Razor Pages、Key 讀取服務、固定模型設定與 Gemini HTTP 用戶端；設定安全錯誤頁、路由及靜態資產 |
| `ApiKeyTestModel` | 綁定 `Input`；GET 不呼叫服務，POST 驗證後更新 `Output`、訊息與樣式；禁止頁面快取 |
| `InputValidation` | `GetError` 檢查空白與長度；頁面及服務共用規則 |
| `IApiKeyReader`／`FileApiKeyReader` | `ReadAsync` 非同步讀取固定 Key 檔，設定錯誤轉為安全訊息 |
| `IGeminiService`／`GeminiService` | `GenerateAsync` 接受文字與取消權杖，回傳 `GeminiResult` |
| `GeminiResult` | 包含 `Outcome`、`Text`、`Message` 與 `Error`；結果狀態為 `Success`、`Truncated` 或 `Error` |
| `GeminiSettings` | 保存啟動時讀取的模型名稱及預設值 |
| `api-key-test.js` | 驗證表單長度、清除舊結果、停用按鈕與阻止同頁重複送出 |

主專案不新增獨立公開 API 端點；瀏覽器只使用 Razor Pages 表單，由後端呼叫 Gemini。

## 請求流程與回應處理

### 從表單到 Gemini

```text
瀏覽器輸入
  → JavaScript 驗證空白與提交長度
  → POST /ApiKeyTest（Input 與防偽權杖）
  → Razor Pages 防偽驗證
  → PageModel 輸入與 ModelState 驗證
  → GeminiService 再次驗證輸入及模型
  → FileApiKeyReader 重新讀取本機 Key
  → 後端 HTTP POST 至 Gemini
  → 分類 HTTP 狀態、解析回應
  → PageModel 更新狀態與純文字結果
  → Razor 編碼輸出至結果框
```

後端請求使用以下路徑與標頭；此處只展示格式，不包含真實 Key：

```http
POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
Content-Type: application/json
x-goog-api-key: <後端從本機檔案讀取>
```

請求本文由 JSON 序列化產生，避免手動拼接引號、換行與 Unicode：

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [
        { "text": "請用繁體中文簡短介紹你能協助的事情。" }
      ]
    }
  ]
}
```

只送出當次使用者文字，沒有歷史、額外指令、工具或自訂生成參數。HTTP 用戶端逾時為 60 秒，不跟隨重新導向、不自動重試。瀏覽器中止請求時，`RequestAborted` 會傳至服務與讀檔操作；使用者中止不分類為逾時。

### 如何判定回覆可顯示

1. 先檢查 HTTP 成功狀態，再解析 JSON；成功 HTTP 狀態不保證有文字。
2. 檢查 `promptFeedback.blockReason`；遭阻擋時不顯示候選內容。
3. 只取第一個候選，檢查其 `finishReason`；安全相關阻擋不顯示內容，其他非正常結束視為異常。
4. 依序直接串接 `content.parts` 的一般文字，排除 `thought: true` 與非文字片段，不額外插入分隔符。
5. `STOP` 且有非空白文字為成功；`MAX_TOKENS` 且有文字為截斷，保留文字並提示不完整。
6. 無候選、無一般文字或只有空白時顯示無可用文字提示；格式異常顯示安全摘要。
7. 若文字意外包含完整 Key，拒絕顯示該回覆。

Razor 對回覆進行 HTML 編碼，結果框只呈現文字，不執行回傳標記。結果框也處理 HTML `textarea` 忽略第一個換行的行為，保留回覆開頭的空行。

## API Key 檔案與保護

### 路徑與解析規則

Key 路徑使用 `ContentRootPath` 加上固定檔名 `gemini-api-key.txt`，不從使用者提交內容取得路徑，也不依目前工作目錄推算。

- 接受 UTF-8、UTF-8 BOM、LF 與 CRLF；無效 UTF-8 會拒絕。
- 每行先去除前後空白，忽略空白行與以 `#` 開頭的註解行。
- 不支援行尾註解；有效 Key 必須獨占一行。
- 剩餘有效行必須恰好一行，且不可為 `YOUR_GEMINI_API_KEY`。
- Key 只接受可安全用於標頭的可列印 ASCII 範圍，不接受內部空白、控制字元或非 ASCII 字元。
- 不檢查固定前綴或長度，真正認證結果由 Gemini 決定。

每次輸入及模型驗證通過後重新讀檔，不將 Key 固定快取至程式結束。缺檔、多組值、占位文字或讀取失敗時，只顯示檔名與修正方向，不顯示內容或完整實體路徑，也不發出 Gemini 請求。

### 保護措施

| 位置或行為 | 目前實作 |
| --- | --- |
| Git | `.gitignore` 使用 `/gemini-api-key.txt`；仍須確認未被追蹤 |
| 建置與發佈 | `.csproj` 排除 Key 檔及 `Verification/**` 的內容／一般檔案，並排除驗證程式的編譯項目 |
| 靜態檔案 | Key 位於內容根目錄，沒有放在 `wwwroot` 或提供下載端點 |
| 瀏覽器 | 沒有 Key 輸入、編輯或顯示欄位，不直接向 Gemini 發送請求 |
| 遠端認證 | Key 僅加入後端 `x-goog-api-key` 標頭，不放在 URL 或 JSON 本文 |
| HTTP 日誌 | 移除用戶端內建 logger 並設定標頭遮蔽 |
| 自訂診斷 | 只記錄錯誤類別、HTTP 狀態與耗時，不附例外、Key、輸入、回覆或原始錯誤本文 |
| 重新導向 | `AllowAutoRedirect = false`，避免認證標頭跟隨遠端重新導向 |
| 頁面與例外 | 測試頁禁止快取；Development 也使用共用安全錯誤頁 |

模型設定不是秘密，Key 則只能保存在本機檔案。不要用真實 Key 進行截圖或建立回歸測試資料。

## 錯誤排查

| 情境 | 說明與處理方向 |
| --- | --- |
| 空白或超過 4,000 單位 | 修改輸入；emoji 與表單換行需依 UTF-16 規則計算 |
| 找不到 Key 檔 | 在內容根目錄建立 UTF-8 `gemini-api-key.txt`，只填一組 Key |
| 占位、多組值或無法讀取 | 替換占位、移除額外有效行，檢查權限、UTF-8 與內部空白 |
| 模型設定錯誤 | 使用合法單一識別字，修改 `Gemini:Model` 後重啟 |
| HTTP 400 | 通常提示請求或服務設定不符；結構化錯誤明確指出 Key 問題時改為認證提示 |
| HTTP 401／403 | 檢查 Key、權限與 API 存取限制；不能僅由此保證問題是 Key 格式 |
| HTTP 404 | 檢查模型名稱與資源可用性，不會自動切換模型 |
| HTTP 429 | 頻率或配額限制，稍後再試或檢查配額；程式不自動重試 |
| HTTP 5xx | Gemini 服務暫時異常，可稍後再試 |
| DNS、連線或 TLS 失敗 | 檢查網路及連線環境 |
| 60 秒逾時 | 稍後重新送出；不要把瀏覽器中止與逾時混為一談 |
| 安全阻擋 | 調整輸入；遭阻擋內容不會顯示 |
| 無可用文字 | 可能沒有候選、只有思考／非文字片段或只有空白 |
| JSON 格式或結束原因異常 | 顯示安全摘要，不輸出原始回應；可稍後再試 |
| 回覆達長度限制 | 有文字時保留並顯示警告，沒有文字時提示無文字及長度限制 |

遇到其他非成功 HTTP 狀態或未預期例外時，頁面顯示一般失敗提示。排查時使用錯誤類別、狀態碼及耗時，不將 Key 或完整請求／回應貼入日誌。

## 開發與偵錯

### 自動重新載入

```sh
dotnet watch --project TestAPIKeyCalling.csproj run --launch-profile http
```

### VS Code F5

以專案根目錄開啟 VS Code，並使用支援 `coreclr` 的 C# 偵錯工具。既有設定包含一個預設建置工作與一個啟動組態：

- 建置工作 `build TestAPIKeyCalling`，建置 Debug 組態。
- 啟動組態 `Debug TestAPIKeyCalling`，執行 `bin/Debug/net10.0/TestAPIKeyCalling.dll`。
- 工作目錄為專案根目錄，`args` 為空，環境為 Development。
- 直接使用 `http://localhost:5277`，服務就緒後開啟瀏覽器。

F5 使用自己的 URL 環境設定，不套用 `launchSettings.json` 的 profile。這次文件更新未實際操作 VS Code F5。

### 格式與差異檢查

```sh
dotnet format TestAPIKeyCalling.csproj --verify-no-changes --no-restore
dotnet format Verification/Verification.csproj --verify-no-changes --no-restore
git -c core.whitespace=cr-at-eol diff --check
```

這些命令只檢查，不重寫程式。C# 格式以 `.editorconfig` 為準，包含四個空白縮排、CRLF 行尾與不加最後換行。第三方套件位於 `wwwroot/lib/`，維護時避免無關修改。

## 驗證程式與瀏覽器模擬

### 獨立驗證程式

```sh
dotnet run --project Verification/Verification.csproj
```

這是一般主控台驗證程式，不依賴 xUnit、NUnit 或 MSTest，也沒有設定 coverage 門檻。它使用暫存 Key 檔、合成憑證、替換的 HTTP handler 與真正的 loopback Razor Pages HTTP 伺服器，不讀取專案真實 Key、不向 Gemini 發送請求。

驗證涵蓋 Key 格式與重新讀檔、輸入邊界、模型、HTTP 錯誤、逾時與取消、文字解析、HTML 編碼、防偽權杖、頁面狀態及安全診斷。個別案例輸出 `PASS`／`FAIL`；全部成功回傳退出碼 0，任一失敗回傳 1，不列印例外訊息或秘密值。

### 模擬伺服器

```sh
dotnet run --project Verification/Verification.csproj -- --serve
```

開啟 `http://127.0.0.1:5288/ApiKeyTest`。此模式只存在於獨立驗證程式，正式應用程式沒有模擬端點或切換開關。

| 輸入 | 模擬行為 |
| --- | --- |
| 一般合法文字 | 成功回覆，包含換行 |
| `simulate:quota` | 配額錯誤 |
| `simulate:script` | 帶 HTML／script 標記的文字，檢查純文字顯示 |
| `simulate:truncated` | 截斷回覆及警告 |
| `simulate:leading-newline` | 開頭空行保留 |

請勿把模擬指令送到正式測試頁期待特殊行為；正式頁會將其視為一般輸入並送至 Gemini。

### 選用的瀏覽器回歸

需要 Node.js／npm、Chrome 與 Playwright CLI；首次透過 `npx` 使用可能需要下載套件。先在一個終端啟動上述模擬伺服器，再從專案根目錄的另一個終端執行：

```sh
npx --package @playwright/cli playwright-cli -s=gemini-check open http://127.0.0.1:5288/ApiKeyTest --browser chrome
browser_check_code="$(cat Verification/browser-checks.js)"
npx --package @playwright/cli playwright-cli -s=gemini-check run-code "$browser_check_code"
```

[browser-checks.js](Verification/browser-checks.js) 驗證空白、emoji、原生換行邊界、處理中狀態、重複提交、標記不執行、回覆首行換行、失敗清空、截斷、手機版排列、本機網路請求及憑證保護。

成功時腳本回傳 `passed: true`、`consoleErrors: 0`、`pageErrors: 0`，並保存合成資料截圖：

- `/private/tmp/gemini-api-key-test-mobile.png`
- `/private/tmp/gemini-api-key-test-desktop.png`

此腳本用於模擬環境，不使用真實 Key。本次文件更新未重新執行瀏覽器回歸或產生截圖。

## 驗證紀錄與限制

### 本次文件更新：2026-10-13

本次僅擴充 `README.md`，重新核對最新程式碼與文件命令。實際執行結果如下：

| 命令或檢查 | 結果 |
| --- | --- |
| `dotnet build TestAPIKeyCalling.csproj --no-restore` | 建置成功，0 警告、0 錯誤 |
| `dotnet run --project Verification/Verification.csproj --no-build` | 87／87 檢查通過，退出碼 0 |
| `git check-ignore -v gemini-api-key.txt` | 根目錄 Key 忽略規則生效 |
| `git ls-files -- gemini-api-key.txt` | 無輸出，Key 未被追蹤 |
| `git -c core.whitespace=cr-at-eol diff --check` | 通過 |
| Markdown 本機連結、目錄錨點、程式碼圍欄及行尾空白 | 通過 |
| Git 變更範圍 | 僅 `README.md` |

本次驗證摘要：

```text
Summary: 87/87 checks passed.
```

第一次執行不含 `--no-build` 的驗證命令，完成服務層案例後停在受限執行環境的 Web host 初始化，已中止；隨後經允許在限制外以上表命令重跑，全部通過。沒有修改程式以繞過限制，也沒有讀取真實 Key 或發送 Gemini 請求。

### 先前實作驗收紀錄：2026-10-13

以下保留原 README 記錄的驗收表，屬於先前實作階段結果，不代表本次重新執行所有案例。

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

先前文件另記錄主專案與驗證專案建置、還原、兩個專案的格式檢查、發佈輸出、Key 忽略與未追蹤檢查，以及正式 HTTP profile 的 GET／空白 POST 驗證成功。先前瀏覽器回歸使用合成資料，並非真實 Gemini 驗收。

本次文件更新未重新執行上述瀏覽器、發佈、正式 profile 啟動或 VS Code F5 操作。真實 Key 的遠端認證、Gemini 成功回覆及目標模型可用性仍未驗證。
