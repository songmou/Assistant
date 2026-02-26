let sessionId = null;
let pollTimer = null;

const userIdInput = document.getElementById("userIdInput");
const createSessionBtn = document.getElementById("createSessionBtn");
const sessionInfo = document.getElementById("sessionInfo");
const qrImage = document.getElementById("qrImage");
const loginStatus = document.getElementById("loginStatus");
const messageInput = document.getElementById("messageInput");
const sendBtn = document.getElementById("sendBtn");
const messages = document.getElementById("messages");

createSessionBtn.addEventListener("click", async () => {
    const userId = userIdInput.value.trim();
    if (!userId) {
        showSessionError("请输入用户标识。");
        return;
    }

    const response = await fetch("/api/sessions", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ userId })
    });

    if (!response.ok) {
        const errorText = await response.text();
        showSessionError(`创建会话失败（${response.status}）：${errorText || "请稍后重试"}`);
        return;
    }

    const data = await response.json();
    sessionId = data.sessionId;
    sessionInfo.className = "alert alert-light border";
    sessionInfo.innerText = `会话已创建：${sessionId}（用户：${data.userId}）`;
    await refreshStatus();

    if (pollTimer) {
        clearInterval(pollTimer);
    }
    pollTimer = setInterval(refreshStatus, 3000);
});

sendBtn.addEventListener("click", async () => {
    const message = messageInput.value.trim();
    if (!sessionId || !message) {
        return;
    }

    appendMessage("你", message, "assistant-message-user");
    messageInput.value = "";

    const response = await fetch(`/api/sessions/${sessionId}/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message })
    });

    if (!response.ok) {
        appendMessage("系统", "处理失败，请稍后重试。", "text-danger");
        return;
    }

    const data = await response.json();
    const actionSummary = (data.actions || [])
        .map(a => {
            const error = a.error ? String(a.error) : "";
            return `- ${a.type} ${a.target}: ${a.status}${error ? ` (${error})` : ""}`;
        })
        .join("\n");
    appendMessage("助手", `${data.reply}${actionSummary ? `\n\n执行结果:\n${actionSummary}` : ""}`, "assistant-message-ai");
});

messageInput.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
        event.preventDefault();
        sendBtn.click();
    }
});

async function refreshStatus() {
    if (!sessionId) {
        return;
    }

    const [statusResp, qrResp] = await Promise.all([
        fetch(`/api/sessions/${sessionId}/status`),
        fetch(`/api/sessions/${sessionId}/qr`)
    ]);

    if (statusResp.ok) {
        const status = await statusResp.json();
        loginStatus.innerText = status.loggedIn
            ? `✅ 已登录，当前页面：${status.currentUrl}`
            : `⏳ 等待扫码，当前页面：${status.currentUrl}`;
    }

    if (qrResp.ok) {
        const blob = await qrResp.blob();
        if (qrImage.src.startsWith("blob:")) {
            URL.revokeObjectURL(qrImage.src);
        }
        qrImage.src = URL.createObjectURL(blob);
    }
}

function appendMessage(sender, text, cssClass) {
    const item = document.createElement("div");
    item.className = `assistant-message ${cssClass}`;
    item.textContent = `${sender}：${text}`;
    messages.appendChild(item);
    messages.scrollTop = messages.scrollHeight;
}

function showSessionError(text) {
    sessionInfo.className = "alert alert-danger";
    sessionInfo.innerText = text;
}
