# API Key Chat Model Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the three-model selector to `/ApiKeyChat` while keeping the selected model in the server-side session and preserving `/ApiKeyTest` behavior.

**Architecture:** Define a fixed chat-model catalog, carry its model ID through session snapshots and send leases, and serialize model changes with existing per-session send/reset operations. Route chat requests through the selected model while leaving the single-call service on `Gemini:Model`; expose selection and switching through Razor Pages and use the existing fake-handler verification project.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, IMemoryCache, HttpClient, JavaScript, the existing `Verification` console checks.

**Spec:** `SpecFile/spec2.md` (existing chat requirements) and `SpecFile/spec3.md` (model selection extension).

## Global Constraints

- Use only synthetic keys and fake HTTP handlers; never read, print, test with, or transmit a real API key, and never call Gemini.
- Allow only `gemini-3.8-flash`, `gemini-3.5-flash-lite`, and `gemini-2.5-flash`; default new sessions to `gemini-3.8-flash`.
- Keep the selected model, key, conversation, deduplication results, and truncation state in the server memory session; the cookie continues to contain only the random session index.
- Switching to a different model preserves the key and cookie and clears conversation, completed request IDs, and truncation state; selecting the current model preserves all state.
- A new conversation retains the key and selected model. A concurrent send and model change must serialize or reject without sending a request to the wrong model.
- `/ApiKeyTest` continues to use `Gemini:Model`; do not modify its single-call behavior.
- Model selection and subsequent chat requests do not send the key from the browser; requests sent to Gemini contain only text contents and use the key header.

## Review Focus

- Missing model on Begin defaults to `gemini-3.8-flash`; an unapproved ID is rejected without replacing an existing session or calling Gemini.
- A real model change clears all old conversation state atomically while retaining the key and cookie; a same-model post preserves history and replay results.
- A switch racing with a send cannot cause a request to use a model different from the session snapshot or lose a committed turn.
- Model IDs are allowlisted at server boundaries; arbitrary values cannot become Gemini URL path segments.
- HTML, cookies, page URLs, chat POSTs, Gemini bodies, and diagnostic logs do not expose the key; `/ApiKeyTest` remains pinned to its configured model.

---

### Task 1: Model catalog and session state

**Files:**
- Create: `Services/GeminiChatModels.cs`
- Modify: `Services/ChatSessionStore.cs`
- Modify: `Verification/ChatSessionChecks.cs`

**Interfaces:**
- Produce `GeminiChatModels.DefaultModelId`, `GeminiChatModels.All`, and `GeminiChatModels.IsAllowed(string?)` with display names and the three required hints.
- Change `IChatSessionStore.Create` to accept `(string apiKey, string modelId)` and add `ChangeModelAsync(string sessionIndex, string modelId, CancellationToken)`.
- Add the model ID to `ChatSessionSnapshot` and `ChatSendLease`; `TryBeginSend` snapshots it under the existing session lock.

- [x] Add session checks proving the default and all three allowlisted IDs are stored and returned by snapshots and leases.
- [x] Add checks that changing models clears messages, completed request IDs, and truncation state while retaining key; changing to the same model preserves those values.
- [x] Add a check that a change waits for an active send and blocks a subsequent send until the new model is applied.
- [x] Implement the catalog and session changes using the existing `OperationGate` and `PendingControls` synchronization.
- [x] Run `dotnet run --project Verification/Verification.csproj -- --session-only` and require all session checks to pass.

### Task 2: Model-aware Gemini chat calls

**Files:**
- Modify: `Services/GeminiApiClient.cs`
- Modify: `Services/GeminiService.cs`
- Modify: `Services/GeminiResult.cs`
- Modify: `Verification/ChatServiceChecks.cs`

**Interfaces:**
- Change `IGeminiChatService.GenerateAsync` to accept the server-selected model ID before the key and messages.
- Add an explicit-model `GeminiApiClient.GenerateAsync` overload; keep the existing overload bound to `GeminiSettings.Model` for `/ApiKeyTest`.

- [x] Add fake-handler checks for each allowed model URL, text-only `contents`, and key-only authentication header.
- [x] Reuse existing `Model_CustomIdentifier_UsedWithoutFallback` coverage to verify that the single-call service still targets configured `Gemini:Model`.
- [x] Implement the overload and service interface update with URL identifier validation and the fixed chat-model allowlist.
- [x] Run `dotnet run --project Verification/Verification.csproj -- --chat-only` and `dotnet run --project Verification/Verification.csproj -- --services-only`.

### Task 3: Begin, display, and switch models in Razor Pages

**Files:**
- Modify: `Pages/ApiKeyChat.cshtml.cs`
- Modify: `Pages/ApiKeyChat.cshtml`
- Modify: `wwwroot/js/api-key-chat.js`
- Modify: `Verification/ChatPageChecks.cs`

**Interfaces:**
- Bind `SelectedModelId` for Begin and ChangeModel handlers; populate the current model from the server session on GET.
- Begin accepts a missing model as the default, rejects unapproved IDs before replacing a session, and creates the session with the validated model.
- ChangeModel posts only model ID and antiforgery token, then redirects after an atomic store update; an absent or invalid session does not call Gemini.
- Display all three model choices with the recommended, lower-cost, and access-limitation hints; explicitly state that changing models clears the conversation.

- [x] Add page checks for initial choices/default, all allowed Begin values, missing-model default, and invalid model rejection without session replacement.
- [x] Add page checks for a different-model switch retaining the same cookie and key while clearing history, and a same-model switch preserving replay/deduplication.
- [x] Add a fake-handler page check for concurrent send and model switch, then verify the next request uses the newly selected model.
- [x] Add UI controls and a `ChangeModel` antiforgery-protected handler; ensure the browser never submits the key or history on switch.
- [x] Update chat busy-state handling so switching is disabled during a send, and keep DOM rendering on `textContent`.
- [x] Run `dotnet run --project Verification/Verification.csproj -- --chat-page-only`.

### Task 4: Integrated verification and scope review

**Files:**
- No additional product files.

- [ ] Run `dotnet build TestAPIKeyCalling.csproj --no-restore`.
- [ ] Run `dotnet run --project Verification/Verification.csproj --no-restore` and require all synthetic checks to pass.
- [ ] Run `node --check wwwroot/js/api-key-chat.js`.
- [ ] Run `git diff --check` and review the final changed-path list; confirm no key file, `appsettings`, `SpecFile/spec2.md`, or `/ApiKeyTest` page was changed.
- [ ] Confirm the tests use only synthetic keys and fake HTTP handlers and make no Gemini network request.
