// OmniCard help viewer — builds the sidebar TOC from the page, does scroll-spy
// active-section highlighting, and client-side full-text search over the sections.
(function () {
  "use strict";

  const sections = Array.from(document.querySelectorAll("main > section"));
  const toc = document.getElementById("toc");
  const content = document.getElementById("content");
  const search = document.getElementById("search");
  const searchMeta = document.getElementById("search-meta");
  const noResults = document.getElementById("no-results");

  // Cache each section's searchable text and its nav link.
  const entries = sections.map((sec) => {
    const h2 = sec.querySelector("h2");
    return {
      id: sec.id,
      section: sec,
      title: h2 ? h2.textContent.trim() : sec.id,
      group: sec.dataset.group || "Other",
      text: sec.textContent.toLowerCase(),
      link: null,
    };
  });

  // ---- Build grouped TOC (preserving first-seen group order) ----
  const groups = [];
  const groupMap = new Map();
  for (const e of entries) {
    if (!groupMap.has(e.group)) {
      const g = { name: e.group, items: [] };
      groupMap.set(e.group, g);
      groups.push(g);
    }
    groupMap.get(e.group).items.push(e);
  }

  for (const g of groups) {
    const groupEl = document.createElement("div");
    groupEl.className = "toc-group";
    const titleEl = document.createElement("div");
    titleEl.className = "toc-group-title";
    titleEl.textContent = g.name;
    groupEl.appendChild(titleEl);
    for (const e of g.items) {
      const a = document.createElement("a");
      a.href = "#" + e.id;
      a.textContent = e.title;
      a.dataset.target = e.id;
      groupEl.appendChild(a);
      e.link = a;
      e.groupTitleEl = titleEl;
    }
    toc.appendChild(groupEl);
  }

  // ---- Scroll-spy: highlight the section nearest the top ----
  function updateActive() {
    let currentId = entries.length ? entries[0].id : null;
    const top = content.scrollTop + 90;
    for (const e of entries) {
      if (e.section.offsetTop <= top) currentId = e.id;
    }
    for (const e of entries) {
      if (e.link) e.link.classList.toggle("active", e.id === currentId);
    }
  }
  content.addEventListener("scroll", () => window.requestAnimationFrame(updateActive));

  // Smooth in-page nav without changing the URL host.
  toc.addEventListener("click", (ev) => {
    const a = ev.target.closest("a[data-target]");
    if (!a) return;
    ev.preventDefault();
    const target = document.getElementById(a.dataset.target);
    if (target) target.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  // ---- Search ----
  function clearHighlights() {
    content.querySelectorAll("mark[data-search]").forEach((m) => {
      const parent = m.parentNode;
      parent.replaceChild(document.createTextNode(m.textContent), m);
      parent.normalize();
    });
  }

  function highlightIn(section, query) {
    const walker = document.createTreeWalker(section, NodeFilter.SHOW_TEXT, {
      acceptNode: (node) => {
        if (!node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
        const tag = node.parentNode.nodeName;
        if (tag === "SCRIPT" || tag === "STYLE" || tag === "MARK") return NodeFilter.FILTER_REJECT;
        return node.nodeValue.toLowerCase().includes(query)
          ? NodeFilter.FILTER_ACCEPT
          : NodeFilter.FILTER_REJECT;
      },
    });
    const targets = [];
    while (walker.nextNode()) targets.push(walker.currentNode);
    for (const node of targets) {
      const frag = document.createDocumentFragment();
      const text = node.nodeValue;
      const lower = text.toLowerCase();
      let i = 0, idx;
      while ((idx = lower.indexOf(query, i)) !== -1) {
        if (idx > i) frag.appendChild(document.createTextNode(text.slice(i, idx)));
        const mark = document.createElement("mark");
        mark.dataset.search = "1";
        mark.textContent = text.slice(idx, idx + query.length);
        frag.appendChild(mark);
        i = idx + query.length;
      }
      if (i < text.length) frag.appendChild(document.createTextNode(text.slice(i)));
      node.parentNode.replaceChild(frag, node);
    }
  }

  function runSearch(raw) {
    const query = raw.trim().toLowerCase();
    clearHighlights();

    if (!query) {
      entries.forEach((e) => {
        e.section.classList.remove("dimmed");
        if (e.link) e.link.classList.remove("hidden");
      });
      groups.forEach((g) => (g.items[0].groupTitleEl.style.display = ""));
      searchMeta.textContent = "";
      noResults.hidden = true;
      updateActive();
      return;
    }

    let matches = 0;
    const groupHasMatch = new Map();
    for (const e of entries) {
      const hit = e.title.toLowerCase().includes(query) || e.text.includes(query);
      e.section.classList.toggle("dimmed", !hit);
      if (e.link) e.link.classList.toggle("hidden", !hit);
      if (hit) {
        matches++;
        groupHasMatch.set(e.group, true);
        highlightIn(e.section, query);
      }
    }

    // Hide group headers with no visible items.
    for (const g of groups) {
      g.items[0].groupTitleEl.style.display = groupHasMatch.get(g.name) ? "" : "none";
    }

    searchMeta.textContent = matches
      ? matches + (matches === 1 ? " section" : " sections") + " match"
      : "";
    noResults.hidden = matches !== 0;
  }

  let debounce;
  search.addEventListener("input", () => {
    clearTimeout(debounce);
    debounce = setTimeout(() => runSearch(search.value), 120);
  });
  // Esc clears the search.
  search.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      search.value = "";
      runSearch("");
    }
  });

  // ---- Auto-upgrade screenshot placeholders ----
  // Each <figure class="shot" data-img="x.png"> tries to load images/x.png; if the file
  // exists it replaces the dashed placeholder, otherwise the placeholder stays. So adding a
  // screenshot is just dropping a PNG into the Docs/images folder — no HTML edits needed.
  document.querySelectorAll("figure.shot[data-img]").forEach((fig) => {
    const name = fig.dataset.img;
    const probe = new Image();
    probe.onload = () => {
      fig.classList.add("has-image");
      fig.innerHTML = "";
      const img = document.createElement("img");
      img.src = "images/" + name;
      img.alt = name;
      fig.appendChild(img);
    };
    probe.src = "images/" + name;
  });

  updateActive();
})();
