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
        // **bold**, `code` and *italic* / _italic_; content is always inserted as text, never HTML.
        const inlinePattern = /\*\*(.+?)\*\*|`([^`]+)`|\*([^*\s][^*]*?)\*|(?<![\p{L}\p{N}])_([^_\s][^_]*?)_(?![\p{L}\p{N}])/gu;
        let cursor = 0;
        let match;
        while ((match = inlinePattern.exec(text)) !== null) {
            if (match.index > cursor) {
                element.appendChild(document.createTextNode(text.slice(cursor, match.index)));
            }
            const [, bold, code, italicStar, italicUnderscore] = match;
            const node = document.createElement(bold !== undefined ? "strong" : code !== undefined ? "code" : "em");
            if (bold !== undefined) {
                appendInlineMarkdown(node, bold);
            } else {
                node.textContent = code ?? italicStar ?? italicUnderscore;
            }
            element.appendChild(node);
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

        const splitRow = (row) => row.replace(/^\|/, "").replace(/\|$/, "").split("|").map(cell => cell.trim());
        const isTableSeparator = (row) => /^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$/.test(row);

        for (let index = 0; index < lines.length; index++) {
            const trimmed = lines[index].trim();
            if (!trimmed) {
                resetList();
                continue;
            }

            if (trimmed.startsWith("|") && isTableSeparator(lines[index + 1]?.trim() ?? "")) {
                resetList();
                const wrapper = document.createElement("div");
                wrapper.className = "chatbot-table-wrapper";
                const table = document.createElement("table");
                const headRow = table.createTHead().insertRow();
                splitRow(trimmed).forEach(cell => {
                    const th = document.createElement("th");
                    appendInlineMarkdown(th, cell);
                    headRow.appendChild(th);
                });
                const body = table.createTBody();
                index += 2;
                while (index < lines.length && lines[index].trim().startsWith("|")) {
                    const row = body.insertRow();
                    splitRow(lines[index].trim()).forEach(cell => appendInlineMarkdown(row.insertCell(), cell));
                    index++;
                }
                index--;
                wrapper.appendChild(table);
                element.appendChild(wrapper);
                continue;
            }

            const quote = trimmed.match(/^>\s?(.*)$/);
            if (quote) {
                resetList();
                const blockquote = document.createElement("blockquote");
                appendInlineMarkdown(blockquote, quote[1]);
                element.appendChild(blockquote);
                continue;
            }

            if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
                resetList();
                element.appendChild(document.createElement("hr"));
                continue;
            }

            const heading = trimmed.match(/^(#{1,3})\s+(.+)$/);
            if (heading) {
                resetList();
                const title = document.createElement(heading[1].length === 1 ? "h3" : "h4");
                appendInlineMarkdown(title, heading[2]);
                element.appendChild(title);
                continue;
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
                continue;
            }

            resetList();
            const paragraph = document.createElement("p");
            appendInlineMarkdown(paragraph, trimmed);
            element.appendChild(paragraph);
        }
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

    const money = (value) => `${Math.round(value).toLocaleString("vi-VN")} đ`;
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };

    // Preview card for a receipt the assistant drafted. Nothing exists in the database until
    // the user presses "Xác nhận tạo phiếu"; the server re-validates everything on confirm.
    function appendDraftCard(draft) {
        const isImport = draft.type === "import";
        const card = element("div", `chatbot-draft${draft.canConfirm ? "" : " is-blocked"}`);
        const header = element("div", "chatbot-draft-header");
        header.append(
            element("strong", "", isImport ? "Bản nháp phiếu nhập" : "Bản nháp phiếu xuất"),
            element("span", "chatbot-draft-badge", "Chưa tạo"));
        card.appendChild(header);

        const meta = element("div", "chatbot-draft-meta");
        if (draft.warehouseName) meta.appendChild(element("span", "", `Kho: ${draft.warehouseName}`));
        if (isImport && draft.supplierName) meta.appendChild(element("span", "", `NCC: ${draft.supplierName}`));
        if (!isImport && draft.orderNumber) meta.appendChild(element("span", "", `Đơn hàng: ${draft.orderNumber}`));
        card.appendChild(meta);

        if (draft.lines.length > 0) {
            const wrapper = element("div", "chatbot-table-wrapper");
            const table = element("table");
            const headRow = table.createTHead().insertRow();
            (isImport ? ["Sản phẩm", "SL", "Đơn giá", "Thành tiền"] : ["Sản phẩm", "SL", "Khả dụng"])
                .forEach(label => headRow.appendChild(element("th", "", label)));
            const body = table.createTBody();
            draft.lines.forEach(line => {
                const row = body.insertRow();
                const product = row.insertCell();
                product.append(element("strong", "", line.sku), document.createTextNode(` ${line.productName}`));
                row.insertCell().textContent = `${line.quantity.toLocaleString("vi-VN")} ${line.unitOfMeasure}`;
                if (isImport) {
                    row.insertCell().textContent = money(line.unitCost);
                    row.insertCell().textContent = money(line.unitCost * line.quantity);
                } else {
                    const available = row.insertCell();
                    available.textContent = (line.availableQuantity ?? 0).toLocaleString("vi-VN");
                    if ((line.availableQuantity ?? 0) < line.quantity) available.className = "is-short";
                }
            });
            wrapper.appendChild(table);
            card.appendChild(wrapper);
            card.appendChild(element("div", "chatbot-draft-total", `${isImport ? "Tổng giá trị" : "Giá vốn ước tính"}: ${money(draft.totalValue)}`));
        }

        if (draft.warnings.length > 0) {
            const warnings = element("ul", "chatbot-draft-warnings");
            draft.warnings.forEach(text => warnings.appendChild(element("li", "", text)));
            card.appendChild(warnings);
        }

        const actions = element("div", "chatbot-draft-actions");
        const confirm = element("button", "chatbot-draft-confirm", "Xác nhận tạo phiếu");
        confirm.type = "button";
        confirm.disabled = !draft.canConfirm;
        const edit = element("a", "chatbot-draft-edit", "Mở trong form để sửa");
        edit.href = `${isImport ? "/nhap-kho" : "/xuat-kho"}/tao-phieu?draftId=${encodeURIComponent(draft.id)}`;
        actions.append(confirm, edit);
        card.appendChild(actions);
        card.appendChild(element("small", "chatbot-draft-note", "Phiếu được tạo ở trạng thái Chờ duyệt và cần Quản lý duyệt."));

        confirm.addEventListener("click", async () => {
            confirm.disabled = true;
            confirm.textContent = "Đang tạo phiếu…";
            try {
                const response = await fetch(`/chatbot/drafts/${encodeURIComponent(draft.id)}/confirm`, {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "RequestVerificationToken": token }
                });
                let result;
                try { result = await response.json(); } catch { result = null; }
                if (response.ok && result?.success) {
                    card.classList.add("is-done");
                    header.querySelector(".chatbot-draft-badge").textContent = "Đã tạo";
                    actions.replaceChildren(element("span", "chatbot-draft-success", result.message));
                    if (result.listUrl) {
                        const link = element("a", "chatbot-draft-edit", "Xem danh sách phiếu");
                        link.href = result.listUrl;
                        actions.appendChild(link);
                    }
                    window.SmartWareToast?.(result.message, "success");
                } else {
                    const message = result?.message ?? (response.status === 403
                        ? "Vai trò của bạn không được phép tạo phiếu."
                        : "Không thể tạo phiếu lúc này. Vui lòng thử lại.");
                    confirm.disabled = false;
                    confirm.textContent = "Xác nhận tạo phiếu";
                    window.SmartWareToast?.(message, "error");
                }
            } catch {
                confirm.disabled = false;
                confirm.textContent = "Xác nhận tạo phiếu";
                window.SmartWareToast?.("Không thể kết nối tới máy chủ.", "error");
            }
        });

        messages.appendChild(card);
        messages.scrollTop = messages.scrollHeight;
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
                if (result.success && result.draft) {
                    appendDraftCard(result.draft);
                }
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

    // Lets other UI (the Ctrl+K quick search) open the assistant with a question.
    window.SmartWareChat = {
        open: () => setOpen(true),
        ask: (text) => {
            setOpen(true);
            ask(text);
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
