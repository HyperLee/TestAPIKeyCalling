# Gemini API Key 對話頁面開發規格書

## 1. 文件目的與目前狀態

本文件描述在現有 .NET 10 ASP.NET Core Razor Pages 專案中新增第二個功能頁面，讓使用者在網頁輸入 Gemini API Key 後進行多輪文字對話，作為後續實作與驗收依據。

目前的 `/ApiKeyTest` 是既有單次測試功能：它從專案根目錄的本機 Key 檔讀取 Key，每次只送出一筆文字。本規格新增獨立的 `/ApiKeyChat` 頁面與獨立 Key／對話工作階段。`/ApiKeyTest` 頁面及 `SpecFile/spec1.md` 的既有功能與規格維持原樣；spec1 中「不包含網頁輸入 Key、聊天歷史」等範圍限制只描述第一個功能，不限制本文件所述的第二個功能。

本文件本次只新增規格，不修改程式、設定、Key 檔案或既有規格，不建立或使用真實 API Key，也不呼叫 Gemini。文件中的程式介面與驗收要求均為後續開發要求，尚未實作或驗證。

## 2. 需求來源與決策

### 2.1 需求摘要

1. 新增一個獨立功能頁面，使用者可先在頁面輸入 API Key，再開始對話。
2. Key 與對話歷史只在伺服器記憶體短期保存，不寫入檔案、資料庫、瀏覽器儲存或一般 ASP.NET Core Session。
3. 對話須帶入本次對話的前文；新訊息與既有成功問答一起送至 Gemini。
4. 保留既有 `/ApiKeyTest` 檔案讀 Key、單次呼叫的流程與頁面。

### 2.2 已確認決策

| 項目 | 決策 |
| --- | --- |
| API 服務 | 沿用 Gemini Developer API 與現有 Gemini 模型設定 |
| 新頁面 | 獨立 Razor Page，路由 `/ApiKeyChat`，不取代或擴充 `/ApiKeyTest` 頁面 |
| Key 暫存 | 使用伺服器端專用記憶體暫存；瀏覽器 cookie 只保存隨機索引，不保存 Key |
| Key 驗證時機 | 按「開始對話」只做本機格式驗證及暫存；第一則訊息才呼叫 Gemini 驗證遠端 Key |
| 對話脈絡 | 同一對話最多保留 20 組成功或有文字的截斷問答；保留的回合均作為後續請求的脈絡 |
| 新對話 | 清除對話歷史並保留目前 Key |
| 清除 Key | 清除 Key 與對話歷史，刪除工作階段索引 cookie，返回 Key 輸入畫面 |
| 工作階段 | 閒置 30 分鐘後到期；應用程式重新啟動後，記憶體資料不再存在 |
| 本次交付 | 僅新增本規格文件，不實作功能 |

### 2.3 開發預設值

| 項目 | 預設值 |
| --- | --- |
| 導覽文字 | `API Key 對話頁面` |
| 頁面路由 | `/ApiKeyChat` |
| 模型 | 沿用 `Gemini:Model`；未設定時沿用現有預設 `gemini-2.5-flash` |
| 對話輸入上限 | 每則最多 4,000 個 .NET `string.Length` 單位（UTF-16 code unit） |
| 單一對話上限 | 最多 20 組已完成的使用者訊息／Gemini 回覆；達上限後保留既有歷史並提示開始新對話 |
| HTTP 逾時 | 60 秒，沿用既有 Gemini 用戶端設定 |
| Gemini 傳輸 | 非串流 `generateContent`；不自動重試 |
| Key／歷史存放 | 單一應用程式程序的記憶體快取，閒置期限 30 分鐘 |
| 對話內容呈現 | 純文字；保留換行，不解析 HTML 或 Markdown |

模型預設值只是沿用目前專案設定，不保證模型名稱或 Key 權限永久可用。實作前須再次確認官方介面及模型可用性。

## 3. 功能範圍

### 3.1 包含

