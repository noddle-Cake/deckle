// Header state on scroll.
const header = document.getElementById("site-header");
const onScroll = () => header.toggleAttribute("data-scrolled", window.scrollY > 8);
addEventListener("scroll", onScroll, { passive: true });
onScroll();

// The look tour: tabs swap the screenshot; arrow keys move between tabs.
const tabs = [...document.querySelectorAll('[role="tab"]')];
const shot = document.getElementById("tour-shot");
const panel = document.getElementById("tour-panel");

function select(tab, focus) {
  for (const other of tabs) {
    const on = other === tab;
    other.setAttribute("aria-selected", on);
    other.tabIndex = on ? 0 : -1;
  }
  shot.src = `assets/screens/${tab.dataset.shot}.png`;
  shot.alt = tab.dataset.alt;
  panel.setAttribute("aria-labelledby", tab.id);
  if (focus) tab.focus();
}

tabs.forEach((tab, i) => {
  tab.addEventListener("click", () => select(tab));
  tab.addEventListener("keydown", (e) => {
    const step = { ArrowRight: 1, ArrowLeft: -1 }[e.key];
    if (step) {
      e.preventDefault();
      select(tabs[(i + step + tabs.length) % tabs.length], true);
    }
  });
});

// Point the hero button at this visitor's system. macOS and Linux have two builds, so those go to the download section.
const agent = navigator.userAgent;
const os = /Windows/.test(agent) ? "windows" : /Mac OS X/.test(agent) && !/iPhone|iPad/.test(agent) ? "macos"
  : /Linux/.test(agent) && !/Android/.test(agent) ? "linux" : null;
if (os) {
  const card = document.querySelector(`.dl[data-os="${os}"]`);
  card.toggleAttribute("data-current", true);
  const hero = document.getElementById("hero-download");
  hero.textContent = `Download for ${card.querySelector("h3").textContent}`;
  if (os === "windows") hero.href = card.querySelector("a").href;
}
