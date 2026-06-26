document.addEventListener('DOMContentLoaded', () => {
  renderDocsPage();
  initSidebarSearch();
  initSidebarCollapse();
  initTOCAndScrollspy();
  initCodeCopyButtons();
  initHeadingAnchors();
  initMobileMenu();
});

function currentDocPath() {
  let path = window.location.pathname.replace(/\/$/, '');
  if (!path || path === '/docs') {
    return '/docs/product-overview.html';
  }
  if (!path.endsWith('.html')) {
    path += '.html';
  }
  return path;
}

function make(tag, className, text) {
  const el = document.createElement(tag);
  if (className) el.className = className;
  if (text !== undefined && text !== null) el.textContent = text;
  return el;
}

function renderDocsPage() {
  const data = window.YANZI_DOCS;
  if (!data) return;

  const path = currentDocPath();
  const page = data.pages[path] || data.pages['/docs/product-overview.html'];
  document.title = `${page.title} - 燕子在线文档`;

  renderDocsSidebar(data.nav, path);
  renderDocsContent(page);
}

function renderDocsSidebar(nav, activePath) {
  const sidebar = document.querySelector('.doc-sidebar');
  if (!sidebar) return;
  sidebar.textContent = '';

  const header = make('div', 'sidebar-header');
  header.appendChild(make('strong', null, '文档目录'));
  const toggle = make('button', 'sidebar-toggle-btn');
  toggle.id = 'sidebar-toggle';
  toggle.type = 'button';
  toggle.title = '折叠侧边栏';
  toggle.textContent = '‹';
  header.appendChild(toggle);
  sidebar.appendChild(header);

  const searchWrap = make('div', 'sidebar-search-container');
  const input = make('input', 'sidebar-search-input');
  input.id = 'directory-search';
  input.placeholder = '搜索目录... (/)';
  input.type = 'text';
  searchWrap.appendChild(input);
  sidebar.appendChild(searchWrap);

  nav.forEach(group => {
    const groupEl = make('div', 'doc-toc-group');
    groupEl.appendChild(make('span', 'doc-toc-title', group.group));
    const links = make('div', 'doc-toc-links');
    group.items.forEach((item, index) => {
      const a = make('a', item.path === activePath ? 'active' : '', `${index + 1}. ${item.title}`);
      a.href = item.path;
      links.appendChild(a);
    });
    groupEl.appendChild(links);
    sidebar.appendChild(groupEl);
  });
}

function renderDocsContent(page) {
  const stack = document.querySelector('.guide-stack');
  if (!stack) return;
  stack.textContent = '';

  const head = make('div', 'doc-content-header');
  const h1 = make('h1', null, page.title);
  const desc = make('p', null, page.description);
  head.appendChild(h1);
  head.appendChild(desc);
  stack.appendChild(head);

  page.sections.forEach((section, index) => {
    const article = make('article', 'guide-card');
    const h2 = make('h2', null, section.title);
    h2.id = `section-${index + 1}`;
    article.appendChild(h2);

    if (section.body) {
      section.body.forEach(text => article.appendChild(make('p', null, text)));
    }

    if (section.cards) {
      const grid = make('div', 'guide-grid');
      section.cards.forEach(card => {
        const mini = make('div', 'guide-mini');
        mini.appendChild(make('strong', null, card[0]));
        mini.appendChild(make('span', null, card[1]));
        grid.appendChild(mini);
      });
      article.appendChild(grid);
    }

    stack.appendChild(article);
  });
}

function initSidebarSearch() {
  const searchInput = document.getElementById('directory-search');
  if (!searchInput) return;
  searchInput.addEventListener('input', (e) => {
    const query = e.target.value.toLowerCase().trim();
    document.querySelectorAll('.doc-toc-links a').forEach(link => {
      const visible = link.textContent.toLowerCase().includes(query);
      link.style.display = visible ? '' : 'none';
    });
    document.querySelectorAll('.doc-toc-group').forEach(group => {
      const visibleLinks = group.querySelectorAll('.doc-toc-links a:not([style*="display: none"])');
      group.style.display = visibleLinks.length ? '' : 'none';
    });
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === '/' && document.activeElement !== searchInput) {
      e.preventDefault();
      searchInput.focus();
    }
  });
}

