(() => {
    "use strict";

    const maximumFileSize = 5 * 1024 * 1024;
    const supportedTypes = new Set(["image/jpeg", "image/png", "image/webp"]);

    document.querySelectorAll("[data-product-image-uploader]").forEach((uploader) => {
        const input = uploader.querySelector("[data-image-input]");
        const image = uploader.querySelector("[data-image-preview-element]");
        const placeholder = uploader.querySelector("[data-image-placeholder]");
        const fileName = uploader.querySelector("[data-image-file-name]");
        const remove = uploader.querySelector("[data-image-remove]");
        const originalSource = image?.getAttribute("src") || "";
        let objectUrl = null;

        const setPreview = (source) => {
            if (!image || !placeholder) return;
            image.src = source;
            image.classList.toggle("d-none", !source);
            placeholder.classList.toggle("d-none", Boolean(source));
        };

        input?.addEventListener("change", () => {
            if (objectUrl) {
                URL.revokeObjectURL(objectUrl);
                objectUrl = null;
            }

            const file = input.files?.[0];
            input.setCustomValidity("");
            if (!file) {
                fileName.textContent = "Chưa chọn ảnh mới.";
                setPreview(remove?.checked ? "" : originalSource);
                return;
            }

            if (!supportedTypes.has(file.type)) {
                input.setCustomValidity("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.");
                input.reportValidity();
                input.value = "";
                input.setCustomValidity("");
                fileName.textContent = "Định dạng ảnh không được hỗ trợ.";
                setPreview(remove?.checked ? "" : originalSource);
                return;
            }

            if (file.size > maximumFileSize) {
                input.setCustomValidity("Ảnh sản phẩm không được vượt quá 5 MB.");
                input.reportValidity();
                input.value = "";
                input.setCustomValidity("");
                fileName.textContent = "Ảnh đã chọn lớn hơn 5 MB.";
                setPreview(remove?.checked ? "" : originalSource);
                return;
            }

            if (remove) remove.checked = false;
            objectUrl = URL.createObjectURL(file);
            setPreview(objectUrl);
            fileName.textContent = `${file.name} · ${(file.size / 1024 / 1024).toFixed(2)} MB`;
        });

        remove?.addEventListener("change", () => {
            if (remove.checked && input) {
                input.value = "";
                input.setCustomValidity("");
                fileName.textContent = "Ảnh hiện tại sẽ được xóa khi lưu.";
                setPreview("");
            } else {
                fileName.textContent = "Chưa chọn ảnh mới.";
                setPreview(originalSource);
            }
        });

        window.addEventListener("beforeunload", () => {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
        }, { once: true });
    });
})();
