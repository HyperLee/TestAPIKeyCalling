# Gemini API Key 對話頁面模型選擇擴充開發規格書

## 1. 文件目的與適用範圍

本文件新增已實作 `/ApiKeyChat` 的模型選擇功能規格，與 `SpecFile/spec2.md` 配套使用，不取代或修改原規格。`spec2.md` 的聊天、Key、工作階段、安全、錯誤處理與回合提交要求繼續適用；其中「不包含模型選單」僅就本文件所述的 `/ApiKeyChat` 模型選擇功能由本文件補充。其他未明列的行為仍以 `spec2.md` 為準。

本次變更同步更新 `/ApiKeyChat` 頁首與 `gemini-3.8-flash` 選項提示，以及本規格中的相關規定；不改變模型清單、預設模型、API 呼叫流程或其他頁面行為。不建立或使用真實 API Key、不呼叫 Gemini，也不將任何秘密資料提交、推送或上傳至 GitHub 或其他公開位置。

## 2. 需求與已確認決策

| 項目 | 決策 |
| --- | --- |
| 功能頁面 | 僅 `/ApiKeyChat` 提供模型選擇；`/ApiKeyTest` 維持原行為 |
| 模型清單 | `gemini-3.8-flash`、`gemini-3.5-flash-lite`、`gemini-2.5-flash` |
| 初始模型 | 新工作階段預選 `gemini-3.8-flash` |
| 模型標示 | 3.8 Flash 標示為建議選項並提醒確認 API Key 專案的付費狀態與模型權限；3.5 Flash-Lite 標示為較省成本選項；2.5 Flash 提示存取可能受限 |
| 工作階段 | 模型 ID 與 Key、歷史一樣只保存在伺服器記憶體工作階段；cookie 仍只存隨機工作階段索引 |
| 切換模型 | 保留 Key，清除對話歷史、已完成請求去重資料與截斷狀態；不呼叫 Gemini |
| 新對話 | 清除歷史並沿用目前選定的模型與 Key |
| 聊天內容 | 純文字 `generateContent`；不新增圖片、影音、檔案、工具或推理等級選項 |
| 模型不可用 | 顯示錯誤，不自動改用其他模型 |

