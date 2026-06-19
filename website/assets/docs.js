document.addEventListener('DOMContentLoaded', () => {
  initSidebarSearch();
  initSidebarCollapse();
  initTOCAndScrollspy();
  initCodeCopyButtons();
  initHeadingAnchors();
  initMobileMenu();
});

/**
 * 1. 左侧文档目录搜索过滤
 */
function initSidebarSearch() {
  const searchInput = document.getElementById('directory-search');
  if (!searchInput) return;

  searchInput.addEventListener('input', (e) => {
    const query = e.target.value.toLowerCase().trim();
    const links = document.querySelectorAll('.doc-toc-links a');
    
    links.forEach(link => {
      const text = link.textContent.toLowerCase();
      if (text.includes(query)) {
        link.style.display = '';
        const group = link.closest('.doc-toc-group');
        if (group) group.style.display = '';
      } else {
        link.style.display = 'none';
      }
    });

    const groups = document.querySelectorAll('.doc-toc-group');
    groups.forEach(group => {
      const visibleLinks = group.querySelectorAll('.doc-toc-links a:not([style*="display: none"])');
      if (visibleLinks.length === 0) {
        group.style.display = 'none';
      } else {
        group.style.display = '';
      }
    });
  });

  // 支持快捷键 / 聚焦搜索框
  document.addEventListener('keydown', (e) => {
    if (e.key === '/' && document.activeElement !== searchInput) {
      e.preventDefault();
      searchInput.focus();
    }
  });
}

/**
 * 2. 侧边栏折叠/展开逻辑（支持 localStorage 记忆）
 */
function initSidebarCollapse() {
  const sidebar = document.querySelector('.doc-sidebar');
  const toggleBtn = document.getElementById('sidebar-toggle');
  const expandBtn = document.getElementById('sidebar-expand-btn');
  const docLayout = document.querySelector('.doc-layout');
  if (!sidebar || !docLayout) return;

  // 从本地存储读取折叠状态
  const isCollapsed = localStorage.getItem('doc-sidebar-collapsed') === 'true';
  if (isCollapsed && window.innerWidth > 900) {
    sidebar.classList.add('collapsed');
    docLayout.classList.add('sidebar-collapsed');
    if (expandBtn) expandBtn.classList.add('visible');
  }

  // 折叠按钮点击事件
  if (toggleBtn) {
    toggleBtn.addEventListener('click', () => {
      sidebar.classList.add('collapsed');
      docLayout.classList.add('sidebar-collapsed');
      if (expandBtn) expandBtn.classList.add('visible');
      localStorage.setItem('doc-sidebar-collapsed', 'true');
    });
  }

  // 展开悬浮按钮点击事件
  if (expandBtn) {
    expandBtn.addEventListener('click', () => {
      sidebar.classList.remove('collapsed');
      docLayout.classList.remove('sidebar-collapsed');
      expandBtn.classList.remove('visible');
      localStorage.setItem('doc-sidebar-collapsed', 'false');
    });
  }
}

/**
 * 3. 动态生成右侧大纲（TOC）与滚动监听（Scrollspy）
 */