- 共用頂部導覽列新增「API Key 對話頁面」，導向 `/ApiKeyChat`。
- 在新頁面輸入一組 Gemini API Key，並啟動暫存工作階段。
- 同一工作階段內進行多輪文字對話，顯示使用者訊息、Gemini 回覆、處理狀態及安全錯誤摘要。
- 提供「新對話」與「清除 Key」操作。
- 對 Key、對話內容、工作階段到期、輸入邊界、錯誤處理及防偽驗證進行測試。

### 3.2 不包含

- 修改 `/ApiKeyTest` 的畫面、路由、檔案讀 Key 方式、服務呼叫行為或單次測試語意。
- 登入、使用者帳號、多組 Key 管理、Key 永久儲存、資料庫、對話匯出或跨伺服器分散式工作階段。
- 模型選單、串流回覆、圖片／音訊／檔案、工具呼叫、系統提示設定或用量統計。
- 將應用程式改成公開或多人共用服務。若日後要從本機以外存取，須另行規劃身分驗證、授權、傳輸安全與用量控管。

## 4. 頁面與使用流程

### 4.1 導覽與版面

- 新增獨立頁面標題「API Key 對話頁面」，沿用專案共用版面與 Bootstrap，不加入新的 UI 套件。
- 共用導覽列保留原有項目，另外加入新頁面連結；原「API Key 測試頁面」仍導向 `/ApiKeyTest`。
- 初始畫面提供密碼型 Key 欄位、說明文字及「開始對話」按鈕。Key 欄位使用 `type="password"`，不以純文字回填。
- 工作階段建立後顯示對話紀錄、可編輯多行輸入框、「送出」按鈕、「新對話」按鈕與「清除 Key」按鈕。
- 對話區分使用者與 Gemini，保留訊息換行；以 Razor 編碼及 DOM `textContent` 顯示，不使用 `innerHTML`，不將 Markdown 或 HTML 轉成可執行標記。
- 使用 `role="status"`／`aria-live` 呈現驗證、處理中、成功、截斷、錯誤及工作階段失效狀態。窄螢幕保持可讀及垂直排列。
- 頁面回應設定為不可快取；重新整理有效頁面時，由伺服器工作階段重新顯示歷史，但不回傳 Key。

### 4.2 操作狀態

| 狀態 | 畫面與行為 |
| --- | --- |
| 沒有有效工作階段 | 顯示空白 Key 欄位；不呼叫 Gemini |
| Key 格式錯誤 | 不建立工作階段、不呼叫 Gemini；顯示一般修正提示，不回顯 Key |
| 開始對話成功 | 伺服器建立暫存工作階段並設定索引 cookie；清除 Key 欄位並顯示空白對話畫面；不呼叫 Gemini |
| 沒有對話訊息 | 顯示輸入框及操作按鈕，等待第一則訊息 |
| 處理中 | 清除輸入框前先保留待送文字；停用送出與重置按鈕、顯示「處理中…」；同一工作階段不接受併發訊息 |
| Gemini 成功 | 顯示使用者訊息與回覆，將成功問答加入歷史，清空輸入框並恢復按鈕 |
| Gemini 回覆截斷 | 顯示已取得的文字及不完整提示；將這筆文字回覆納入歷史，恢復按鈕 |
| Gemini 失敗 | 顯示分類後的錯誤；不把失敗回合加入歷史，保留原輸入以供修改或重試 |
| 對話已達上限 | 保留 Key 與全部歷史、停止接受新訊息，提示使用者開始新對話 |
| 工作階段失效 | 不呼叫 Gemini；清除畫面中的歷史並返回 Key 輸入狀態，提示重新輸入 Key |

每次新的送出由瀏覽器產生唯一 GUID `requestId`，並將它與該則訊息綁定。同一工作階段內的伺服器端處理須防止併發請求造成歷史遺失或回合順序錯亂；前端停用按鈕作為第一層防護，伺服器須序列化或拒絕同一工作階段的重複進行中請求。伺服器成功提交回合時，同時保存該 `requestId`、原訊息及結果；最多 20 組成功回合，因此去重結果也有固定上限。相同 ID 與相同訊息重送時回傳已保存結果而不再次呼叫 Gemini；相同 ID 搭配不同訊息則拒絕。對使用者已看見的明確錯誤進行重試時，前端須產生新的 ID。若上游已處理請求但應用程式在取得可保存的回覆前逾時或中斷，無法保證遠端呼叫恰好一次；此時不可自動重送，使用者明確重試才發出新請求。

