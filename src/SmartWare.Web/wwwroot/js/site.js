(() => {
    const body = document.body;
    const toggle = document.querySelector("[data-sidebar-toggle]");
    const closeButton = document.querySelector("[data-sidebar-close]");

    if (!toggle) {
        return;
    }

    const setOpen = (open) => {
        body.classList.toggle("sidebar-open", open);
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
    };

    toggle.addEventListener("click", () => setOpen(!body.classList.contains("sidebar-open")));
    closeButton?.addEventListener("click", () => setOpen(false));

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            setOpen(false);
        }
    });

    window.addEventListener("resize", () => {
        if (window.innerWidth >= 992) {
            setOpen(false);
        }
    });
})();
