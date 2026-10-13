(() => {
    "use strict";

    const form = document.getElementById("api-key-test-form");
    if (!form) return;

    const input = document.getElementById("Input");
    const result = document.getElementById("Result");
    const button = document.getElementById("submit-test");
    const status = document.getElementById("test-status");
    const maximumLength = Number(input.dataset.maxLength);
    let submitting = false;

    function showStatus(message, style) {
        status.textContent = message;
        status.className = `mt-3 alert alert-${style}`;
    }

    form.addEventListener("submit", (event) => {
        if (submitting) {
            event.preventDefault();
            return;
        }

        result.value = "";
        // Native form POST sends textarea line breaks as CRLF. Count exactly
        // those UTF-16 units without changing or truncating the submitted input.
        const submittedLength = input.value.replace(/\r?\n/g, "\r\n").length;
        const error = !input.value.trim()
            ? "請輸入要測試的文字"
            : submittedLength > maximumLength
                ? "輸入內容不可超過 4,000 個字元"
                : null;

        if (error) {
            event.preventDefault();
            showStatus(error, "danger");
            input.focus();
            return;
        }

        submitting = true;
        button.disabled = true;
        form.setAttribute("aria-busy", "true");
        showStatus("處理中…", "info");
    });

    window.addEventListener("pageshow", (event) => {
        submitting = false;
        button.disabled = false;
        form.removeAttribute("aria-busy");
        if (event.persisted && status.textContent === "處理中…") {
            status.textContent = "";
            status.className = "mt-3";
        }
    });
})();
