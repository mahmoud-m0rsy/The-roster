(() => {
    "use strict";
    const csrf = document.querySelector('meta[name="csrf-token"]')?.content ?? "";

    // ───────────── Live toast ─────────────
    const live = document.querySelector("[data-live-toast]");
    let toastTimer = 0;
    const toast = (message, isError = false) => {
        if (!live) return;
        live.textContent = message;
        live.classList.toggle("is-error", isError);
        live.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => (live.hidden = true), isError ? 6000 : 2600);
    };

    document.querySelectorAll("[data-toast]").forEach((el) => {
        setTimeout(() => {
            el.classList.add("is-leaving");
            setTimeout(() => el.remove(), 450);
        }, 6000);
    });

    // ───────────── Mobile sidebar ─────────────
    const sidebar = document.getElementById("sidebar");
    const menuBtn = document.querySelector("[data-sidebar-toggle]");
    const setMenu = (open) => {
        sidebar?.classList.toggle("is-open", open);
        menuBtn?.setAttribute("aria-expanded", String(open));
    };
    menuBtn?.addEventListener("click", () => setMenu(!sidebar.classList.contains("is-open")));
    document.querySelector("[data-sidebar-close]")?.addEventListener("click", () => setMenu(false));
    document.addEventListener("keydown", (e) => { if (e.key === "Escape") setMenu(false); });

    // ───────────── Confirm dialogs ─────────────
    const confirmDialog = document.createElement("dialog");
    confirmDialog.className = "confirm";
    confirmDialog.innerHTML = `
        <form method="dialog">
            <div class="confirm__body"><h2>Are you sure?</h2><p class="muted" data-confirm-text></p></div>
            <div class="confirm__actions">
                <button class="btn btn--ghost" value="cancel">Cancel</button>
                <button class="btn btn--danger" value="ok" data-confirm-ok>Delete</button>
            </div>
        </form>`;
    document.body.appendChild(confirmDialog);

    document.querySelectorAll("form[data-confirm]").forEach((form) => {
        form.addEventListener("submit", (e) => {
            if (form.dataset.confirmed === "1") return;
            e.preventDefault();
            confirmDialog.querySelector("[data-confirm-text]").textContent = form.dataset.confirm;
            confirmDialog.querySelector("[data-confirm-ok]").textContent = form.dataset.confirmAction || "Confirm";
            confirmDialog.returnValue = "";
            confirmDialog.showModal();
            confirmDialog.addEventListener("close", () => {
                if (confirmDialog.returnValue !== "ok") return;
                form.dataset.confirmed = "1";
                form.requestSubmit();
            }, { once: true });
        });
    });

    // ───────────── Busy state on submit ─────────────
    document.querySelectorAll("form[data-busy-form]").forEach((form) => {
        form.addEventListener("submit", (e) => {
            if (form.dataset.processing === "1") {
                e.preventDefault();
                toast("Photos are still being prepared — one moment…");
                return;
            }
            form.querySelectorAll('button[type="submit"]').forEach((b) => b.classList.add("is-busy"));
        });
    });

    // ───────────── Position inputs: submit on change ─────────────
    document.querySelectorAll("[data-position-form]").forEach((form) => {
        const input = form.querySelector("input[name=position]");
        const original = input.value;
        input.addEventListener("change", () => {
            if (input.value && input.value !== original) form.requestSubmit();
        });
        input.addEventListener("focus", () => input.select());
    });

    // ───────────── Menus that save as soon as they change (e.g. priority) ─────────────
    document.querySelectorAll("select[data-autosubmit]").forEach((select) => {
        select.addEventListener("change", () => {
            select.form.classList.add("is-saving");
            select.form.requestSubmit();
        });
    });

    // ───────────── Filter ─────────────
    document.querySelectorAll("[data-filter]").forEach((input) => {
        const list = document.querySelector(input.dataset.filter);
        const empty = list?.parentElement.querySelector(".filter-empty");
        input.addEventListener("input", () => {
            const q = input.value.trim().toLowerCase();
            let shown = 0;
            list.querySelectorAll("[data-filter-text]").forEach((row) => {
                const match = !q || row.dataset.filterText.includes(q);
                row.hidden = !match;
                if (match) shown++;
            });
            list.classList.toggle("filtering", q.length > 0);
            if (empty) empty.hidden = shown > 0;
        });
    });

    // ───────────── Sortable lists & grids (mouse, touch and pen) ─────────────
    const flip = (container, mutate) => {
        const items = [...container.children];
        const before = new Map(items.map((el) => [el, el.getBoundingClientRect()]));
        mutate();
        for (const el of items) {
            if (el.classList.contains("is-dragging")) continue;
            const a = before.get(el);
            const b = el.getBoundingClientRect();
            const dx = a.left - b.left;
            const dy = a.top - b.top;
            if (dx || dy) {
                el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "none" }],
                    { duration: 220, easing: "cubic-bezier(.2,.75,.2,1)" });
            }
        }
    };

    const renumber = (container) => {
        const rows = [...container.querySelectorAll(":scope > [data-sort-item]")];
        rows.forEach((row, i) => {
            const pos = i + 1;
            const rank = row.querySelector("[data-rank]");
            if (rank) rank.value = pos;
            row.querySelectorAll('form:not([data-position-form]) input[name=position]').forEach((hidden, k) => {
                hidden.value = k === 0 ? pos - 1 : pos + 1;
                const btn = hidden.form.querySelector("button");
                if (btn) btn.disabled = k === 0 ? pos === 1 : pos === rows.length;
            });
        });
    };

    document.querySelectorAll("[data-sortable]").forEach((container) => {
        let dragging = null;
        let startOrder = "";
        let scrollTimer = 0;
        let lastY = 0;

        const tiered = container.hasAttribute("data-sort-tiers");
        const order = () => [...container.querySelectorAll(":scope > [data-sort-item]")].map((el) => el.dataset.id);

        // Each client's priority = the group header above it.
        const tiersOf = () => {
            const tiers = [];
            let tier = 0;
            for (const el of container.children) {
                if (el.matches("[data-sort-divider]")) tier = Number(el.dataset.priority);
                else if (el.matches("[data-sort-item]")) tiers.push(tier);
            }
            return tiers;
        };

        // Reflect the new groups in the counts and in each row's priority menu.
        const syncTiers = (tiers) => {
            const rows = [...container.querySelectorAll(":scope > [data-sort-item]")];
            rows.forEach((row, i) => {
                const select = row.querySelector("select[name=priority]");
                if (!select) return;
                select.value = String(tiers[i]);
                select.className = select.className.replace(/priority-select__input--\d/, `priority-select__input--${tiers[i]}`);
            });
            container.querySelectorAll("[data-sort-divider]").forEach((d) => {
                const count = d.querySelector("[data-tier-count]");
                if (count) count.textContent = String(tiers.filter((t) => t === Number(d.dataset.priority)).length);
            });
        };

        const autoScroll = () => {
            const edge = 90;
            if (lastY < edge) window.scrollBy(0, -12);
            else if (lastY > window.innerHeight - edge) window.scrollBy(0, 12);
        };

        container.addEventListener("pointerdown", (e) => {
            const handle = e.target.closest("[data-sort-handle]");
            if (!handle || container.classList.contains("filtering") || e.button > 0) return;
            e.preventDefault();
            dragging = handle.closest("[data-sort-item]");
            startOrder = order().join(",") + (tiered ? "|" + tiersOf().join(",") : "");
            try { handle.setPointerCapture(e.pointerId); } catch { /* capture is a nicety; dragging works without it */ }
            dragging.classList.add("is-dragging");
            container.classList.add("is-sorting");
            lastY = e.clientY;
            scrollTimer = setInterval(autoScroll, 16);
        });

        container.addEventListener("pointermove", (e) => {
            if (!dragging) return;
            lastY = e.clientY;
            // Priority group headers are drop targets too, so a client can be dragged into another group.
            const target = document.elementFromPoint(e.clientX, e.clientY)?.closest("[data-sort-item], [data-sort-divider]");
            if (!target || target === dragging || target.parentElement !== container) return;
            const children = [...container.children];
            let after = children.indexOf(target) > children.indexOf(dragging);
            if (target.matches("[data-sort-divider]") && target === container.firstElementChild) after = true;
            const reference = after ? target.nextSibling : target;
            if (reference === dragging || reference === dragging.nextSibling && !after) return;
            flip(container, () => container.insertBefore(dragging, reference));
        });

        const finish = async () => {
            if (!dragging) return;
            clearInterval(scrollTimer);
            dragging.classList.remove("is-dragging");
            container.classList.remove("is-sorting");
            dragging = null;
            const ids = order();
            const tiers = tiered ? tiersOf() : null;
            const signature = ids.join(",") + (tiers ? "|" + tiers.join(",") : "");
            if (signature === startOrder) return;
            renumber(container);
            if (tiered) syncTiers(tiers);
            try {
                const res = await fetch(container.dataset.sortUrl, {
                    method: "POST",
                    headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf },
                    body: JSON.stringify(tiered ? { ids: ids.map(Number), priorities: tiers } : { ids: ids.map(Number) }),
                    credentials: "same-origin",
                });
                const data = await res.json().catch(() => ({}));
                if (!res.ok || !data.ok) throw new Error(data.error || "The new order could not be saved.");
                toast("Order saved");
            } catch (err) {
                toast(`${err.message} Reloading…`, true);
                setTimeout(() => location.reload(), 1600);
            }
        };
        container.addEventListener("pointerup", finish);
        container.addEventListener("pointercancel", finish);
    });

    // ───────────── Image uploader: resize, re-encode (strips EXIF/GPS) and make thumbnails ─────────────
    const FULL_EDGE = 2400;
    const THUMB_EDGE = 900;
    const MAX_FILES = 30;

    const decode = async (file) => {
        if ("createImageBitmap" in window) {
            try { return await createImageBitmap(file, { imageOrientation: "from-image" }); } catch { /* fall through */ }
        }
        const url = URL.createObjectURL(file);
        try {
            const img = new Image();
            img.src = url;
            await img.decode();
            return img;
        } finally {
            URL.revokeObjectURL(url);
        }
    };

    const encode = (source, maxEdge, quality) => new Promise((resolve, reject) => {
        const w = source.width, h = source.height;
        const scale = Math.min(1, maxEdge / Math.max(w, h));
        const canvas = document.createElement("canvas");
        canvas.width = Math.round(w * scale);
        canvas.height = Math.round(h * scale);
        const ctx = canvas.getContext("2d");
        ctx.imageSmoothingQuality = "high";
        ctx.drawImage(source, 0, 0, canvas.width, canvas.height);
        canvas.toBlob((webp) => {
            if (webp && webp.type === "image/webp") return resolve(webp);
            canvas.toBlob((jpg) => (jpg ? resolve(jpg) : reject(new Error("encode failed"))), "image/jpeg", quality);
        }, "image/webp", quality);
    });

    const baseName = (name) => name.replace(/\.[^.]+$/, "") || "photo";

    document.querySelectorAll("[data-uploader]").forEach((zone) => {
        const input = zone.querySelector("[data-upload-input]");
        const thumbInput = zone.querySelector("[data-thumb-input]");
        const previews = zone.querySelector(".upload-previews");
        const form = zone.closest("form");
        const autosubmit = zone.hasAttribute("data-autosubmit");
        if (!input || typeof DataTransfer === "undefined") return;

        let busy = false;
        const process = async (files) => {
            const list = [...files].filter((f) => f.type.startsWith("image/") || /\.(jpe?g|png|webp|avif|heic)$/i.test(f.name));
            if (!list.length) return;
            if (list.length > MAX_FILES) {
                toast(`Please choose at most ${MAX_FILES} images at a time.`, true);
                input.value = "";
                return;
            }
            busy = true;
            form.dataset.processing = "1";
            previews.innerHTML = "";
            const tiles = list.map(() => {
                const tile = document.createElement("div");
                tile.className = "upload-preview is-processing";
                previews.appendChild(tile);
                return tile;
            });

            const full = new DataTransfer();
            const thumbs = new DataTransfer();
            let failed = 0;
            for (let i = 0; i < list.length; i++) {
                const file = list[i];
                try {
                    const bitmap = await decode(file);
                    const [big, small] = await Promise.all([encode(bitmap, FULL_EDGE, 0.86), encode(bitmap, THUMB_EDGE, 0.8)]);
                    bitmap.close?.();
                    const ext = big.type === "image/webp" ? "webp" : "jpg";
                    full.items.add(new File([big], `${baseName(file.name)}.${ext}`, { type: big.type }));
                    thumbs.items.add(new File([small], `${baseName(file.name)}-thumb.${ext}`, { type: small.type }));
                    const img = document.createElement("img");
                    img.src = URL.createObjectURL(small);
                    img.alt = "";
                    tiles[i].appendChild(img);
                    tiles[i].classList.remove("is-processing");
                } catch {
                    failed++;
                    tiles[i].remove();
                }
            }
            input.files = full.files;
            if (thumbInput) thumbInput.files = thumbs.files;
            busy = false;
            delete form.dataset.processing;

            if (failed) toast(`${failed} file${failed > 1 ? "s" : ""} couldn't be read. Use JPG, PNG or WebP.`, true);
            if (autosubmit && full.files.length) {
                zone.classList.add("is-uploading");
                form.requestSubmit();
            }
        };

        input.addEventListener("change", () => { if (!busy) process(input.files); });

        ["dragenter", "dragover"].forEach((t) => zone.addEventListener(t, (e) => {
            e.preventDefault();
            zone.classList.add("is-over");
        }));
        ["dragleave", "drop"].forEach((t) => zone.addEventListener(t, (e) => {
            e.preventDefault();
            if (t === "dragleave" && zone.contains(e.relatedTarget)) return;
            zone.classList.remove("is-over");
        }));
        zone.addEventListener("drop", (e) => { if (!busy && e.dataTransfer?.files.length) process(e.dataTransfer.files); });
    });

    // ───────────── Work-entry links: add rows ─────────────
    document.querySelectorAll("[data-add-link]").forEach((button) => {
        const rows = button.parentElement.querySelector("[data-link-rows]");
        const max = Number(rows.dataset.max || 10);
        const sync = () => (button.hidden = rows.children.length >= max);
        button.addEventListener("click", () => {
            const index = rows.children.length;
            const row = rows.lastElementChild.cloneNode(true);
            row.querySelectorAll("input").forEach((el) => {
                el.value = "";
                el.classList.remove("input-validation-error");
                el.name = el.name.replace(/\[\d+\]/, `[${index}]`);
                el.id = el.id.replace(/_\d+__/, `_${index}__`);
            });
            row.querySelectorAll(".field__error").forEach((el) => (el.textContent = ""));
            rows.appendChild(row);
            row.querySelector("input").focus();
            sync();
        });
        sync();
    });
})();