### 4.3 Key 輸入驗證

- Key 欄位不得為空白；前後空白可去除，內部空白、換行、控制字元或非可列印 ASCII 字元須拒絕。
- 不以固定前綴或固定長度判斷 Key 真偽；實際有效性由第一則 Gemini 請求驗證。
- Key 格式錯誤時不寫入暫存、不送出遠端請求、不把欄位值帶回錯誤頁面。
- Key 送出採同源 Razor Pages POST，使用 ASP.NET Core 防偽權杖；Key 不得放在 URL、查詢字串、隱藏欄位或前端儲存。

### 4.4 對話輸入驗證

- 不接受空字串或只有空白／換行的訊息，提示「請輸入要傳送的文字」。
- 每則訊息最多 4,000 個 UTF-16 code unit；前端可先提示，後端須重新驗證。超長訊息拒絕且不得默默截斷。
- 長度依提交到伺服器的文字計算，包含換行所佔 UTF-16 單位；Unicode 與 emoji 依 .NET `string.Length` 規則計算。
- 驗證失敗不呼叫 Gemini、不修改既有對話歷史，保留使用者可編輯的輸入。

## 5. Key 與對話工作階段

### 5.1 暫存設計

- 新增專用 `IChatSessionStore`，以單一應用程式程序內的記憶體快取保存工作階段資料；每筆資料包含一組 Key、已完成的對話訊息及必要的併發控制狀態。
- 不使用一般 ASP.NET Core Session 直接保存 Key。Microsoft 文件提醒 Session 可能跨瀏覽器視窗延續且不一定對應單一使用者，因此此功能使用專用、可主動清除及設定期限的記憶體暫存。[ASP.NET Core app state](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0)
- 開始工作階段時使用密碼學安全亂數產生至少 256 bit 的不可預測索引。以 Base64Url 等安全編碼後放入 cookie；cookie 僅含索引，不含 Key 或對話內容。
- Cookie 設定 `HttpOnly`、`SameSite=Strict`、`Path=/ApiKeyChat`，不設定 `Domain` 或持久期限；使用 HTTPS 時加上 `Secure`。刪除工作階段時以相同 Path 刪除 cookie。
- 記憶體資料使用 30 分鐘滑動閒置期限。有效的頁面讀取與對話操作更新期限；過期、索引不存在或程序重新啟動時一律視為無工作階段。
- 工作階段索引不得出現在網址、頁面文字、一般日誌或 Gemini 請求本文。只有同源 cookie 可攜帶索引至 `/ApiKeyChat`。
- 重新輸入 Key 前先清除舊工作階段，再建立新的索引；每一瀏覽器工作階段各自隔離 Key 與對話歷史。相同瀏覽器 cookie 的分頁共用同一對話。

### 5.2 工作階段操作

- 「開始對話」只做 Key 格式驗證、建立記憶體項目、設定索引 cookie，之後顯示聊天畫面；不呼叫 Gemini，也不作網路連線探測。
- 「新對話」只清空伺服器端訊息歷史並保留 Key；清空後的下一次 Gemini 請求只含新對話的第一則訊息。
- 單一對話最多接受 20 組成功或有文字的截斷回合。達上限後不再接受訊息，保留 Key 及完整歷史，提示使用者選擇「新對話」；失敗回合不計入上限。
- 「新對話」也清除與舊歷史相連的已完成 `requestId` 去重結果，避免舊回合結果被帶入新對話。
- 「清除 Key」移除該索引對應的 Key 與全部歷史，刪除索引 cookie，並返回 Key 欄位。
- 遠端認證失敗時保留暫存直到使用者清除或工作階段到期；頁面須提供清除並重新輸入的操作，不顯示或回填原 Key。
- 若 cookie 尚在但伺服器快取已過期，視同工作階段失效；不嘗試恢復 Key 或歷史。

## 6. 多輪對話與 Gemini 介面

