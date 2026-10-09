// UX helpers for the signed-in shell: collapsible sidebar, quick search (Ctrl+K), toasts and
// busy submit buttons. All content is inserted with textContent / cloned nodes, never innerHTML.
(() => {
    const body = document.body;
    if (!body.classList.contains("app-body")) return;

    const storage = {
        get(key) { try { return window.localStorage.getItem(key); } catch { return null; } },
        set(key, value) { try { window.localStorage.setItem(key, value); } catch { /* storage unavailable */ } }
    };

    const fold = (text) => text.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/đ/g, "d").replace(/Đ/g, "D").toLowerCase().trim();

    // ---------- Collapsible sidebar ----------
    const collapseButton = document.querySelector("[data-sidebar-collapse]");
    const setCollapsed = (collapsed) => {
        body.classList.toggle("sidebar-collapsed", collapsed);
        collapseButton?.setAttribute("aria-pressed", collapsed ? "true" : "false");
        collapseButton?.setAttribute("title", collapsed ? "Mở rộng menu" : "Thu gọn menu");
        storage.set("sw.sidebarCollapsed", collapsed ? "1" : "0");
    };
    document.querySelectorAll(".sidebar-link").forEach(link => {
        link.title = link.querySelector("span")?.textContent.trim() ?? "";
    });
    if (storage.get("sw.sidebarCollapsed") === "1") setCollapsed(true);
    collapseButton?.addEventListener("click", () => setCollapsed(!body.classList.contains("sidebar-collapsed")));

    // ---------- Toasts ----------
    const toastStack = document.createElement("div");
    toastStack.className = "toast-stack";
    toastStack.setAttribute("aria-live", "polite");
    body.appendChild(toastStack);

    const dismissToast = (toast) => {
        if (!toast.isConnected) return;
        toast.classList.add("is-leaving");
        window.setTimeout(() => toast.remove(), 200);
    };

    const showToast = (message, type = "success") => {
        const toast = document.createElement("div");
        toast.className = `app-toast is-${type}`;
        toast.setAttribute("role", type === "error" ? "alert" : "status");
        const icon = document.createElement("span");
        icon.className = "toast-icon";
        icon.textContent = type === "success" ? "✓" : type === "error" ? "!" : "i";
        const text = document.createElement("div");
        text.className = "toast-body";
        text.textContent = message;
        const close = document.createElement("button");
        close.type = "button";
        close.className = "toast-close";
        close.setAttribute("aria-label", "Đóng thông báo");
        close.textContent = "×";
        close.addEventListener("click", () => dismissToast(toast));
        toast.append(icon, text, close);
        toastStack.appendChild(toast);
        // Errors stay until dismissed so the user can read them.
        if (type !== "error") window.setTimeout(() => dismissToast(toast), 5000);
    };
    window.SmartWareToast = showToast;

    // TempData alerts rendered at the top of a page become toasts; form validation alerts stay inline.
    document.querySelectorAll(".app-content > .alert").forEach(alert => {
        const type = alert.classList.contains("alert-danger") ? "error"
            : alert.classList.contains("alert-warning") ? "warning"
            : alert.classList.contains("alert-info") ? "info" : "success";
        const message = Array.from(alert.childNodes)
            .filter(node => !(node instanceof Element && node.classList.contains("btn-close")))
            .map(node => node.textContent)
            .join(" ")
            .replace(/\s+/g, " ")
            .trim();
        if (!message) return;
        alert.remove();
        showToast(message, type);
    });

    // ---------- Busy submit buttons (prevents double submission) ----------
    document.addEventListener("submit", event => {
        const form = event.target;
        if (event.defaultPrevented || !(form instanceof HTMLFormElement)) return;
        if ((form.getAttribute("method") ?? "get").toLowerCase() !== "post") return;
        const button = event.submitter ?? form.querySelector("button[type=submit], button:not([type])");
        if (!button || button.classList.contains("logout-button")) return;
        window.setTimeout(() => {
            button.classList.add("is-busy");
            button.setAttribute("aria-busy", "true");
            form.querySelectorAll("button[type=submit], button:not([type])").forEach(b => { b.disabled = true; });
        }, 0);
    });
    // Restore buttons when the page is shown again from the back/forward cache.
    window.addEventListener("pageshow", event => {
        if (!event.persisted) return;
        document.querySelectorAll(".btn.is-busy").forEach(button => {
            button.classList.remove("is-busy");
            button.removeAttribute("aria-busy");
        });
        document.querySelectorAll("form button[disabled]").forEach(button => { button.disabled = false; });
    });

    // ---------- Quick search palette ----------
    const palette = document.querySelector("[data-palette]");
    if (!palette) return;
    const input = palette.querySelector("[data-palette-input]");
    const list = palette.querySelector("[data-palette-list]");
    const openButtons = document.querySelectorAll("[data-palette-open]");
    const searchIcon = palette.querySelector(".palette-input-row svg");
    let items = [];
    let selected = 0;
    let lastFocus = null;

    const pages = Array.from(document.querySelectorAll(".sidebar-link")).map(link => ({
        group: "Trang",
        label: link.querySelector("span")?.textContent.trim() ?? link.title,
        href: link.getAttribute("href"),
        icon: link.querySelector("svg")
    }));
    const actions = Array.from(palette.querySelectorAll("[data-palette-action]")).map(link => ({
        group: "Thao tác nhanh",
        label: link.textContent.trim(),
        href: link.getAttribute("href"),
        hint: link.dataset.hint ?? ""
    }));

    const searchTargets = [
        { label: "Tìm sản phẩm", href: "/san-pham" },
        { label: "Tìm trong tồn kho", href: "/ton-kho" },
        { label: "Tìm phiếu nhập", href: "/nhap-kho" },
        { label: "Tìm phiếu xuất", href: "/xuat-kho" }
    ];

    const buildItems = (query) => {
        const folded = fold(query);
        const matches = (label) => !folded || fold(label).includes(folded);
        const result = [
            ...actions.filter(item => matches(item.label)),
            ...pages.filter(item => matches(item.label))
        ];
        if (query.trim()) {
            if (window.SmartWareChat) {
                // Matching pages/actions come first (the query is probably navigation); when nothing
                // matches, "Hỏi AI" ends up first and becomes the Enter default for a question.
                result.push({ group: "Trợ lý AI", label: `Hỏi AI: “${query.trim()}”`, ai: query.trim(), hint: "Trợ lý AI" });
            }
            result.push(...searchTargets.map(target => ({
                group: "Tìm kiếm",
                label: `${target.label}: “${query.trim()}”`,
                href: `${target.href}?search=${encodeURIComponent(query.trim())}`
            })));
        }
        return result;
    };

    const iconFor = (item) => {
        const holder = document.createElement("span");
        holder.className = "palette-icon";
        const aiIcon = item.ai ? document.querySelector(".chatbot-launcher svg") : null;
        if (item.icon || aiIcon) {
            holder.appendChild((item.icon ?? aiIcon).cloneNode(true));
        } else if (searchIcon) {
            holder.appendChild(searchIcon.cloneNode(true));
        }
        return holder;
    };

    const render = () => {
        list.replaceChildren();
        if (items.length === 0) {
            const empty = document.createElement("li");
            empty.className = "palette-empty";
            empty.textContent = "Không có kết quả. Thử từ khoá khác.";
            list.appendChild(empty);
            return;
        }
        let currentGroup = null;
        items.forEach((item, index) => {
            if (item.group !== currentGroup) {
                currentGroup = item.group;
                const heading = document.createElement("li");
                heading.className = "palette-group";
                heading.setAttribute("role", "presentation");
                heading.textContent = item.group;
                list.appendChild(heading);
            }
            const row = document.createElement("li");
            row.className = `palette-item${item.ai ? " is-ai" : ""}`;
            row.id = `palette-item-${index}`;
            row.setAttribute("role", "option");
            row.setAttribute("aria-selected", index === selected ? "true" : "false");
            row.dataset.index = String(index);
            const label = document.createElement("span");
            label.textContent = item.label;
            row.append(iconFor(item), label);
            if (item.hint) {
                const hint = document.createElement("small");
                hint.textContent = item.hint;
                row.appendChild(hint);
            }
            list.appendChild(row);
        });
        input.setAttribute("aria-activedescendant", `palette-item-${selected}`);
        list.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: "nearest" });
    };

    const refresh = () => {
        items = buildItems(input.value);
        selected = 0;
        render();
    };

    const open = () => {
        lastFocus = document.activeElement;
        palette.hidden = false;
        input.value = "";
        refresh();
        input.focus();
    };

    const close = () => {
        palette.hidden = true;
        lastFocus?.focus?.();
    };

    const activate = (item) => {
        if (!item) return;
        close();
        if (item.ai) {
            window.SmartWareChat?.ask(item.ai);
        } else if (item.href) {
            window.location.href = item.href;
        }
    };

    openButtons.forEach(button => button.addEventListener("click", open));
    palette.addEventListener("click", event => {
        if (event.target === palette) close();
        const row = event.target.closest(".palette-item");
        if (row) activate(items[Number(row.dataset.index)]);
    });
    list.addEventListener("mousemove", event => {
        const row = event.target.closest(".palette-item");
        if (!row || Number(row.dataset.index) === selected) return;
        selected = Number(row.dataset.index);
        list.querySelectorAll(".palette-item").forEach(el => el.setAttribute("aria-selected", el === row ? "true" : "false"));
        input.setAttribute("aria-activedescendant", row.id);
    });
    input.addEventListener("input", refresh);
    input.addEventListener("keydown", event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            if (items.length === 0) return;
            selected = (selected + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
            render();
        } else if (event.key === "Enter") {
            event.preventDefault();
            activate(items[selected]);
        } else if (event.key === "Escape") {
            event.preventDefault();
            close();
        }
    });

    document.addEventListener("keydown", event => {
        const typing = event.target instanceof HTMLElement &&
            (event.target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName));
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
            event.preventDefault();
            palette.hidden ? open() : close();
        } else if (event.key === "/" && !typing && palette.hidden) {
            event.preventDefault();
            open();
        }
    });
})();
