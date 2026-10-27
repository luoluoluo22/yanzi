/**
 * 燕子网页小程序统一生命周期与运行时桥接基座 (Yanzi WebApp Bridge SDK)
 *
 * 核心职责：
 * 1. 统一管理小程序的生命周期（activate / deactivate / settingsChange）；
 * 2. 自动追踪并安全清理全部副作用（DOM 元素、定时器、事件监听、DOM 观察者）；
 * 3. 自动响应扩展管理界面的即时启停命令（无需刷新页面，热插拔秒级生效）；
 * 4. 自动处理与燕子主程序及云端的配置存储双向同步。
 */

(() => {
  'use strict';

  if (globalThis.__yanziWebAppBridgeLoaded) return;
  globalThis.__yanziWebAppBridgeLoaded = true;

  const INSTALLED_STORAGE_KEY = 'yanziWebApps.installed';
  const DATA_PREFIX = 'yanziWebAppData::';

  // 注册表，存储当前页面上下文中的所有小程序控制器
  const webAppRegistry = new Map();

  /**
   * 小程序执行上下文类 (ExecutionContext)
   * 负责代理配置读取、自动追踪所有副作用并在注销时执行彻底清理
   */
  class WebAppExecutionContext {
    constructor(definition) {
      this.id = definition.id;
      this.name = definition.name || definition.id;
      this.version = definition.version || '0.1.0';
      this.defaultSettings = { ...(definition.defaultSettings || {}) };
      this.settings = { ...this.defaultSettings };
      this.syncKeys = Array.isArray(definition.syncKeys) ? definition.syncKeys : ['settings'];
      this.definition = definition;
      this.isActive = false;

      // 副作用追踪池
      this._elements = new Set();
      this._timers = new Set();
      this._listeners = [];
      this._observers = new Set();
      this._disposables = new Set();
    }

    /**
     * 登记由当前小程序创建的 DOM 元素
     * 当小程序被停用（未启用）时，SDK 会自动从 DOM 中彻底移除该元素
     */
    registerElement(element) {
      if (element && typeof element.remove === 'function') {
        this._elements.add(element);
      }
      return element;
    }

    /**
     * 安全定时器（替代全局 setInterval）
     * 小程序停用时自动 clearInterval，杜绝后台游离跑死循环
     */
    setInterval(callback, delayMs) {
      const id = setInterval(callback, delayMs);
      this._timers.add(id);
      return id;
    }

    /**
     * 安全延迟器（替代全局 setTimeout）
     * 小程序停用时自动 clearTimeout
     */
    setTimeout(callback, delayMs) {
      const id = setTimeout(() => {
        this._timers.delete(id);
        try { callback(); } catch (err) { console.error(`[${this.id}] setTimeout error:`, err); }
      }, delayMs);
      this._timers.add(id);
      return id;
    }

    clearInterval(timerId) {
      clearInterval(timerId);
      this._timers.delete(timerId);
    }

    clearTimeout(timerId) {
      clearTimeout(timerId);
      this._timers.delete(timerId);
    }

    /**
     * 安全事件监听（替代 target.addEventListener）
     * 小程序停用时自动 removeEventListener，防止内存泄露与事件重入
     */
    addEventListener(target, type, listener, options) {
      if (!target || typeof target.addEventListener !== 'function') return;
      target.addEventListener(type, listener, options);
      this._listeners.push({ target, type, listener, options });
    }

    /**
     * 安全 DOM 观察者
     * 小程序停用时自动 disconnect
     */
    createObserver(callback) {
      const observer = new MutationObserver(callback);
      this._observers.add(observer);
      return observer;
    }

    /**
     * 注册自定义清理回调
     */
    onDispose(disposeFn) {
      if (typeof disposeFn === 'function') {
        this._disposables.add(disposeFn);
      }
    }

    /**
     * 更新配置（持久化至 local storage，并通知后台触发燕子云同步）
     */
    async updateSettings(partialOrFull) {
      this.settings = { ...this.settings, ...(partialOrFull || {}) };
      const storageKey = `${DATA_PREFIX}${this.id}::settings`;

      try {
        await chrome.storage.local.set({ [storageKey]: this.settings });
        chrome.runtime.sendMessage({
          type: 'yanzi_webapp_storage_set',
          appId: this.id,
          key: 'settings',
          value: this.settings
        }).catch(() => {});
      } catch (err) {
        console.warn(`[${this.id}] 保存配置失败:`, err);
      }

      try {
        this.definition.onSettingsChanged?.(this.settings, this);
      } catch (err) {
        console.error(`[${this.id}] onSettingsChanged 回调异常:`, err);
      }
    }

    /**
     * 读取配置（合并默认值）
     */
    async loadSettings() {
      const storageKey = `${DATA_PREFIX}${this.id}::settings`;
      const legacyKey = `yanzi.${this.id}.settings.v1`;
      try {
        const stored = await chrome.storage.local.get([storageKey, legacyKey]);
        const value = stored[storageKey] || stored[legacyKey];
        if (value && typeof value === 'object') {
          this.settings = { ...this.defaultSettings, ...value };
        } else {
          this.settings = { ...this.defaultSettings };
        }
      } catch {
        this.settings = { ...this.defaultSettings };
      }
      return this.settings;
    }

    /**
     * 激活小程序
     */
    async activate() {
      if (this.isActive) return;
      this.isActive = true;
      await this.loadSettings();
      try {
        await this.definition.onActivate?.(this);
      } catch (err) {
        console.error(`[${this.id}] onActivate 激活异常:`, err);
      }
    }

    /**
     * 停用小程序（全量自动清理所有副作用）
     */
    deactivate() {
      if (!this.isActive) return;
      this.isActive = false;

      // 1. 调用业务自定义销毁逻辑
      try {
        this.definition.onDeactivate?.(this);
      } catch (err) {
        console.error(`[${this.id}] onDeactivate 回调异常:`, err);
      }

      // 2. 自动清理 registered elements
      for (const el of this._elements) {
        try {
          if (el && typeof el.remove === 'function') el.remove();
        } catch {}
      }
      this._elements.clear();

      // 3. 自动清理所有定时器
      for (const id of this._timers) {
        try {
          clearInterval(id);
          clearTimeout(id);
        } catch {}
      }
      this._timers.clear();

      // 4. 自动解绑所有事件监听
      for (const item of this._listeners) {
        try {
          item.target.removeEventListener(item.type, item.listener, item.options);
        } catch {}
      }
      this._listeners = [];

      // 5. 自动断开所有观察者
      for (const obs of this._observers) {
        try {
          obs.disconnect();
        } catch {}
      }
      this._observers.clear();

      // 6. 执行自定义 dispose 回调
      for (const fn of this._disposables) {
        try {
          fn();
        } catch {}
      }
      this._disposables.clear();
    }
  }

  /**
   * 标准化注册函数：定义并启动一个网页小程序
   * @param {Object} definition - 小程序声明规范对象
   */
  function defineYanziWebApp(definition) {
    if (!definition || !definition.id) {
      throw new Error('[YanziWebAppBridge] 小程序声明必须包含合法 id');
    }

    // 单例保护：如果当前页面已经注册过该 ID，直接复用已有实例
    let context = webAppRegistry.get(definition.id);
    if (!context) {
      context = new WebAppExecutionContext(definition);
      webAppRegistry.set(definition.id, context);
    } else {
      // 更新定义引用
      context.definition = definition;
    }

    // 初始状态检测：检查当前小程序是否在管理界面中被启用
    chrome.storage.local.get([INSTALLED_STORAGE_KEY]).then(data => {
      const installedMap = data?.[INSTALLED_STORAGE_KEY] || {};
      const isInstalled = typeof installedMap[definition.id] === 'boolean'
        ? installedMap[definition.id]
        : Boolean(definition.defaultInstalled !== false);

      if (isInstalled) {
        void context.activate();
      } else {
        context.deactivate();
      }
    }).catch(() => {
      // 容错降级
      void context.activate();
    });

    return context;
  }

  // 全局消息监听（统一拦截并分发后台广播命令）
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (!message || !message.appId) return;
    const context = webAppRegistry.get(message.appId);
    if (!context) return;

    // 1. 响应后台心跳探测
    if (message.type === 'yanzi_webapp_ping') {
      sendResponse({
        ok: true,
        appId: context.id,
        version: context.version,
        active: context.isActive
      });
      return;
    }

    // 2. 响应后台即时启停指令 (Hot Toggle)
    if (message.type === 'yanzi_webapp_command') {
      if (message.action === 'disable') {
        context.deactivate();
        sendResponse?.({ ok: true, active: false });
      } else if (message.action === 'enable') {
        void context.activate();
        sendResponse?.({ ok: true, active: true });
      } else {
        try {
          const res = context.definition.onCommand?.(message.action, message, context);
          sendResponse?.({ ok: true, result: res });
        } catch (err) {
          sendResponse?.({ ok: false, error: err?.message || String(err) });
        }
      }
      return;
    }

    // 3. 响应配置变更同步 (Data Sync)
    if (message.type === 'yanzi_webapp_data_changed' && message.key === 'settings') {
      if (message.value && typeof message.value === 'object') {
        context.settings = { ...context.defaultSettings, ...message.value };
        try {
          context.definition.onSettingsChanged?.(context.settings, context);
        } catch (err) {
          console.error(`[${context.id}] onSettingsChanged 回调异常:`, err);
        }
      }
    }
  });

  // 全局本地存储监听（双保险响应开关变化）
  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== 'local') return;

    // 监听安装开关变化
    if (changes[INSTALLED_STORAGE_KEY]) {
      const installedMap = changes[INSTALLED_STORAGE_KEY].newValue || {};
      for (const [appId, context] of webAppRegistry.entries()) {
        if (typeof installedMap[appId] === 'boolean') {
          if (installedMap[appId]) {
            void context.activate();
          } else {
            context.deactivate();
          }
        }
      }
    }

    // 监听配置直接变更
    for (const [appId, context] of webAppRegistry.entries()) {
      const settingsKey = `${DATA_PREFIX}${appId}::settings`;
      if (changes[settingsKey]) {
        const newVal = changes[settingsKey].newValue;
        if (newVal && typeof newVal === 'object') {
          context.settings = { ...context.defaultSettings, ...newVal };
          try {
            context.definition.onSettingsChanged?.(context.settings, context);
          } catch (err) {
            console.error(`[${appId}] onSettingsChanged 异常:`, err);
          }
        }
      }
    }
  });

  // 暴露公共接口
  globalThis.defineYanziWebApp = defineYanziWebApp;
  globalThis.getYanziWebAppContext = id => webAppRegistry.get(id);
})();