### 6.1 請求流程

```text
GET /ApiKeyChat
  → 檢查同源索引 cookie 與記憶體工作階段
  → 有效時顯示歷史與輸入框；無效時顯示 Key 欄位

POST /ApiKeyChat?handler=Begin
  → 防偽驗證、Key 格式驗證
  → 建立記憶體工作階段及隨機索引 cookie
  → 回傳／導向空白聊天畫面，不呼叫 Gemini

POST /ApiKeyChat?handler=Send
  → 防偽驗證、工作階段查找、requestId 與訊息驗證
  → 以伺服器既有歷史加上新訊息呼叫 Gemini
  → 成功時才將使用者訊息與 Gemini 回覆一併寫入歷史
  → 回傳本次結果與狀態，不回傳 Key 或完整歷史
```

清除 Key 與新對話也使用同源、帶防偽驗證的 POST handler。聊天送出可由頁面 JavaScript 以 `fetch` 呼叫 Razor Pages handler；這不是供外部使用的獨立公開 API。瀏覽器的聊天 POST 只帶本次訊息、`requestId` 與防偽資料，不帶 Key、不帶既有完整歷史。

### 6.2 Gemini 請求

- 使用現有 `Gemini:Model`，呼叫 Gemini Developer API `generateContent`。官方文件說明，多輪請求的 `contents[]` 需包含對話歷史與最新請求；REST 呼叫由應用程式管理歷史，`role` 依序為 `user` 與 `model`。[Gemini generateContent](https://ai.google.dev/api/generate-content)
- 後端以 JSON 序列化建立請求，使用 `x-goog-api-key` 標頭認證，不將 Key 放在 URL 或查詢字串。
- 第一則訊息的 `contents` 只含該則 `user` 文字。後續每一則訊息使用目前工作階段所有已成功的 `user`／`model` 訊息，再附上當前 `user` 訊息。
- 每次只有一般文字內容，不傳送圖片、音訊、檔案、工具、額外系統指令或自訂生成選項。使用現有非串流逾時設定 60 秒，不自動重試。
- 單一對話最多 20 組成功問答；達上限後保留完整歷史並要求使用者開始新對話。歷史不自動裁切或摘要。若 Gemini 因上下文長度拒絕請求，顯示「對話內容已超出模型可處理範圍，請開始新對話」或等義訊息；保留既有成功歷史及本次輸入。

### 6.3 回合提交規則

- 先以「既有歷史 + 本次使用者訊息」呼叫 Gemini；遠端回覆有可顯示文字且為成功或截斷結果時，才原子地將使用者訊息與 Gemini 回覆加至伺服器歷史。
- 截斷結果若包含文字，將文字作為 `model` 回合保存，並顯示不完整提示；沒有一般文字、內容遭安全阻擋、認證失敗、網路錯誤、逾時或無效回應時，不新增本次回合。
- 每筆新訊息由前端產生 GUID `requestId`，將 ID 與訊息一併送出；使用者不可修改 ID 或將同一 ID 用於不同訊息。
- 伺服器對每個成功提交的 `requestId` 保存原訊息與結果。同一 ID／同一訊息的重送回傳已保存結果而不再呼叫 Gemini；同一 ID／不同訊息回傳驗證錯誤。回合提交與去重結果保存須原子完成。
- 同工作階段同時只能處理一筆新請求；重複的進行中請求不得再呼叫 Gemini。對明確回傳的失敗進行人工重試時使用新 `requestId`，保留輸入但不新增歷史。對上游已處理、應用程式卻未取得可保存回覆的逾時／中斷，不保證遠端呼叫恰好一次，也不得由應用程式自動重送。
- 使用者中止或瀏覽器斷線時將取消權杖傳入服務；將用戶端中止與 60 秒服務逾時分開處理。若瀏覽器未收到明確結果，保留本次 `requestId` 與文字供使用者手動重送；若伺服器已提交回合，重送會取得已保存結果。收到明確失敗結果後的重試才使用新的 `requestId`。

## 7. 後端元件與介面

| 元件／介面 | 責任 |
| --- | --- |
| `ApiKeyChatModel` | 新頁面 GET／POST handlers、驗證、工作階段狀態、回合提交及狀態訊息；禁止快取回應 |
| `IChatSessionStore` | 建立、讀取、更新歷史、清除歷史及刪除 Key 工作階段；提供 30 分鐘閒置期限、最多 20 組回合及同工作階段併發／請求去重保護 |
| `ChatSessionState` | 僅在伺服器記憶體保存 Key、最多 20 組已完成對話訊息、成功 `requestId` 的原訊息與回應，以及併發狀態 |
| `GeminiChatMessage` | 表示一筆 `user` 或 `model` 角色的純文字訊息 |
| `IGeminiChatService` | 接受伺服器端 Key、包含當前使用者訊息的多輪訊息清單與取消權杖，回傳既有 `GeminiResult` 類型結果 |
| 共用 Gemini API client/helper | 集中 HTTP 傳輸、回應解析及錯誤分類，供現有單次服務與新聊天服務共用 |
| `api-key-chat.js` | Key 表單／聊天操作、前端輸入驗證、處理中狀態、防止重複提交及以純文字更新訊息 |

既有 `IGeminiService.GenerateAsync(input, cancellationToken)` 與 `IApiKeyReader` 介面及其檔案讀取職責維持不變。新 `IGeminiChatService` 取得 Key 的唯一來源是有效伺服器工作階段；不得呼叫 `FileApiKeyReader` 代替頁面輸入值。共用底層 HTTP／解析程式碼時，須維持既有 `/ApiKeyTest` 的錯誤分類、逾時、取消與結果語意。

新頁面不建立 ASP.NET Core Controller 或獨立公開 API；使用 Razor Pages 路由與 handlers。Key 與歷史只能由後端工作階段取得，不能由 model binding 接受前端提交的 Key 或歷史清單。

## 8. 錯誤處理與安全要求

### 8.1 錯誤分類

| 情境 | 提示與處理 |
| --- | --- |
| Key 欄位空白或格式不安全 | 提示檢查 Key；不建工作階段、不呼叫 Gemini、不顯示原值 |
| 工作階段不存在或到期 | 提示重新輸入 Key；不呼叫 Gemini |
| HTTP 400 | 顯示請求或內容不符合服務要求；能辨識上下文超限時提示另開新對話 |
| HTTP 401／403 | 提示認證失敗或權限不足，提供清除 Key 後重輸操作 |
| HTTP 404 | 提示模型或 API 資源不存在，檢查現有 `Gemini:Model` 設定 |
| HTTP 429 | 提示頻率或配額限制，可稍後重試 |
| HTTP 5xx | 提示 Gemini 服務暫時異常 |
| DNS、連線或 TLS 失敗 | 提示無法連線至 Gemini，檢查網路 |
| 超過 60 秒 | 顯示逾時提示，可再次送出 |
| 安全阻擋、無一般文字或異常 JSON | 不顯示遭阻擋／思考內容或原始回應本文，顯示安全摘要 |
| 其他例外 | 顯示一般失敗提示，不洩漏內部細節 |

錯誤訊息與對話本文分開顯示。失敗回合不寫入歷史，不以原始上游錯誤本文直接輸出至頁面。

### 8.2 Key、對話與瀏覽器安全

- Key 會由使用者瀏覽器以 HTTPS 或本機 loopback 同源 POST body 傳到應用程式一次；初次表單請求必然包含使用者輸入值。完成後不得將 Key 回傳至 HTML、JSON、JavaScript、隱藏欄位、cookie 值、網址或後續聊天請求。
- Key 只在伺服器程序記憶體中暫存，僅在發往 Gemini 時置於 `x-goog-api-key` 標頭；不寫入檔案、資料庫、分散式快取、一般 ASP.NET Core Session 或瀏覽器 local/session storage。
- 不記錄 Key、Key 表單本文、使用者訊息、Gemini 回覆、原始錯誤本文或例外堆疊。診斷日誌最多記錄錯誤類別、HTTP 狀態與耗時；HTTP 標頭記錄必須遮蔽認證標頭。
- 只允許本機自用的部署範圍；不得新增對外綁定位址。若將來需要非本機存取，須另訂 HTTPS、登入、授權及濫用防護要求。
- 所有變更 Key、送出訊息、清除歷史或清除工作階段的 POST handler 均驗證防偽權杖。Cookie 設 `HttpOnly`、`SameSite=Strict`，HTTPS 請求加上 `Secure`。
- 頁面及 AJAX 回應設定禁止快取。DOM 更新以 `textContent` 或等效安全文字 API 呈現；測試包含 `</textarea>`、`<script>` 等文字，確認不執行 HTML。
- 提示使用者送出的訊息及必要對話歷史會傳送至 Gemini；不在畫面或日誌顯示 Key。

## 9. 驗收案例

以下為後續功能開發的驗收條件，尚未執行。所有測試使用合成 Key 與假 HTTP handler；不得將真實 Key 放入測試輸出、截圖或版本控制。

| 編號 | 情境／操作 | 預期結果 |
| --- | --- | --- |
| AC-01 | 首頁及共用導覽 | 新連結導向 `/ApiKeyChat`；既有測試頁連結仍導向 `/ApiKeyTest` |
| AC-02 | 開啟新頁且沒有有效工作階段 | 顯示空白密碼欄位；不呼叫 Gemini；回應不可快取 |
| AC-03 | 輸入格式正確的合成 Key 並開始 | 建立伺服器暫存並設定隨機索引 cookie；不呼叫 Gemini；回應 HTML、cookie 值與日誌不含 Key |
| AC-04 | 空 Key、內部空白、換行、控制字元或非 ASCII Key | 顯示安全格式提示；不建立工作階段、不呼叫 Gemini、不回顯 Key |
| AC-05 | Key 欄位已送出後讀取頁面 HTML／JSON | Key 不出現在頁面、回應本文、隱藏欄位、網址或 cookie；密碼欄位不回填 |
| AC-06 | 開始後尚未送出訊息 | 不發生 Gemini 請求，也不做額外連線測試 |
| AC-07 | 送出第一則合法文字 | 瀏覽器 POST 只含本次文字、唯一 `requestId` 與防偽資料；伺服器呼叫一次 Gemini，Key 只在認證標頭，請求本文包含一個 `user` 回合 |
| AC-08 | 成功後送出第二則訊息 | Gemini `contents` 按序包含第一個 `user`、第一個 `model` 及第二個 `user` 回合；UI 顯示完整歷史 |
| AC-09 | 送出空白、恰為 4,000 與超過 4,000 UTF-16 單位，含 emoji／換行 | 空白及超長由前後端拒絕且不呼叫 Gemini；邊界長度接受；輸入不默默截斷 |
| AC-10 | Gemini 回覆含多段文字或換行 | 一般文字依序合併並保留換行，以純文字顯示 |
| AC-11 | Gemini 回傳有文字的 `MAX_TOKENS` | 顯示文字及不完整提示，並將該成功文字回合加入對話歷史 |
| AC-12 | 認證失敗、429、404、5xx、網路錯誤、逾時、安全阻擋或異常 JSON | 顯示分類摘要；不回傳原始本文或內部例外；不將失敗回合加入歷史，保留輸入供重試 |
| AC-13 | Key 遠端認證失敗後清除並重新輸入 | 舊 Key 與歷史移除；新 Key 不需重新啟動應用程式即可使用 |
| AC-14 | 按「新對話」 | 歷史清空、Key 保留；下一個 Gemini payload 只含新訊息 |
| AC-15 | 按「清除 Key」 | 快取中的 Key 與歷史移除、cookie 刪除、頁面返回 Key 輸入狀態；不呼叫 Gemini |
| AC-16 | 使用兩個不同工作階段索引 | Key 與歷史互相隔離；相同 cookie 的分頁共用其工作階段 |
| AC-17 | 重新整理有效工作階段頁面 | 伺服器重新顯示歷史但不回傳 Key，使用者可繼續對話 |
| AC-18 | 工作階段閒置超過 30 分鐘或應用程式重新啟動 | Key 與歷史無法恢復；頁面要求重新輸入 Key；不發出 Gemini 請求 |
| AC-19 | Gemini 回報上下文超限 | 顯示另開新對話提示；不自動裁切舊歷史，保留既有成功歷史及本次輸入 |
| AC-20 | 完成 20 組成功或有文字的截斷回合後再送出 | 保留 Key 與 20 組完整歷史；拒絕新訊息並提示開始新對話；失敗回合不計入上限 |
| AC-21 | 已成功回合的回應未抵達瀏覽器，以相同 `requestId` 與訊息重送 | 回傳已保存結果，不再次呼叫 Gemini，也不新增重複回合；相同 ID 改搭不同訊息時拒絕 |
| AC-22 | 同一工作階段快速重複或跨分頁併發送出 | 一次只處理一筆；不重複呼叫或遺失／錯置歷史回合 |
| AC-23 | POST 缺少或帶無效防偽權杖 | Razor Pages 拒絕請求；不修改工作階段、不呼叫 Gemini |
| AC-24 | Gemini 回覆含 HTML／script 字串 | 當純文字顯示；不建立可執行 DOM，不產生瀏覽器執行錯誤 |
| AC-25 | 搜尋回應、cookie、瀏覽器後續聊天 POST、URL、前端資產及日誌中的合成 Key | 除初次送往本機伺服器的 Key 表單 body 及後端送往 Gemini 的認證標頭外，其餘位置均不含 Key |
| AC-26 | 回歸既有 `/ApiKeyTest` | 原檔案讀 Key、單次輸入、錯誤分類及頁面操作仍通過原有驗證 |

## 10. 後續開發驗證方式

- 執行 `dotnet restore TestAPIKeyCalling.csproj`，再執行 `dotnet build TestAPIKeyCalling.csproj --no-restore`。
- 執行 `dotnet run --project Verification/Verification.csproj`，擴充現有自訂驗證主控台程式，使用假的工作階段 store、合成 Key 與假 HTTP handler 驗證 payload、錯誤、隔離、清除、逾時、20 組回合上限、相同 `requestId` 重送及 Key 不外洩；不得以真實配額測試錯誤分類。
- 執行 `dotnet format TestAPIKeyCalling.csproj --verify-no-changes --no-restore`。
- 啟動本機應用程式，以瀏覽器驗證導覽、Key 輸入與清除、多輪脈絡、重整恢復、處理中／重複送出、錯誤後重試、窄螢幕版面及瀏覽器主控台。
- 檢查初始 Key POST、後續聊天 POST、Gemini 請求標頭與本文、頁面回應、cookie、前端資產及診斷日誌。除初始本機 Key 表單本文及伺服器向 Gemini 傳送的認證標頭外，不得出現 Key。
- 真實 API 冒煙測試僅在使用者本機自行提供 Key 並明確操作時進行；紀錄命令與結果時不可保存或顯示 Key、訊息或私人回覆。

## 11. 本次規格文件檢查

- 確認本文件只描述新增 `/ApiKeyChat`；未把新流程寫成替代 `/ApiKeyTest`，也未修改第一功能規格。
- 確認決策、預設值、Key 生命週期、資料流、服務介面、錯誤處理與驗收案例互相一致，無待填欄位、真實 Key 或互相矛盾的要求。
- 確認本次工作樹只有新增 `SpecFile/spec2.md`；檢查新增文件本身的尾端空白與 Markdown fence，並執行 `git diff --check`／等效的新增檔案 whitespace 檢查。
- 本次不執行建置或 Gemini 呼叫；規格文件完成不代表功能已實作或通過後續驗收。

## 12. 官方參考資料

- [Gemini generateContent API：請求、對話歷史與回應格式](https://ai.google.dev/api/generate-content)
- [Gemini API Key：伺服器端認證方式](https://ai.google.dev/gemini-api/docs/api-key)
- [ASP.NET Core app state：Session 與工作階段限制](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0)
- [Gemini API 疑難排解](https://ai.google.dev/gemini-api/docs/troubleshooting)

後續開發時再次確認 Gemini 官方文件及可用模型，避免將 API 規格、配額或模型可用性視為永久保證。
