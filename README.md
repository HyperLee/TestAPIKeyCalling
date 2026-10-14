# TestAPIKeyCalling — Gemini API Key 測試與對話工具

本專案以 **.NET 10 與 ASP.NET Core Razor Pages** 開發，提供兩個供本機使用的 Gemini Developer API 功能頁面：`/ApiKeyTest` 從本機檔案讀取 Key，進行單次文字測試；`/ApiKeyChat` 讓使用者在頁面輸入 Key，進行可帶入對話歷史的多輪文字對話，並選擇模型。

文件以目前程式碼為準。[spec1.md](SpecFile/spec1.md)說明 `/ApiKeyTest` 的原始需求；[spec2.md](SpecFile/spec2.md)描述 `/ApiKeyChat` 的多輪對話基礎；[spec3.md](SpecFile/spec3.md)補充聊天頁的模型選擇。這三份規格中的「尚未實作」或「後續開發」是文件撰寫時的歷史狀態，目前兩個功能頁面均已實作。

## 目錄

- [功能與使用範圍](#功能與使用範圍)
- [快速開始](#快速開始)
- [頁面操作與輸入限制](#頁面操作與輸入限制)
- [模型設定與選擇](#模型設定與選擇)
- [專案結構與元件責任](#專案結構與元件責任)
- [請求流程與回應處理](#請求流程與回應處理)
- [API Key 暫存與保護](#api-key-暫存與保護)
- [錯誤排查](#錯誤排查)
- [開發與偵錯](#開發與偵錯)
- [驗證程式與瀏覽器模擬](#驗證程式與瀏覽器模擬)
- [驗證紀錄與限制](#驗證紀錄與限制)

## 功能與使用範圍

目前提供以下功能：

- 共用導覽列提供「API Key 測試頁面」（`/ApiKeyTest`）與「API Key 對話頁面」（`/ApiKeyChat`）。
- `/ApiKeyTest`：從專案根目錄讀取一組 Key，每次只送出當次文字，不保留問答歷史；結果以純文字呈現，保留換行。
- `/ApiKeyChat`：在頁面輸入一組 Key，成功問答會保存在伺服器記憶體並用作後續對話脈絡；每個對話最多 20 組成功或含文字的截斷回合。
- 聊天頁支援三個固定模型選項，並可開始新對話、切換模型或清除 Key；切換至不同模型會清除當前對話歷史。
- 兩頁皆使用非串流文字生成與繁體中文錯誤提示；頁面以純文字安全呈現 Gemini 回覆，不解析 HTML 或 Markdown。
- 獨立驗證程式涵蓋 Key、Gemini 呼叫、聊天工作階段與頁面流程；另有針對 `/ApiKeyTest` 的瀏覽器模擬回歸。

應用程式透過 localhost／loopback 使用，適合本機自用；沒有登入、帳號管理、永久 Key 管理、資料庫、圖片／音訊／檔案上傳、工具呼叫或用量統計。`/ApiKeyChat` 的模型選擇只適用於聊天頁；若要公開或多人共用，需要另行設計存取控制與用量限制。

## 快速開始

### 1. 準備環境

需要：

- .NET 10 SDK，可使用 `dotnet --version` 確認目前 SDK。
- 可連線至 Google Gemini 的網路環境。
- 一組可使用目標模型的 Gemini Developer API Key。
- 瀏覽器；VS Code 為選用開發工具。

以下命令均從含有 `TestAPIKeyCalling.csproj` 的專案根目錄執行。

### 2. 為 `/ApiKeyTest` 設定本機 Key 檔案

這一步只供 `/ApiKeyTest` 使用。若只要使用 `/ApiKeyChat`，可略過本步驟，直接在該頁輸入 Key；聊天頁不讀取此檔案。

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

啟動後可開啟：

- 首頁：`http://localhost:5277/`
- 測試頁：`http://localhost:5277/ApiKeyTest`
- 對話頁：`http://localhost:5277/ApiKeyChat`

也可從共用導覽列點擊「API Key 測試頁面」或「API Key 對話頁面」。修改程式後，使用 `--no-build` 啟動前須重新建置。

HTTPS 啟動方式：

```sh
dotnet run --project TestAPIKeyCalling.csproj --no-build --launch-profile https
```

此 profile 設定 `https://localhost:7187` 與 `http://localhost:5277`；HTTPS 測試頁為 `https://localhost:7187/ApiKeyTest`，對話頁為 `https://localhost:7187/ApiKeyChat`。若本機尚未信任 ASP.NET Core 開發憑證，可使用 `dotnet dev-certs https --trust` 後再啟動。HTTP 與 HTTPS profile 均使用 `Development` 環境。

### 4. 開始測試或對話

使用 `/ApiKeyTest` 單次測試：

1. 在上方輸入框輸入文字，例如「請用繁體中文簡短介紹你能協助的事情」。
2. 點擊「送出測試」。合法輸入會送至 Gemini，按鈕暫時停用並顯示「處理中…」。
3. 完成後查看狀態訊息與下方結果框；若失敗，依提示修正 Key、模型或網路後重新送出。

使用 `/ApiKeyChat` 多輪對話：

1. 在「Gemini API Key」欄位輸入 Key，選擇模型（預選 Gemini 3.8 Flash），再按「開始對話」。此步只建立伺服器記憶體工作階段，不呼叫 Gemini。
2. 輸入訊息並按「送出」。後續訊息會連同本工作階段已完成的對話歷史送至 Gemini。
3. 按「新對話」清除歷史並保留 Key 和目前模型；選擇不同模型並按「切換模型」會清除歷史、保留 Key；按「清除 Key」則刪除 Key 與歷史並返回輸入畫面。

`/ApiKeyTest` 的 Key 檔修改後不需重啟；修改該頁的模型設定後需要重新啟動。`/ApiKeyChat` 的 Key 與模型選擇屬於目前工作階段，重新啟動應用程式後須重新輸入 Key。

## 頁面操作與輸入限制

### `/ApiKeyTest` 單次測試

| 狀態 | 畫面與行為 |
| --- | --- |
| 初次開啟 | 輸入與結果為空，按鈕可用，不呼叫 Gemini |
| 驗證失敗 | 保留輸入、清除舊結果、顯示提示，不呼叫 Gemini |
| 處理中 | 清除舊結果、停用送出按鈕、顯示「處理中…」 |
| 成功 | 顯示回覆與「測試成功」，按鈕恢復可用 |
| 截斷 | 保留取得的文字，提示內容可能不完整 |
| 失敗 | 結果為空，狀態區顯示錯誤，允許再次測試 |

#### `/ApiKeyTest` 輸入限制

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

### `/ApiKeyChat` 多輪對話

| 狀態或操作 | 畫面與行為 |
| --- | --- |
| 尚未建立工作階段 | 顯示空白密碼型 Key 欄位及模型選單；不呼叫 Gemini |
| 開始對話 | 驗證 Key 與模型後，將它們放入伺服器記憶體並設定隨機索引 cookie；不呼叫 Gemini |
| 送出訊息 | 顯示處理狀態；同一工作階段不接受併發送出 |
| 成功或含文字的截斷回覆 | 將使用者訊息與 Gemini 回覆一起加入歷史；截斷回覆另顯示不完整提示 |
| 明確失敗 | 不加入失敗回合，保留輸入與先前完成的歷史，允許使用者修改或重試 |
| 工作階段失效 | 清除頁面歷史並返回 Key 輸入畫面，提示重新輸入 |
| 達 20 組回合 | 保留 Key 與歷史，停止接受新訊息，提示開始新對話 |
| 新對話 | 清除歷史與已完成請求的去重資料，保留 Key 與目前模型 |
| 切換模型 | 換成不同模型時清除歷史、去重資料與截斷標記；保留 Key 及工作階段 cookie |
| 清除 Key | 刪除伺服器端 Key 與歷史、移除索引 cookie，返回 Key 輸入畫面 |

每則訊息不可為空白，最多 **4,000 個 UTF-16 code unit**；emoji 與換行的長度依提交內容及 .NET `string.Length` 計算。前端提示與後端驗證均會執行，超長內容不會被截斷。成功回合及有文字的截斷回合會作為後續對話脈絡；失敗回合不加入歷史。

工作階段閒置 30 分鐘後到期，應用程式重新啟動也會清除記憶體中的 Key 與歷史。同一瀏覽器的分頁共用其工作階段。切換到目前已選的模型不會清除歷史；模型切換或訊息處理期間，前端與伺服器會避免操作交錯。

## 模型設定與選擇

### `/ApiKeyTest` 固定模型設定

[appsettings.json](appsettings.json) 的 `Gemini:Model` 是 `/ApiKeyTest` 使用的非機密模型名稱：

```json
{
  "Gemini": {
    "Model": "gemini-2.5-flash"
  }
}
```

這是設定片段，修改時保留檔案中的其他設定。程式透過 ASP.NET Core 設定系統讀取 `Gemini:Model`，啟動時建立 `GeminiSettings`；`/ApiKeyTest` 不提供頁面模型選單或設定熱更新。

- 設定不存在時，使用程式預設值 `gemini-2.5-flash`。
- 明確設定空字串或空白時視為設定錯誤，不回退至預設值。
- 第一個字元必須是 ASCII 英文字母或數字；其餘只能是 ASCII 英文字母、數字、`-`、`_` 或 `.`。
- 不接受 URL、`models/` 前綴、其他路徑或空白。
- 修改模型後重新啟動；模型不可用時顯示錯誤，不自動切換其他模型。

`Gemini:Model` 的預設值是目前專案設定，不代表所有 Key 均有存取權，也不保證遠端模型持續可用。此設定不會改變 `/ApiKeyChat` 的模型選擇。

### `/ApiKeyChat` 模型選擇

聊天頁的固定允許清單與初始選項如下；清單及顯示提示依目前程式碼記載：

| 模型 ID | 頁面名稱 | 頁面提示 |
| --- | --- | --- |
| `gemini-3.8-flash` | Gemini 3.8 Flash | 建議選擇；請確認 API Key 所屬專案的付費狀態與模型權限 |
| `gemini-3.5-flash-lite` | Gemini 3.5 Flash-Lite | 較省成本 |
| `gemini-2.5-flash` | Gemini 2.5 Flash | 存取可能受限 |

新工作階段預選 `gemini-3.8-flash`。伺服器只接受清單中的模型 ID；聊天使用工作階段目前選定的模型，與 `/ApiKeyTest` 的 `Gemini:Model` 設定分開。模型不可用、Key 無權限或配額不足時顯示安全錯誤，不會自動切換或重試。選項提示不保證 Google 端的模型可用性或專案權限。

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
│   ├── ApiKeyChat.cshtml          # 多輪對話、Key 與模型選擇畫面
│   ├── ApiKeyChat.cshtml.cs       # 工作階段與對話 handlers
│   ├── Privacy.cshtml             # 起始隱私頁
│   ├── Error.cshtml               # 共用錯誤頁
│   └── Shared/                    # 共用 layout 與驗證片段
├── Services/
│   ├── FileApiKeyReader.cs        # Key 檔讀取與格式驗證
│   ├── InputValidation.cs         # 共用輸入規則
│   ├── GeminiService.cs           # 遠端呼叫與回應解析
│   ├── GeminiApiClient.cs         # 共用 HTTP 傳輸、解析及錯誤分類
│   ├── GeminiChatModels.cs        # 聊天頁的模型允許清單與顯示提示
│   ├── ChatSessionStore.cs        # 記憶體工作階段、歷史與併發控制
│   └── GeminiResult.cs            # 介面、設定、結果與錯誤型別
├── wwwroot/
│   ├── js/api-key-test.js         # 前端驗證與送出狀態
│   ├── js/api-key-chat.js         # Key 表單、聊天請求與純文字呈現
│   ├── css/                       # 自訂樣式
│   └── lib/                       # 既有第三方前端套件
├── Verification/                 # 獨立驗證程式與瀏覽器腳本
│   ├── ChatSessionChecks.cs       # 聊天工作階段及模型選擇檢查
│   ├── ChatServiceChecks.cs       # 多輪請求與模型端點檢查
│   └── ChatPageChecks.cs          # 聊天頁與安全行為檢查
├── Properties/launchSettings.json # HTTP／HTTPS 本機 profile
├── .vscode/                      # 建置與 F5 設定
├── SpecFile/spec1.md              # 原始需求規格
├── SpecFile/spec2.md              # 多輪對話基礎規格
├── SpecFile/spec3.md              # 聊天頁模型選擇擴充規格
└── gemini-api-key.txt             # 本機自行設定，不納入 Git 或輸出
```

`bin/` 與 `obj/` 是產生的建置檔案；上方 Key 檔為本機設定用途，可能尚未存在。

| 元件 | 責任與主要介面 |
| --- | --- |
| `Program` | 註冊 Razor Pages、聊天記憶體工作階段、兩種 Gemini 服務及共用 HTTP 用戶端；設定安全錯誤頁、路由及靜態資產 |
| `ApiKeyTestModel` | 綁定 `Input`；GET 不呼叫服務，POST 驗證後更新 `Output`、訊息與樣式；禁止頁面快取 |
| `InputValidation` | `GetError` 檢查 `/ApiKeyTest` 的輸入空白與長度；聊天頁另驗證每則訊息 |
| `IApiKeyReader`／`FileApiKeyReader` | `ReadAsync` 非同步讀取固定 Key 檔，設定錯誤轉為安全訊息 |
| `IGeminiService`／`GeminiService` | `GenerateAsync` 接受文字與取消權杖，回傳 `GeminiResult` |
| `GeminiResult` | 包含 `Outcome`、`Text`、`Message` 與 `Error`；結果狀態為 `Success`、`Truncated` 或 `Error` |
| `GeminiSettings` | 保存啟動時讀取的 `Gemini:Model` 名稱及預設值，供 `/ApiKeyTest` 使用 |
| `api-key-test.js` | 驗證表單長度、清除舊結果、停用按鈕與阻止同頁重複送出 |
| `ApiKeyChatModel` | 驗證 Key／模型與聊天輸入，處理開始工作階段、送出、新對話、清除 Key 及切換模型 |
| `IChatSessionStore`／`MemoryChatSessionStore` | 在伺服器記憶體保存 Key、選定模型、歷史與去重資料；管理期限及同工作階段併發操作 |
| `IGeminiChatService`／`GeminiChatService` | 以工作階段 Key、模型和多輪訊息呼叫 Gemini，回傳 `GeminiResult` |
| `GeminiChatModels` | 定義聊天頁允許的模型 ID、預選模型與畫面提示 |
| `GeminiApiClient` | 共用 Gemini HTTP 傳輸、回應解析、取消、逾時與錯誤分類；測試頁用設定模型，聊天頁用已選模型 |
| `api-key-chat.js` | 送出同源聊天請求、產生重送識別碼、控制處理狀態並以文字 API 呈現訊息 |

主專案不新增 Controller 或獨立對外 API。聊天頁的非同步訊息 POST 仍由同一 Razor Page handler 處理；瀏覽器不直接呼叫 Gemini。

## 請求流程與回應處理

### `/ApiKeyTest` 從表單到 Gemini

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

後端請求使用以下路徑與標頭；`/ApiKeyTest` 以 `Gemini:Model` 選定 `{model}`，Key 從本機檔案讀取。此處只展示格式，不包含真實 Key：

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

`/ApiKeyTest` 只送出當次使用者文字，沒有歷史、額外指令、工具或自訂生成參數。HTTP 用戶端逾時為 60 秒，不跟隨重新導向、不自動重試。瀏覽器中止請求時，`RequestAborted` 會傳至服務與讀檔操作；使用者中止不分類為逾時。

### `/ApiKeyChat` 多輪對話與模型切換

```text
GET /ApiKeyChat
  → 以同源索引 cookie 查找伺服器記憶體工作階段
  → 有效時顯示目前模型及歷史；無效或到期時顯示 Key 輸入畫面

POST /ApiKeyChat?handler=Begin
  → 防偽驗證、檢查 Key 格式及允許的模型 ID
  → 清除舊工作階段，保存新 Key／模型並設定隨機索引 cookie
  → 顯示空白對話，不呼叫 Gemini

POST /ApiKeyChat?handler=Send
  → 同源 fetch 提交當次訊息、requestId 與防偽資料
  → 後端讀取工作階段 Key、模型與已完成歷史，再呼叫 Gemini
  → 成功或有文字的截斷回覆才加入歷史；失敗時不新增回合

POST /ApiKeyChat?handler=ChangeModel
  → 只提交模型 ID 與防偽資料；不同模型會清除歷史及去重資料
  → 保留 Key 和工作階段 cookie，不呼叫 Gemini

POST /ApiKeyChat?handler=NewChat 或 ClearKey
  → NewChat 清除歷史並保留 Key／模型；ClearKey 移除 Key／歷史及 cookie
```

聊天頁最多保留 20 組成功或有文字的截斷問答。每次 Gemini 請求都包含該工作階段已完成的 `user`／`model` 歷史及本次 `user` 訊息；瀏覽器只送本次訊息，不送 Key 或整份歷史。相同 `requestId` 與訊息的重送可取回已保存結果，不會再次呼叫 Gemini；明確失敗的人工重試使用新的 ID。

聊天頁的 Gemini `contents` 會依序包含訊息角色與純文字，例如：

```json
{
  "contents": [
    { "role": "user", "parts": [{ "text": "第一個問題" }] },
    { "role": "model", "parts": [{ "text": "第一個回答" }] },
    { "role": "user", "parts": [{ "text": "接續問題" }] }
  ]
}
```

`/ApiKeyChat` 使用工作階段目前選定的模型建立相同 `generateContent` 路徑，Key 由後端記憶體讀取並放入 `x-goog-api-key` 標頭。聊天頁同樣使用 60 秒逾時、不自動重試及共用回應解析；模型錯誤時不會自動換模型。

### 如何判定回覆可顯示

1. 先檢查 HTTP 成功狀態，再解析 JSON；成功 HTTP 狀態不保證有文字。
2. 檢查 `promptFeedback.blockReason`；遭阻擋時不顯示候選內容。
3. 只取第一個候選，檢查其 `finishReason`；安全相關阻擋不顯示內容，其他非正常結束視為異常。
4. 依序直接串接 `content.parts` 的一般文字，排除 `thought: true` 與非文字片段，不額外插入分隔符。
5. `STOP` 且有非空白文字為成功；`MAX_TOKENS` 且有文字為截斷，保留文字並提示不完整。
6. 無候選、無一般文字或只有空白時顯示無可用文字提示；格式異常顯示安全摘要。
7. 若文字意外包含完整 Key，拒絕顯示該回覆。

`/ApiKeyTest` 的 Razor 輸出會對回覆進行 HTML 編碼，結果框只呈現文字，並保留開頭空行。`/ApiKeyChat` 的初始歷史由 Razor 編碼輸出，後續訊息以 DOM `textContent` 加入；兩頁都不會把 Gemini 回覆當成可執行的 HTML 或 Markdown。

## API Key 暫存與保護

### `/ApiKeyTest` 本機 Key 檔路徑與解析規則

Key 路徑使用 `ContentRootPath` 加上固定檔名 `gemini-api-key.txt`，不從使用者提交內容取得路徑，也不依目前工作目錄推算。

- 接受 UTF-8、UTF-8 BOM、LF 與 CRLF；無效 UTF-8 會拒絕。
- 每行先去除前後空白，忽略空白行與以 `#` 開頭的註解行。
- 不支援行尾註解；有效 Key 必須獨占一行。
- 剩餘有效行必須恰好一行，且不可為 `YOUR_GEMINI_API_KEY`。
- Key 只接受可安全用於標頭的可列印 ASCII 範圍，不接受內部空白、控制字元或非 ASCII 字元。
- 不檢查固定前綴或長度，真正認證結果由 Gemini 決定。

每次輸入及模型驗證通過後重新讀檔，不將 Key 固定快取至程式結束。缺檔、多組值、占位文字或讀取失敗時，只顯示檔名與修正方向，不顯示內容或完整實體路徑，也不發出 Gemini 請求。

### `/ApiKeyChat` 記憶體工作階段

聊天頁在開始對話時以同源 POST 接收 Key，先做本機格式驗證，不會為了建立工作階段而呼叫 Gemini。Key 與模型及對話歷史只保存在單一應用程式程序的專用記憶體快取；閒置 30 分鐘後到期，應用程式重新啟動後資料也會消失。

瀏覽器 cookie 只保存密碼學安全亂數產生的工作階段索引，不保存 Key、模型或對話內容。cookie 設為 `HttpOnly`、`SameSite=Strict`、`Path=/ApiKeyChat`；HTTPS 請求另設 `Secure`。新對話只清除歷史和去重資料；清除 Key 會刪除整個工作階段並移除 cookie。

### 保護措施

| 位置或行為 | 目前實作 |
| --- | --- |
| Git | `.gitignore` 使用 `/gemini-api-key.txt`；仍須確認未被追蹤 |
| 建置與發佈 | `.csproj` 排除 Key 檔及 `Verification/**` 的內容／一般檔案，並排除驗證程式的編譯項目 |
| `/ApiKeyTest` Key | 從內容根目錄讀取，不放在 `wwwroot` 或提供下載端點；瀏覽器沒有 Key 輸入或顯示欄位 |
| `/ApiKeyChat` Key | 密碼型欄位透過同源 POST 傳送一次；建立工作階段後不回傳至頁面或後續請求 |
| 聊天頁 cookie | 只含隨機索引，設定 `HttpOnly`、`SameSite=Strict`、限定路徑及 HTTPS `Secure` 屬性 |
| 遠端認證 | 兩頁皆由後端將 Key 放入 `x-goog-api-key` 標頭，不放在 URL 或 JSON 本文 |
| HTTP 日誌 | 移除用戶端內建 logger 並設定標頭遮蔽 |
| 自訂診斷 | 只記錄錯誤類別、HTTP 狀態與耗時，不附例外、Key、輸入、回覆或原始錯誤本文 |
| 重新導向 | `AllowAutoRedirect = false`，避免認證標頭跟隨遠端重新導向 |
| 頁面與例外 | 兩個功能頁皆禁止快取；Development 也使用共用安全錯誤頁 |

模型名稱不是秘密。`/ApiKeyTest` 的 Key 留在被忽略的本機檔案；`/ApiKeyChat` 的 Key 只在應用程式記憶體工作階段中暫存。不要把真實 Key 放入程式碼、Git、日誌、截圖、README 或測試資料。

## 錯誤排查

| 情境 | 說明與處理方向 |
| --- | --- |
| 空白或超過 4,000 單位 | 修改輸入；emoji 與表單換行需依 UTF-16 規則計算 |
| `/ApiKeyTest` 找不到 Key 檔 | 在內容根目錄建立 UTF-8 `gemini-api-key.txt`，只填一組 Key |
| `/ApiKeyTest` Key 檔錯誤 | 替換占位、移除額外有效行，檢查權限、UTF-8 與內部空白 |
| `/ApiKeyChat` Key 格式錯誤 | 確認欄位非空白，且 Key 沒有內部空白或不安全字元；格式錯誤不會建立工作階段 |
| `/ApiKeyChat` 工作階段失效 | 閒置逾 30 分鐘或應用程式重新啟動後，重新輸入 Key 開始對話 |
| 聊天已達 20 組回合 | 按「新對話」保留 Key 與目前模型並清空歷史 |
| `/ApiKeyTest` 模型設定錯誤 | 使用合法單一識別字，修改 `Gemini:Model` 後重啟 |
| `/ApiKeyChat` 模型不可用 | 檢查 Key 所屬專案的模型權限與配額；頁面不會自動切換模型 |
| HTTP 400 | 通常提示請求或服務設定不符；結構化錯誤明確指出 Key 問題時改為認證提示 |
| HTTP 401／403 | 檢查 Key、權限與 API 存取限制；不能僅由此保證問題是 Key 格式 |
| HTTP 404 | 檢查 `/ApiKeyTest` 的 `Gemini:Model`，或 `/ApiKeyChat` 目前選定模型及其可用性；不會自動切換模型 |
| HTTP 429 | 頻率或配額限制，稍後再試或檢查配額；程式不自動重試 |
| HTTP 5xx | Gemini 服務暫時異常，可稍後再試 |
| DNS、連線或 TLS 失敗 | 檢查網路及連線環境 |
| 60 秒逾時 | 稍後重新送出；不要把瀏覽器中止與逾時混為一談 |
| 安全阻擋 | 調整輸入；遭阻擋內容不會顯示 |
| 無可用文字 | 可能沒有候選、只有思考／非文字片段或只有空白 |
| JSON 格式或結束原因異常 | 顯示安全摘要，不輸出原始回應；可稍後再試 |
| 回覆達長度限制 | 有文字時保留並顯示警告，沒有文字時提示無文字及長度限制 |
| 對話上下文超限 | 開始新對話；原有歷史與本次輸入不會因失敗而加入新回合 |

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

驗證涵蓋 `/ApiKeyTest` 的 Key 檔格式與重新讀檔、輸入邊界、Gemini 錯誤與回應解析，也涵蓋 `/ApiKeyChat` 的模型允許清單、Key 與歷史隔離、模型切換、回合上限、過期、重送去重、併發控制、防偽權杖及純文字呈現。個別案例輸出 `PASS`／`FAIL`；全部成功回傳退出碼 0，任一失敗回傳 1，不列印例外訊息或秘密值。

### 模擬伺服器

```sh
dotnet run --project Verification/Verification.csproj -- --serve
```

開啟 `http://127.0.0.1:5288/ApiKeyTest`。此模擬伺服器只提供 `/ApiKeyTest` 的單次測試情境；聊天頁的模型與工作階段案例由上述驗證程式使用假 HTTP handler 檢查。模擬伺服器只存在於獨立驗證程式，正式應用程式沒有模擬端點或切換開關。

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

[browser-checks.js](Verification/browser-checks.js) 驗證 `/ApiKeyTest` 的空白、emoji、原生換行邊界、處理中狀態、重複提交、標記不執行、回覆首行換行、失敗清空、截斷、手機版排列、本機網路請求及憑證保護；此腳本不操作 `/ApiKeyChat`。

成功時腳本回傳 `passed: true`、`consoleErrors: 0`、`pageErrors: 0`，並保存合成資料截圖：

- `/private/tmp/gemini-api-key-test-mobile.png`
- `/private/tmp/gemini-api-key-test-desktop.png`

此腳本用於模擬環境，不使用真實 Key。2026-10-13 文件更新未重新執行瀏覽器回歸或產生截圖。

## 驗證紀錄與限制

### 本次文件更新：2026-10-14

本次只修改 `README.md`，依 spec2、spec3 與目前頁面／服務實作核對兩頁的 Key、歷史及模型行為。

| 命令或檢查 | 結果 |
| --- | --- |
| `dotnet build TestAPIKeyCalling.csproj --no-restore` | 建置成功，0 警告、0 錯誤 |
| Markdown 本機連結、目錄錨點及程式碼圍欄 | 通過；目錄錨點及本機連結均解析成功 |
| README 秘密格式、UTF-8、CRLF 與結尾換行檢查 | 通過；未發現 API Key 格式 |
| `git -c core.whitespace=cr-at-eol diff --check` | 通過 |
| Git 變更範圍 | 僅 `README.md` |

本次未重新執行獨立驗證程式、瀏覽器回歸或 Gemini 遠端呼叫。

### 文件更新紀錄：2026-10-13

當日僅擴充 `README.md`，重新核對最新程式碼與文件命令。實際執行結果如下：

| 命令或檢查 | 結果 |
| --- | --- |
| `dotnet build TestAPIKeyCalling.csproj --no-restore` | 建置成功，0 警告、0 錯誤 |
| `dotnet run --project Verification/Verification.csproj --no-build` | 87／87 檢查通過，退出碼 0 |
| `git check-ignore -v gemini-api-key.txt` | 根目錄 Key 忽略規則生效 |
| `git ls-files -- gemini-api-key.txt` | 無輸出，Key 未被追蹤 |
| `git -c core.whitespace=cr-at-eol diff --check` | 通過 |
| Markdown 本機連結、目錄錨點、程式碼圍欄及行尾空白 | 通過 |
| Git 變更範圍 | 僅 `README.md` |

2026-10-13 驗證摘要：

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

該次文件更新未重新執行上述瀏覽器、發佈、正式 profile 啟動或 VS Code F5 操作。真實 Key 的遠端認證、Gemini 成功回覆及目標模型可用性仍未驗證。