截至 2026-10-14，Google 官方模型清單列出 `gemini-3.8-flash` 與 `gemini-3.5-flash-lite` 為穩定模型，並提醒 2.5 系列存取可能受限。官方價格表將 3.8 Flash 標準推論的 Free Tier 輸入與輸出列為免費；但免費層只開放部分模型，實際存取與配額依 API Key 所屬專案的計費層級及權限而異。因此，本頁只提醒確認專案狀態，不宣稱所有免費方案都無法使用此模型。模型狀態會變動，開始實作前仍須重新核對官方資料。([Gemini API 模型清單](https://ai.google.dev/gemini-api/docs/models?hl=en)、[官方價格表](https://ai.google.dev/gemini-api/docs/pricing)、[Billing 說明](https://ai.google.dev/gemini-api/docs/billing))

## 3. 頁面流程與選擇行為

### 3.1 尚未建立工作階段

- Key 輸入表單新增模型下拉選單，預選 `gemini-3.8-flash`，列出本文件定義的三個模型及其用途提示；3.8 Flash 提示須提醒確認 API Key 專案的付費狀態與模型權限。
- 「開始對話」的同源 POST 可包含 Key、模型 ID 與防偽資料。後端先驗證模型 ID 及 Key 格式，再建立工作階段；不得為了驗證 Key 或模型而呼叫 Gemini。
- 缺少模型欄位時採用 `gemini-3.8-flash`。模型 ID 不在允許清單時拒絕請求，不建立或替換工作階段，不清除既有 Key／歷史，也不呼叫 Gemini。

### 3.2 已建立工作階段

- 聊天畫面顯示目前模型及模型選單，並提供明確的「切換模型」操作；介面須事先提示切換會清空目前對話。初始表單與聊天畫面的模型選單沿用相同提示，提醒確認 API Key 所屬專案的付費狀態與模型權限，不宣稱該模型不可用。
- 切換請求只提交模型 ID 與防偽資料，不提交 Key 或聊天歷史。後端重新驗證允許清單後，原子地更新模型並清除訊息、成功請求去重紀錄及截斷標記；保留 Key 和原工作階段 cookie，不呼叫 Gemini。
- 若選取的模型與目前模型相同，視為不變更，保留歷史及去重紀錄。
- 「新對話」只清空歷史與去重紀錄，保留 Key 並沿用目前模型。
- 處理訊息期間，前端停用模型切換；後端亦須序列化或拒絕併發切換，避免一筆訊息以不同於畫面所示的模型送出。拒絕時不得改動模型或歷史，也不得多送 Gemini 請求。
- 有效模型選擇只儲存在伺服器工作階段；不得放入 URL、cookie、瀏覽器 local/session storage 或可由用戶端直接決定的 Gemini 請求路徑。

## 4. 後端資料與 Gemini 請求

- 伺服器以明確允許清單接受三個模型識別字；不信任前端值，不接受任意模型名稱、URL 或路徑片段。
- 工作階段狀態須保存目前模型。建立工作階段時設定選定模型；切換模型須與清除歷史及去重資料一併完成。
- 聊天服務須依伺服器工作階段中的模型建立 `generateContent` URL。聊天頁可以將模型 ID 傳給聊天服務／共用 API client；既有 `/ApiKeyTest` 仍使用 `Gemini:Model` 設定及原有單次呼叫語意。
- Gemini 請求沿用現有 JSON 序列化及多輪 `contents` 格式，只傳送 `user`／`model` 純文字訊息。不得加入圖片、音訊、影片、檔案、工具、系統提示或額外生成設定；保留模型預設行為。3.8 Flash 的官方 `generateContent` 範例支援純文字輸入。([Gemini 3.8 Flash API 指引](https://ai.google.dev/gemini-api/docs/generate-content/whats-new-gemini-3.8))
- 不可用、Key 無權限、配額不足或其他上游錯誤時依 `spec2.md` 分類顯示安全摘要；不得自動切換模型、重試或顯示原始錯誤本文。若送出失敗，保留本次輸入及目前模型。

### 4.1 介面責任調整

| 元件／介面 | 擴充責任 |
| --- | --- |
| `ApiKeyChatModel` | 綁定初始模型選項；提供模型切換 handler；驗證模型值及回應目前模型 |
| `IChatSessionStore`／`ChatSessionState` | 建立時保存模型；以序列化操作切換模型並清除該模型舊對話及去重狀態 |
| `ChatSessionSnapshot` | 提供目前模型 ID 給聊天頁呈現 |
| `IGeminiChatService` | 接收已驗證的模型 ID、伺服器端 Key、純文字訊息與取消權杖 |
| 共用 Gemini API client | 支援聊天呼叫指定模型，同時維持 `/ApiKeyTest` 原本使用設定模型的行為 |

介面名稱用來描述責任；實作時沿用專案既有 Razor Pages、服務及工作階段模式，不新增公開 Controller 或獨立外部 API。

## 5. API Key 與公開資訊安全要求

以下要求適用本功能的規格、實作、測試及交付；API Key 是機密，任何真實 Key 都不得洩漏或公開：

- 本文件不得包含真實 Key、可用 Key、Key 片段或可還原的 Key；程式、設定、測試資料、範例、日誌、錯誤訊息、截圖、文件、終端輸出及 Git 差異均不得包含真實 Key。
- 不得將含有真實 Key 的檔案或內容提交、推送、建立 GitHub issue／PR、上傳雲端、公開展示或傳送給不必要的外部服務。不得在 commit message、PR 文字或截圖中揭露 Key。
- 執行時沿用 `spec2.md` 的 Key 流程：Key 由使用者在本機頁面輸入，僅隨開始工作階段的同源 HTTPS／loopback POST body 傳至應用程式一次，接著只在伺服器程序記憶體暫存，並只以 `x-goog-api-key` 標頭送往 Gemini。
- 模型切換、聊天、清除歷史及新對話請求不得攜帶 Key。Key 不得回傳至 HTML／JSON、隱藏欄位、JavaScript、URL、cookie、瀏覽器儲存區、一般日誌或診斷輸出。
- 開發與驗證使用假的工作階段 store、假 HTTP handler 及合成 Key；測試輸出須避免呈現 Key。不得以真實 Key 或真實配額進行本功能的自動驗證。
- 本文件不建立 Key 檔案。若後續工作確實需要本機 Key 檔，必須先確認該精確路徑已被 `.gitignore` 忽略，再建立檔案；禁止將 Key 檔加入 Git。

## 6. 擴充驗收案例

以下案例是本擴充的新增驗收條件；`spec2.md` 的其他驗收案例繼續適用。測試僅使用合成 Key 與假 HTTP handler，不連線 Gemini。

| 編號 | 情境／操作 | 預期結果 |
| --- | --- | --- |
| MS-AC-01 | 首次開啟沒有工作階段的聊天頁 | 顯示三個允許模型，預選 `gemini-3.8-flash`；選項及頁首均提示確認專案方案與模型權限；不呼叫 Gemini |
| MS-AC-02 | 使用三個允許模型各自開始工作階段 | Key 與所選模型保存在伺服器記憶體；頁面、cookie、URL 與日誌沒有 Key |
| MS-AC-03 | Begin 請求缺模型值或竄改為未允許的模型 ID | 缺值採預設；未允許值拒絕且不建立／替換工作階段、不呼叫 Gemini |
| MS-AC-04 | 有效工作階段中切換至不同模型 | 保留 Key 與 cookie，清除訊息、去重資料與截斷狀態；顯示新模型；不呼叫 Gemini |
| MS-AC-05 | 切換至目前已選模型 | 不清除歷史或去重資料，不呼叫 Gemini |
| MS-AC-06 | 按「新對話」後送出訊息 | 保留目前模型與 Key；新的 Gemini `contents` 只含新對話訊息 |
| MS-AC-07 | 竄改模型切換 POST 的模型 ID 或缺少防偽資料 | 拒絕操作；模型、Key、歷史及去重資料不變；不呼叫 Gemini |
| MS-AC-08 | 切換模型與聊天送出併發 | 後端序列化或拒絕其中一項；不得串錯模型、遺失歷史或重複呼叫 Gemini |
| MS-AC-09 | 以各模型送出一般文字 | URL 使用所選模型 ID；請求本文只有純文字 `contents`，不含 Key 或媒體欄位 |
| MS-AC-10 | 選定模型回傳 404、權限錯誤、429 或其他失敗 | 顯示分類安全訊息，不自動換模型、不洩漏原始本文；保留輸入與已完成歷史 |
| MS-AC-11 | 使用合成 Key 搜尋 Begin POST、切換模型 POST、聊天 POST、頁面回應、cookie、URL、日誌、截圖及 Git 差異 | Key 只出現在初次本機 Key 表單 body 與伺服器送往 Gemini 的認證標頭；切換及後續聊天請求不含 Key；任何真實 Key 均不得進入 Git 或公開平台 |
| MS-AC-12 | 回歸 `/ApiKeyTest` | 仍使用 `Gemini:Model`，不受 `/ApiKeyChat` 選擇影響 |
| MS-AC-13 | 有效工作階段的模型切換選單 | 3.8 Flash 選項顯示與初始表單相同的方案及權限提示；模型清單與目前模型不變 |

## 7. 文件檢查與後續驗證

- 本次變更僅更新 `Services/GeminiChatModels.cs` 的 3.8 Flash 顯示提示、`Pages/ApiKeyChat.cshtml` 頁首說明及 `SpecFile/spec3.md`；不變更允許模型清單、預設模型、伺服器模型權限判斷、API 呼叫行為或 Key 管理流程。
- 檢查本文件決策、資料流、安全要求及驗收案例一致，並確認沒有真實 Key、佔位用的可用憑證、待填欄位或互相矛盾的要求。
- 檢查工作樹只出現本次範圍內的必要變更；檢查空白字元、Markdown code fence、編碼及行尾。
- 不建立或使用真實 API Key，也不呼叫 Gemini；完成文案更新不代表 Google 專案權限或配額已實際驗證。
- 後續實作時依 `spec2.md` 與本文件執行自動及瀏覽器驗收；模型清單、存取限制、配額及定價均可能變動，開發前重新核對官方文件。

## 8. 官方參考資料

- [Gemini API 模型清單與模型可用性](https://ai.google.dev/gemini-api/docs/models?hl=en)
- [Gemini 3.8 Flash：generateContent、純文字請求與模型設定](https://ai.google.dev/gemini-api/docs/generate-content/whats-new-gemini-3.8)
- [Gemini API Key：安全取得與使用](https://ai.google.dev/gemini-api/docs/api-key)
- [Gemini API 價格表與 Free Tier／Paid Tier](https://ai.google.dev/gemini-api/docs/pricing)
- [Gemini API Billing 與計費層級](https://ai.google.dev/gemini-api/docs/billing)
- [基準規格：Gemini API Key 對話頁面開發規格書](spec2.md)
