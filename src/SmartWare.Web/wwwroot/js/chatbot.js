(() => {
    const root = document.querySelector("[data-chatbot]");
    if (!root) return;

    const panel = root.querySelector("[data-chatbot-panel]");
    const openButton = root.querySelector("[data-chatbot-open]");
    const closeButton = root.querySelector("[data-chatbot-close]");
    const newButton = root.querySelector("[data-chatbot-new]");
    const historyButton = root.querySelector("[data-chatbot-history]");
    const historyPanel = root.querySelector("[data-chatbot-history-panel]");
    const historyClose = root.querySelector("[data-chatbot-history-close]");
    const historyList = root.querySelector("[data-chatbot-history-list]");
    const form = root.querySelector("[data-chatbot-form]");
    const input = root.querySelector("[data-chatbot-input]");
    const sendButton = root.querySelector("[data-chatbot-send]");
    const messages = root.querySelector("[data-chatbot-messages]");
    const suggestions = root.querySelector("[data-chatbot-suggestions]");
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let sending = false;
    let sessionId = null;

    function appendInlineMarkdown(element, text) {
        const boldPattern = /\*\*(.+?)\*\*/g;
        let cursor = 0;
        let match;
        while ((match = boldPattern.exec(text)) !== null) {
            if (match.index > cursor) {
                element.appendChild(document.createTextNode(text.slice(cursor, match.index)));
            }
            const strong = document.createElement("strong");
            strong.textContent = match[1];
            element.appendChild(strong);
            cursor = match.index + match[0].length;
        }
        if (cursor < text.length) {
            element.appendChild(document.createTextNode(text.slice(cursor)));
        }
    }

    function renderAssistantText(element, text) {
        element.classList.add("chatbot-richtext");
        const lines = text.replace(/\r\n/g, "\n").split("\n");
        let activeList = null;
        let activeListType = null;

        const resetList = () => {
            activeList = null;
            activeListType = null;
        };

        lines.forEach(line => {
            const trimmed = line.trim();
            if (!trimmed) {
                resetList();
                return;
            }

            const heading = trimmed.match(/^(#{1,3})\s+(.+)$/);
            if (heading) {
                resetList();
                const title = document.createElement(heading[1].length === 1 ? "h3" : "h4");
                appendInlineMarkdown(title, heading[2]);
                element.appendChild(title);
                return;
            }

            const unordered = trimmed.match(/^[-*]\s+(.+)$/);
            const ordered = trimmed.match(/^\d+[.)]\s+(.+)$/);
            if (unordered || ordered) {
                const listType = ordered ? "ol" : "ul";
                if (!activeList || activeListType !== listType) {
                    activeList = document.createElement(listType);
                    activeListType = listType;
                    element.appendChild(activeList);
                }
                const item = document.createElement("li");
                appendInlineMarkdown(item, (ordered ?? unordered)[1]);
                activeList.appendChild(item);
                return;
            }

            resetList();
            const paragraph = document.createElement("p");
            appendInlineMarkdown(paragraph, trimmed);
            element.appendChild(paragraph);
        });
    }

    function appendMessage(type, text, sources = []) {
        const wrapper = document.createElement("div");
        wrapper.className = `chatbot-message ${type}`;
        const content = document.createElement("div");
        content.className = "chatbot-bubble";
        if (type === "assistant") {
            renderAssistantText(content, text);
        } else {
            content.textContent = text;
        }
        wrapper.appendChild(content);
        if (sources.length > 0) {
            const sourceList = document.createElement("div");
            sourceList.className = "chatbot-sources";
            const label = document.createElement("span");
            label.className = "chatbot-sources-label";
            label.textContent = "Nguồn";
            sourceList.appendChild(label);
            sources.forEach(source => {
                const chip = document.createElement("span");
                chip.className = `chatbot-source-chip${source.startsWith("RAG:") ? " rag" : " sql"}`;
                chip.textContent = source;
                sourceList.appendChild(chip);
            });
            wrapper.appendChild(sourceList);
        }
        messages.appendChild(wrapper);
        messages.scrollTop = messages.scrollHeight;
        return wrapper;
    }

    const renderWelcome = () => {
        messages.replaceChildren();
        appendMessage(
            "assistant",
            "Xin chào! Tôi có thể tra cứu dữ liệu kho và tìm câu trả lời trong kho tri thức bằng RAG. Mỗi cuộc trò chuyện được lưu riêng cho tài khoản của bạn."
        );
        suggestions.hidden = false;
    };

    const setOpen = (open) => {
        root.classList.toggle("open", open);
        panel.setAttribute("aria-hidden", open ? "false" : "true");
        openButton.setAttribute("aria-expanded", open ? "true" : "false");
        if (open) {
            loadHistory();
            window.setTimeout(() => input.focus(), 100);
        }
    };

    const setSending = (value) => {
        sending = value;
        input.disabled = value;
        sendButton.disabled = value;
        root.classList.toggle("sending", value);
    };

    const requestJson = async (url, options = {}) => {
        const response = await fetch(url, { credentials: "same-origin", ...options });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.status === 204 ? null : response.json();
    };

    const loadHistory = async () => {
        try {
            const sessions = await requestJson("/chatbot/history");
            historyList.replaceChildren();
            if (sessions.length === 0) {
                const empty = document.createElement("p");
                empty.className = "chatbot-history-empty";
                empty.textContent = "Chưa có cuộc trò chuyện nào.";
                historyList.appendChild(empty);
                return;
            }

            sessions.forEach(session => {
                const row = document.createElement("div");
                row.className = `chatbot-history-item${session.id === sessionId ? " active" : ""}`;
                const open = document.createElement("button");
                open.type = "button";
                open.className = "chatbot-history-open";
                open.dataset.sessionId = session.id;
                const title = document.createElement("strong");
                title.textContent = session.title;
                const meta = document.createElement("small");
                meta.textContent = `${session.messageCount} tin · ${new Date(session.updatedAt).toLocaleString("vi-VN")}`;
                open.append(title, meta);
                const remove = document.createElement("button");
                remove.type = "button";
                remove.className = "chatbot-history-delete";
                remove.dataset.deleteSessionId = session.id;
                remove.setAttribute("aria-label", "Xóa cuộc trò chuyện");
                remove.textContent = "×";
                row.append(open, remove);
                historyList.appendChild(row);
            });
        } catch {
            historyList.replaceChildren();
            const error = document.createElement("p");
            error.className = "chatbot-history-empty";
            error.textContent = "Không tải được lịch sử.";
            historyList.appendChild(error);
        }
    };

    const openSession = async (id) => {
        try {
            const session = await requestJson(`/chatbot/history/${encodeURIComponent(id)}`);
            sessionId = session.id;
            messages.replaceChildren();
            session.messages.forEach(message => {
                appendMessage(message.role === "user" ? "user" : "assistant", message.content, message.sources ?? []);
            });
            suggestions.hidden = session.messages.length > 0;
            historyPanel.hidden = true;
            await loadHistory();
        } catch {
            appendMessage("assistant error", "Không thể mở cuộc trò chuyện đã chọn.");
        }
    };

    const deleteSession = async (id) => {
        try {
            await requestJson(`/chatbot/history/${encodeURIComponent(id)}`, {
                method: "DELETE",
                headers: { "RequestVerificationToken": token }
            });
            if (sessionId === id) {
                sessionId = null;
                renderWelcome();
            }
            await loadHistory();
        } catch {
            appendMessage("assistant error", "Không thể xóa cuộc trò chuyện.");
        }
    };

    const newChat = () => {
        sessionId = null;
        historyPanel.hidden = true;
        renderWelcome();
        input.focus();
        loadHistory();
    };

    const ask = async (message) => {
        const trimmed = message.trim();
        if (sending || trimmed.length < 2) return;

        appendMessage("user", trimmed);
        input.value = "";
        suggestions.hidden = true;
        const typing = appendMessage("assistant typing", "Đang phân tích dữ liệu và soạn câu trả lời...");
        setSending(true);
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 50000);
        try {
            const response = await fetch("/chatbot/ask", {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": token
                },
                body: JSON.stringify({ message: trimmed, sessionId }),
                signal: controller.signal
            });
            let result;
            try { result = await response.json(); } catch { result = null; }
            typing.remove();
            if (result?.answer) {
                appendMessage(result.success ? "assistant" : "assistant error", result.answer, result.sources ?? []);
                if (result.success && result.sessionId) {
                    sessionId = result.sessionId;
                    await loadHistory();
                }
            } else if (response.status === 429) {
                appendMessage("assistant error", "Bạn đã gửi quá nhiều câu hỏi. Vui lòng chờ một phút rồi thử lại.");
            } else {
                appendMessage("assistant error", "Không thể xử lý câu hỏi lúc này. Vui lòng thử lại sau.");
            }
        } catch (error) {
            typing.remove();
            appendMessage(
                "assistant error",
                error.name === "AbortError"
                    ? "Yêu cầu đã quá thời gian chờ. Vui lòng thử lại."
                    : "Không thể kết nối tới chatbot. Vui lòng kiểm tra kết nối và thử lại."
            );
        } finally {
            window.clearTimeout(timeout);
            setSending(false);
            input.focus();
        }
    };

    openButton.addEventListener("click", () => setOpen(!root.classList.contains("open")));
    closeButton.addEventListener("click", () => setOpen(false));
    newButton.addEventListener("click", newChat);
    historyButton.addEventListener("click", async () => {
        historyPanel.hidden = !historyPanel.hidden;
        if (!historyPanel.hidden) await loadHistory();
    });
    historyClose.addEventListener("click", () => { historyPanel.hidden = true; });
    historyList.addEventListener("click", event => {
        const open = event.target.closest("[data-session-id]");
        const remove = event.target.closest("[data-delete-session-id]");
        if (open) openSession(open.dataset.sessionId);
        if (remove) deleteSession(remove.dataset.deleteSessionId);
    });
    form.addEventListener("submit", event => { event.preventDefault(); ask(input.value); });
    input.addEventListener("keydown", event => {
        if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            form.requestSubmit();
        }
    });
    suggestions.addEventListener("click", event => {
        const button = event.target.closest("[data-chatbot-prompt]");
        if (button) ask(button.dataset.chatbotPrompt ?? "");
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && root.classList.contains("open")) setOpen(false);
    });
})();
