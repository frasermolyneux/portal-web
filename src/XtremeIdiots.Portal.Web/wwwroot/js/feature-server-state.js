(() => {
    "use strict";
    document.querySelectorAll("[data-feature-server-state]").forEach(section => {
        const select = section.querySelector("select");
        const output = section.querySelector("[data-effective-feature-state]");
        if (!select || !output) return;
        const update = () => {
            const enabled = select.value === "" ? section.dataset.globalEnabled === "true" : select.value === "true";
            output.textContent = `Effective state: ${enabled ? "Enabled" : "Disabled"}.`;
        };
        select.addEventListener("change", update);
        update();
    });
})();