function initSidebarCollapse() {
  const sidebar = document.querySelector('.doc-sidebar');
  const toggleBtn = document.getElementById('sidebar-toggle');
  const expandBtn = document.getElementById('sidebar-expand-btn');
  const docLayout = document.querySelector('.doc-layout');
  if (!sidebar || !docLayout) return;

  if (localStorage.getItem('doc-sidebar-collapsed') === 'true' && window.innerWidth > 900) {
    sidebar.classList.add('collapsed');
    docLayout.classList.add('sidebar-collapsed');
    if (expandBtn) expandBtn.classList.add('visible');
  }

  if (toggleBtn) toggleBtn.addEventListener('click', () => {
    sidebar.classList.add('collapsed');
    docLayout.classList.add('sidebar-collapsed');
    if (expandBtn) expandBtn.classList.add('visible');
    localStorage.setItem('doc-sidebar-collapsed', 'true');
  });

  if (expandBtn) expandBtn.addEventListener('click', () => {
    sidebar.classList.remove('collapsed');
    docLayout.classList.remove('sidebar-collapsed');
    expandBtn.classList.remove('visible');
    localStorage.setItem('doc-sidebar-collapsed', 'false');
  });
}

function initTOCAndScrollspy() {
  const outlineList = document.getElementById('outline-list');
  const content = document.querySelector('.guide-stack');
  if (!outlineList || !content) return;
  outlineList.textContent = '';
  const headings = content.querySelectorAll('article h2, article h3');
  if (!headings.length) {
    const parent = outlineList.closest('.doc-outline');
    if (parent) parent.style.display = 'none';
    return;
  }
  headings.forEach((heading, index) => {
    if (!heading.id) heading.id = `heading-${index}`;
    const li = make('li', heading.tagName.toLowerCase() === 'h3' ? 'toc-item depth-3' : 'toc-item depth-2');
    const a = make('a', null, heading.innerText.replace(/#$/, '').trim());
    a.href = `#${heading.id}`;
    a.addEventListener('click', (e) => {
      e.preventDefault();
      const topOffset = heading.getBoundingClientRect().top + window.scrollY - 90;
      window.scrollTo({ top: topOffset, behavior: 'smooth' });
      history.pushState(null, null, `#${heading.id}`);
    });
    li.appendChild(a);
    outlineList.appendChild(li);
  });
}

function initCodeCopyButtons() {
  document.querySelectorAll('pre code').forEach(code => {
    const pre = code.parentElement;
    if (!pre || pre.tagName.toLowerCase() !== 'pre') return;
    pre.style.position = 'relative';
    const copyBtn = make('button', 'copy-code-btn', '复制');
    copyBtn.type = 'button';
    copyBtn.addEventListener('click', () => {
      navigator.clipboard.writeText(code.innerText).then(() => {
        copyBtn.textContent = '已复制!';
        setTimeout(() => { copyBtn.textContent = '复制'; }, 2000);
      });
    });
    pre.appendChild(copyBtn);
  });
}

function initHeadingAnchors() {
  document.querySelectorAll('.guide-stack article h2, .guide-stack article h3').forEach(heading => {
    if (!heading.id) return;
    const anchor = make('a', 'heading-anchor-link', '#');
    anchor.href = `#${heading.id}`;
    heading.appendChild(anchor);
  });
}

function initMobileMenu() {
  const sidebar = document.querySelector('.doc-sidebar');
  if (!sidebar || document.querySelector('.mobile-menu-btn')) return;
  const menuBtn = make('button', 'mobile-menu-btn', '☰');
  const overlay = make('div', 'mobile-menu-overlay');
  document.body.appendChild(menuBtn);
  document.body.appendChild(overlay);
  menuBtn.addEventListener('click', () => {
    sidebar.classList.toggle('mobile-open');
    overlay.classList.toggle('visible');
  });
  overlay.addEventListener('click', () => {
    sidebar.classList.remove('mobile-open');
    overlay.classList.remove('visible');
  });
}