function initTOCAndScrollspy() {
  const outlineList = document.getElementById('outline-list');
  const content = document.querySelector('.guide-stack');
  if (!outlineList || !content) return;

  // 1. 获取文章正文中的 h2 和 h3 标题
  const headings = content.querySelectorAll('article h2, article h3');
  if (headings.length === 0) {
    const parent = outlineList.closest('.doc-outline');
    if (parent) parent.style.display = 'none';
    return;
  }

  // 2. 动态构建 TOC 树
  headings.forEach((heading, index) => {
    if (!heading.id) {
      heading.id = `heading-${index}`;
    }

    const li = document.createElement('li');
    li.className = heading.tagName.toLowerCase() === 'h3' ? 'toc-item depth-3' : 'toc-item depth-2';
    
    const a = document.createElement('a');
    a.href = `#${heading.id}`;
    a.textContent = heading.innerText.replace(/#$/, '').trim();
    
    a.addEventListener('click', (e) => {
      e.preventDefault();
      const target = document.getElementById(heading.id);
      if (target) {
        const topOffset = target.getBoundingClientRect().top + window.scrollY - 90;
        window.scrollTo({
          top: topOffset,
          behavior: 'smooth'
        });
        history.pushState(null, null, `#${heading.id}`);
      }
    });

    li.appendChild(a);
    outlineList.appendChild(li);
  });

  // 3. 滚动监听实现 (Scrollspy)
  const tocLinks = outlineList.querySelectorAll('a');
  
  function updateActiveHeading() {
    const scrollPosition = window.scrollY + 120;
    let activeId = null;

    for (let i = 0; i < headings.length; i++) {
      const heading = headings[i];
      const top = heading.getBoundingClientRect().top + window.scrollY;
      
      if (scrollPosition >= top) {
        activeId = heading.id;
      }
    }

    if (!activeId && headings.length > 0 && scrollPosition < (headings[0].getBoundingClientRect().top + window.scrollY)) {
      activeId = headings[0].id;
    }

    tocLinks.forEach(link => {
      const href = link.getAttribute('href').substring(1);
      if (href === activeId) {
        link.classList.add('active');
        const li = link.parentElement;
        const container = outlineList.parentElement;
        const offsetTop = li.offsetTop - container.offsetTop;
        if (offsetTop < container.scrollTop || offsetTop > container.scrollTop + container.clientHeight) {
          container.scrollTo({ top: offsetTop - 40, behavior: 'smooth' });
        }
      } else {
        link.classList.remove('active');
      }
    });
  }

  window.addEventListener('scroll', updateActiveHeading);
  window.addEventListener('resize', updateActiveHeading);
  updateActiveHeading();
}

/**
 * 4. 为代码块动态生成复制按钮与语言标签
 */
function initCodeCopyButtons() {
  const codeBlocks = document.querySelectorAll('pre code');
  
  codeBlocks.forEach(code => {
    const pre = code.parentElement;
    if (pre.tagName.toLowerCase() !== 'pre') return;
    
    pre.style.position = 'relative';

    let lang = 'CODE';
    const classes = Array.from(code.classList);
    const langClass = classes.find(c => c.startsWith('language-') || c.startsWith('lang-'));
    if (langClass) {
      lang = langClass.replace(/^(language-|lang-)/, '').toUpperCase();
    } else {
      const text = code.textContent.trim();
      if (text.startsWith('{') && text.endsWith('}')) {
        lang = 'JSON';
      }
    }

    const langBadge = document.createElement('span');
    langBadge.className = 'code-lang-badge';
    langBadge.textContent = lang;
    pre.appendChild(langBadge);

    const copyBtn = document.createElement('button');
    copyBtn.className = 'copy-code-btn';
    copyBtn.type = 'button';
    copyBtn.innerHTML = `
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
        <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
      </svg>
      <span>复制</span>
    `;

    copyBtn.addEventListener('click', () => {
      const textToCopy = code.innerText;
      
      navigator.clipboard.writeText(textToCopy).then(() => {
        copyBtn.classList.add('copied');
        copyBtn.querySelector('span').textContent = '已复制!';
        setTimeout(() => {
          copyBtn.classList.remove('copied');
          copyBtn.querySelector('span').textContent = '复制';
        }, 2000);
      }).catch(err => {
        console.error('复制失败: ', err);
        copyBtn.querySelector('span').textContent = '失败';
      });
    });

    pre.appendChild(copyBtn);
  });
}

/**
 * 5. 标题 Hover 锚点链接支持
 */
function initHeadingAnchors() {
  const content = document.querySelector('.guide-stack');
  if (!content) return;

  const headings = content.querySelectorAll('article h2, article h3');
  headings.forEach(heading => {
    if (!heading.id) return;
    
    const anchor = document.createElement('a');
    anchor.className = 'heading-anchor-link';
    anchor.href = `#${heading.id}`;
    anchor.title = '复制该章节的直接链接';
    anchor.innerHTML = '#';
    
    anchor.addEventListener('click', (e) => {
      e.preventDefault();
      const fullUrl = window.location.origin + window.location.pathname + `#${heading.id}`;
      
      navigator.clipboard.writeText(fullUrl).then(() => {
        const tooltip = document.createElement('span');
        tooltip.className = 'anchor-copied-tooltip';
        tooltip.textContent = '链接已复制!';
        heading.appendChild(tooltip);
        
        setTimeout(() => {
          tooltip.remove();
        }, 1500);
        
        const topOffset = heading.getBoundingClientRect().top + window.scrollY - 90;
        window.scrollTo({ top: topOffset, behavior: 'smooth' });
        history.pushState(null, null, `#${heading.id}`);
      });
    });

    heading.appendChild(anchor);
  });
}

/**
 * 6. 移动端侧边栏抽屉与悬浮按钮
 */
function initMobileMenu() {
  const sidebar = document.querySelector('.doc-sidebar');
  if (!sidebar) return;

  // 创建悬浮菜单按钮
  const menuBtn = document.createElement('button');
  menuBtn.className = 'mobile-menu-btn';
  menuBtn.innerHTML = `
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <line x1="3" y1="12" x2="21" y2="12"></line>
      <line x1="3" y1="6" x2="21" y2="6"></line>
      <line x1="3" y1="18" x2="21" y2="18"></line>
    </svg>
  `;
  document.body.appendChild(menuBtn);

  // 创建暗色虚化背景遮罩
  const overlay = document.createElement('div');
  overlay.className = 'mobile-menu-overlay';
  document.body.appendChild(overlay);

  // 绑定点击事件，控制显示/隐藏
  menuBtn.addEventListener('click', () => {
    sidebar.classList.toggle('mobile-open');
    overlay.classList.toggle('visible');
  });

  overlay.addEventListener('click', () => {
    sidebar.classList.remove('mobile-open');
    overlay.classList.remove('visible');
  });

  // 点击任何一个侧边栏链接后自动收起
  const sidebarLinks = sidebar.querySelectorAll('a');
  sidebarLinks.forEach(link => {
    link.addEventListener('click', () => {
      sidebar.classList.remove('mobile-open');
      overlay.classList.remove('visible');
    });
  });
}
