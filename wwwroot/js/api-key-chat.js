(() => {
    "use strict";

    const beginForm = document.getElementById("api-key-chat-begin-form");
    if (beginForm) {
        const keyInput = document.getElementById("ApiKey");
        beginForm.addEventListener("submit", (event) => {
            const key = keyInput.value.trim();
            const valid = key.length > 0 && [...key].every((character) => {
                const code = character.codePointAt(0);
                return code >= 0x21 && code <= 0x7e;
            });
            if (!valid) {
                event.preventDefault();
                showStatus("Key 格式不正確，請確認沒有內部空白或特殊字元。", "danger");
                keyInput.focus();
            } else {
                beginForm.querySelector("button[type=submit]").disabled = true;
                beginForm.setAttribute("aria-busy", "true");
                showStatus("正在建立工作階段…", "info");
            }
        });
    }

    const form = document.getElementById("api-key-chat-send-form");
    if (!form) return;

    const input = document.getElementById("chat-message-input");
    const sendButton = document.getElementById("chat-send-button");
    const history = document.getElementById("chat-history");
    const roundStatus = document.getElementById("chat-round-status");
    const chat = document.getElementById("api-key-chat");
    const modelSelect = document.getElementById("SelectedModelId");
    const maximumLength = Number(input.dataset.maxLength);
    let busy = false;
    let pendingRequestId = null;
    let pendingText = null;

    for (const controlForm of document.querySelectorAll(".chat-control-form")) {
        controlForm.addEventListener("submit", (event) => {
            if (busy) {
                event.preventDefault();
                return;
            }
            setBusy(true, controlForm);
            showStatus("正在更新對話…", "info");
        });
    }

    input.addEventListener("input", () => {
        if (pendingText !== null && input.value !== pendingText) {
            pendingRequestId = null;
            pendingText = null;
        }
    });

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (busy) return;

        const message = input.value;
        const submittedMessage = message.replace(/\r?\n/g, "\r\n");
        if (!message.trim()) {
            showStatus("請輸入要傳送的文字。", "danger");
            input.focus();
            return;
        }
        if (submittedMessage.length > maximumLength) {
            showStatus("訊息不可超過 4,000 個 UTF-16 單位。", "danger");
            input.focus();
            return;
        }

        if (pendingRequestId === null || pendingText !== message) {
            pendingRequestId = crypto.randomUUID();
            pendingText = message;
        }

        setBusy(true);
        showStatus("處理中…", "info");
        const body = new URLSearchParams(new FormData(form));
        body.set("Message", submittedMessage);
        body.set("RequestId", pendingRequestId);

        try {
            const response = await fetch("/ApiKeyChat?handler=Send", {
                method: "POST",
                credentials: "same-origin",
                headers: { "Accept": "application/json" },
                body
            });
            const result = await response.json();
            if (result.status === "sessionExpired") {
                window.location.reload();
                return;
            }
            if (result.status === "busy") {
                showStatus(result.message || "上一則訊息仍在處理中，請稍候。", "info");
                return;
            }
            if (result.status === "success" || result.status === "truncated") {
                appendMessage("user", message);
                appendMessage("model", result.text);
                const rounds = Number(chat.dataset.completedRounds) + 1;
                chat.dataset.completedRounds = String(rounds);
                roundStatus.textContent = `已完成 ${rounds} / 20 組`;
                if (result.status === "truncated") {
                    const last = history.lastElementChild;
                    const note = document.createElement("div");
                    note.className = "alert alert-warning mt-2 mb-0";
                    note.textContent = result.message || "內容可能不完整。";
                    last.append(note);
                }
                input.value = "";
                pendingRequestId = null;
                pendingText = null;
                showStatus(result.status === "truncated" ? result.message : "訊息已送出。", result.status === "truncated" ? "warning" : "success");
                if (rounds >= 20) {
                    const notice = document.createElement("div");
                    notice.id = "chat-limit-notice";
                    notice.className = "alert alert-warning";
                    notice.setAttribute("role", "status");
                    notice.textContent = "此對話已達 20 組上限，請開始新對話。";
                    form.before(notice);
                }
                return;
            }

            showStatus(result.message || "訊息未能完成，請檢查後再試。", "danger");
            pendingRequestId = null;
            pendingText = null;
        } catch {
            // Keep the ID and input when no explicit response arrived. A manual
            // resubmission then safely replays a result already committed by the server.
            showStatus("目前未收到明確結果，可保留文字後手動重送。", "warning");
        } finally {
            setBusy(false);
        }
    });

    function appendMessage(role, text) {
        const article = document.createElement("article");
        article.className = `chat-message ${role === "user" ? "chat-message-user" : "chat-message-model"}`;
        const label = document.createElement("h2");
        label.className = "chat-message-label";
        label.textContent = role === "user" ? "你" : "Gemini";
        const content = document.createElement("div");
        content.className = "chat-message-text";
        content.textContent = text;
        article.append(label, content);
        history.append(article);
        history.scrollTop = history.scrollHeight;
    }

    function setBusy(value, submittedControlForm = null) {
        busy = value;
        sendButton.disabled = value || Number(chat.dataset.completedRounds) >= 20;
        input.disabled = value;
        for (const control of document.querySelectorAll(".chat-control-form button")) {
            if (value && submittedControlForm?.contains(control)) continue;
            control.disabled = value;
        }
        if (modelSelect && !(value && submittedControlForm?.contains(modelSelect))) {
            modelSelect.disabled = value;
        }
        form.setAttribute("aria-busy", String(value));
    }

    function showStatus(message, style) {
        const status = document.getElementById("chat-status");
        status.textContent = message;
        status.className = `mb-3 alert alert-${style}`;
    }
})();
