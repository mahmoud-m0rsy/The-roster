// Loaded synchronously in <head> so reveal animations start hidden without a flash of content.
document.documentElement.classList.add("js");

// Apply the visitor's saved light/dark choice before the first paint (light is the default).
try {
    if (localStorage.getItem("theme") === "dark") document.documentElement.dataset.theme = "dark";
} catch { /* storage unavailable: stay light */ }
