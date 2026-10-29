(() => {
    "use strict";
    const root = document.documentElement;
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const finePointer = window.matchMedia("(hover: hover) and (pointer: fine)").matches;

    // ───────────── Header: frosted when scrolled, hides on scroll down, returns on scroll up ─────────────
    const header = document.querySelector("[data-header]");
    if (header) {
        let lastY = window.scrollY;
        let ticking = false;
        const update = () => {
            const y = window.scrollY;
            header.classList.toggle("is-scrolled", y > 12);
            const hide = y > 320 && y > lastY + 4;
            const show = y < lastY - 4 || y < 320;
            if (hide) { header.classList.add("is-hidden"); root.classList.add("header-hidden"); }
            else if (show) { header.classList.remove("is-hidden"); root.classList.remove("header-hidden"); }
            lastY = y;
            ticking = false;
        };
        update();
        window.addEventListener("scroll", () => { if (!ticking) { ticking = true; requestAnimationFrame(update); } }, { passive: true });
    }

    // ───────────── Images fade in once decoded ─────────────
    document.querySelectorAll("img[data-fade]").forEach((img) => {
        const done = () => img.classList.add("is-loaded");
        if (img.complete && img.naturalWidth) done();
        else {
            img.addEventListener("load", done, { once: true });
            img.addEventListener("error", done, { once: true });
        }
    });

    // ───────────── Reveal on scroll, staggered for items entering together ─────────────
    const reveals = document.querySelectorAll(".reveal");
    if ("IntersectionObserver" in window && reveals.length && !reduceMotion) {
        let batch = 0;
        let frame = 0;
        const io = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                entry.target.style.setProperty("--delay", `${Math.min(batch++, 8) * 80}ms`);
                entry.target.classList.add("is-visible");
                io.unobserve(entry.target);
            }
            cancelAnimationFrame(frame);
            frame = requestAnimationFrame(() => (batch = 0));
        }, { rootMargin: "0px 0px -8% 0px", threshold: 0.06 });
        reveals.forEach((el) => io.observe(el));
    } else {
        reveals.forEach((el) => el.classList.add("is-visible"));
    }

    // ───────────── Animated counters ─────────────
    document.querySelectorAll("[data-count]").forEach((el) => {
        const target = Number(el.dataset.count);
        if (!target || reduceMotion) return;
        const start = performance.now() + 500;
        const duration = 1600;
        el.textContent = "0";
        const step = (now) => {
            const t = Math.min(1, Math.max(0, (now - start) / duration));
            el.textContent = String(Math.round(target * (1 - Math.pow(1 - t, 4))));
            if (t < 1) requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    });

    // ───────────── Subtle 3D tilt on cards (mouse only) ─────────────
    if (finePointer && !reduceMotion) {
        document.querySelectorAll("[data-tilt]").forEach((card) => {
            const link = card.querySelector(".card__link");
            card.addEventListener("pointermove", (e) => {
                const r = link.getBoundingClientRect();
                const x = (e.clientX - r.left) / r.width - 0.5;
                const y = (e.clientY - r.top) / r.height - 0.5;
                card.classList.add("is-tilting");
                link.style.setProperty("--ry", `${(x * 7).toFixed(2)}deg`);
                link.style.setProperty("--rx", `${(-y * 7).toFixed(2)}deg`);
            });
            card.addEventListener("pointerleave", () => {
                card.classList.remove("is-tilting");
                link.style.removeProperty("--rx");
                link.style.removeProperty("--ry");
            });
        });

        // Hero collage drifts with the pointer for depth.
        const scene = document.querySelector("[data-parallax]");
        if (scene) {
            const layers = scene.querySelectorAll("[data-depth]");
            window.addEventListener("pointermove", (e) => {
                const x = e.clientX / window.innerWidth - 0.5;
                const y = e.clientY / window.innerHeight - 0.5;
                layers.forEach((layer) => {
                    const d = Number(layer.dataset.depth) * 10;
                    layer.style.setProperty("--tx", `${(-x * d).toFixed(1)}px`);
                    layer.style.setProperty("--ty", `${(-y * d).toFixed(1)}px`);
                });
            }, { passive: true });
        }

        // Magnetic primary buttons.
        document.querySelectorAll("[data-magnetic]").forEach((btn) => {
            btn.addEventListener("pointermove", (e) => {
                const r = btn.getBoundingClientRect();
                btn.style.transform = `translate(${(e.clientX - r.left - r.width / 2) * 0.12}px, ${(e.clientY - r.top - r.height / 2) * 0.2}px)`;
            });
            btn.addEventListener("pointerleave", () => (btn.style.transform = ""));
        });
    }

    // ───────────── Live search (suggestions as you type) ─────────────
    const searchDialog = document.querySelector("[data-search-dialog]");
    if (searchDialog && typeof searchDialog.showModal === "function") {
        const input = searchDialog.querySelector("[data-search-input]");
        const list = searchDialog.querySelector("[data-search-results]");
        const hint = searchDialog.querySelector("[data-search-hint]");
        const all = searchDialog.querySelector("[data-search-all]");
        const term = searchDialog.querySelector("[data-search-term]");
        const form = searchDialog.querySelector("[data-search-form]");
        let items = [];
        let active = -1;
        let timer = 0;
        let controller = null;
        let lastQuery = null;

        const escapeHtml = (s) => s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
        const highlight = (text, q) => {
            const i = text.toLocaleLowerCase().indexOf(q.toLocaleLowerCase());
            if (i < 0) return escapeHtml(text);
            return escapeHtml(text.slice(0, i)) + "<mark>" + escapeHtml(text.slice(i, i + q.length)) + "</mark>" + escapeHtml(text.slice(i + q.length));
        };
        const allUrl = (q) => `/?q=${encodeURIComponent(q)}#roster`;

        const setActive = (i) => {
            active = items.length ? (i + items.length) % items.length : -1;
            list.querySelectorAll(".search-result").forEach((li, n) => {
                li.classList.toggle("is-active", n === active);
                li.setAttribute("aria-selected", String(n === active));
            });
            if (active >= 0) {
                input.setAttribute("aria-activedescendant", `sr-${active}`);
                list.children[active].scrollIntoView({ block: "nearest" });
            } else input.removeAttribute("aria-activedescendant");
        };

        const render = (q, results) => {
            items = results;
            list.innerHTML = results.map((r, i) => {
                const thumb = r.thumb ? `<img src="${escapeHtml(r.thumb)}" alt="" loading="lazy" />` : escapeHtml(r.initials || "");
                const meta = [r.role, r.location].filter(Boolean).map(escapeHtml).join(" · ");
                return `<li class="search-result" id="sr-${i}" role="option" aria-selected="false" style="--i:${i}">
                    <a href="${escapeHtml(r.url)}">
                        <span class="search-result__thumb">${thumb}</span>
                        <span class="search-result__text">
                            <span class="search-result__name" dir="auto">${highlight(r.name, q)}</span>
                            ${meta ? `<span class="search-result__meta" dir="auto">${meta}</span>` : ""}
                        </span>
                        <span class="search-result__rank"><small>No.</small>${String(r.rank).padStart(2, "0")}</span>
                    </a></li>`;
            }).join("");
            input.setAttribute("aria-expanded", String(results.length > 0));
            hint.hidden = results.length > 0;
            if (!q) hint.textContent = "Type a name to find a client instantly.";
            else if (!results.length) hint.textContent = `No clients match “${q}”. Try another spelling.`;
            all.hidden = !q || !results.length;
            if (q) { term.textContent = `“${q}”`; all.href = allUrl(q); }
            setActive(results.length ? 0 : -1);
        };

        const run = async () => {
            const q = input.value.trim();
            if (q === lastQuery) return;
            lastQuery = q;
            controller?.abort();
            if (!q) { searchDialog.classList.remove("is-loading"); render("", []); return; }
            controller = new AbortController();
            searchDialog.classList.add("is-loading");
            try {
                const res = await fetch(`/api/search?q=${encodeURIComponent(q)}`, { signal: controller.signal, headers: { Accept: "application/json" } });
                if (!res.ok) throw new Error(String(res.status));
                const data = await res.json();
                if (input.value.trim() === q) render(q, data);
            } catch (err) {
                if (err.name !== "AbortError") { hint.hidden = false; hint.textContent = "Search is unavailable right now. Please try again."; }
            } finally {
                if (input.value.trim() === q) searchDialog.classList.remove("is-loading");
            }
        };

        const open = () => {
            if (searchDialog.open) return;
            searchDialog.showModal();
            document.body.style.overflow = "hidden";
            input.focus();
            input.select();
        };
        document.querySelectorAll("[data-search-open]").forEach((btn) => btn.addEventListener("click", open));
        searchDialog.querySelector("[data-search-close]").addEventListener("click", () => searchDialog.close());
        searchDialog.addEventListener("close", () => { document.body.style.overflow = ""; });
        searchDialog.addEventListener("click", (e) => { if (e.target === searchDialog) searchDialog.close(); });

        input.addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(run, 120); });
        input.addEventListener("keydown", (e) => {
            if (e.key === "ArrowDown") { e.preventDefault(); setActive(active + 1); }
            else if (e.key === "ArrowUp") { e.preventDefault(); setActive(active - 1); }
        });
        form.addEventListener("submit", (e) => {
            e.preventDefault();
            const q = input.value.trim();
            if (!q) return;
            const target = active >= 0 && items[active] ? items[active].url : allUrl(q);
            searchDialog.close();
            window.location.href = target;
        });

        document.addEventListener("keydown", (e) => {
            const typing = /input|textarea|select/i.test(document.activeElement?.tagName) || document.activeElement?.isContentEditable;
            if ((e.key === "/" && !typing) || ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k")) {
                e.preventDefault();
                open();
            }
        });
    }

    // ───────────── Back to top ─────────────
    document.querySelector("[data-scroll-top]")?.addEventListener("click", () => window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" }));

    // ───────────── Profile: section nav with sliding pill + scroll spy ─────────────
    const subnav = document.querySelector("[data-subnav]");
    if (subnav) {
        const pill = subnav.querySelector("[data-subnav-pill]");
        const links = [...subnav.querySelectorAll("a")];
        const sections = links.map((a) => document.querySelector(a.getAttribute("href"))).filter(Boolean);
        const movePill = (link) => {
            links.forEach((a) => a.classList.toggle("is-active", a === link));
            pill.style.left = `${link.offsetLeft}px`;
            pill.style.width = `${link.offsetWidth}px`;
        };
        const spy = () => {
            const line = window.innerHeight * 0.35;
            let current = links[0];
            sections.forEach((s, i) => { if (s.getBoundingClientRect().top < line) current = links[i]; });
            if (!current.classList.contains("is-active") || !pill.style.width) movePill(current);
        };
        document.fonts?.ready.then(spy);
        spy();
        window.addEventListener("scroll", spy, { passive: true });
        window.addEventListener("resize", () => movePill(subnav.querySelector("a.is-active") || links[0]));
    }

    // ───────────── Profile: timeline progress line ─────────────
    const timeline = document.querySelector("[data-timeline]");
    const progress = timeline?.querySelector("[data-timeline-progress]");
    if (timeline && progress) {
        const update = () => {
            const r = timeline.getBoundingClientRect();
            const seen = Math.min(Math.max(window.innerHeight * 0.7 - r.top, 0), r.height);
            progress.style.setProperty("--progress", `${(seen / r.height) * 100}%`);
        };
        update();
        window.addEventListener("scroll", update, { passive: true });
    }

    // ───────────── Copy-to-clipboard ─────────────
    const toast = (text) => {
        document.querySelector(".pop-toast")?.remove();
        const el = Object.assign(document.createElement("div"), { className: "pop-toast", textContent: text });
        el.setAttribute("role", "status");
        document.body.appendChild(el);
        setTimeout(() => el.remove(), 2200);
    };
    document.querySelectorAll("[data-copy]").forEach((btn) => {
        btn.addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(btn.dataset.copy);
                btn.classList.add("is-copied");
                btn.textContent = "Copied";
                toast("Copied to clipboard");
                setTimeout(() => { btn.classList.remove("is-copied"); btn.textContent = "Copy"; }, 1800);
            } catch {
                toast("Couldn't copy — select the text instead");
            }
        });
    });

    // ───────────── Lightbox ─────────────
    const triggers = document.querySelectorAll("[data-lightbox]");
    if (!triggers.length || typeof HTMLDialogElement === "undefined") return;

    const dialog = document.createElement("dialog");
    dialog.className = "lightbox";
    dialog.setAttribute("aria-label", "Photo viewer");
    dialog.innerHTML = `
        <div class="lightbox__bar">
            <span data-lb-count></span>
            <button class="lightbox__btn" type="button" data-lb-close aria-label="Close"><svg class="icon"><use href="#i-close"/></svg></button>
        </div>
        <div class="lightbox__stage">
            <button class="lightbox__btn lightbox__nav lightbox__nav--prev" type="button" data-lb-prev aria-label="Previous photo"><svg class="icon"><use href="#i-arrow-left"/></svg></button>
            <img alt="" data-lb-img />
            <button class="lightbox__btn lightbox__nav lightbox__nav--next" type="button" data-lb-next aria-label="Next photo"><svg class="icon"><use href="#i-arrow-right"/></svg></button>
        </div>
        <p class="lightbox__caption" data-lb-caption></p>`;
    document.body.appendChild(dialog);

    const img = dialog.querySelector("[data-lb-img]");
    const count = dialog.querySelector("[data-lb-count]");
    const caption = dialog.querySelector("[data-lb-caption]");
    const prev = dialog.querySelector("[data-lb-prev]");
    const next = dialog.querySelector("[data-lb-next]");
    let group = [];
    let index = 0;

    const show = (i) => {
        index = (i + group.length) % group.length;
        const item = group[index];
        img.style.animation = "none";
        void img.offsetWidth;
        img.style.animation = "";
        img.src = item.href;
        img.alt = item.dataset.caption || "";
        caption.textContent = item.dataset.caption || "";
        count.textContent = group.length > 1 ? `${index + 1} / ${group.length}` : "";
        prev.hidden = next.hidden = group.length < 2;
        if (group.length > 1) new Image().src = group[(index + 1) % group.length].href;
    };

    triggers.forEach((el) => el.addEventListener("click", (e) => {
        if (e.metaKey || e.ctrlKey || e.shiftKey) return;
        e.preventDefault();
        group = [...document.querySelectorAll(`[data-lightbox="${el.dataset.lightbox}"]`)];
        show(group.indexOf(el));
        dialog.showModal();
        document.body.style.overflow = "hidden";
    }));

    dialog.addEventListener("close", () => { document.body.style.overflow = ""; img.removeAttribute("src"); });
    dialog.querySelector("[data-lb-close]").addEventListener("click", () => dialog.close());
    prev.addEventListener("click", () => show(index - 1));
    next.addEventListener("click", () => show(index + 1));
    dialog.addEventListener("click", (e) => { if (e.target.classList.contains("lightbox__stage")) dialog.close(); });
    dialog.addEventListener("keydown", (e) => {
        if (e.key === "ArrowLeft") show(index - 1);
        if (e.key === "ArrowRight") show(index + 1);
    });

    let startX = null;
    dialog.addEventListener("touchstart", (e) => { startX = e.touches[0].clientX; }, { passive: true });
    dialog.addEventListener("touchend", (e) => {
        if (startX === null || group.length < 2) return;
        const dx = e.changedTouches[0].clientX - startX;
        if (Math.abs(dx) > 50) show(index + (dx < 0 ? 1 : -1));
        startX = null;
    });
})();
