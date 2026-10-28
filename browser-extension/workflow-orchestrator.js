/* Yanzi Browser Workflow V2: tab-scoped, cross-navigation orchestration.
 * Runs in the extension's background service worker. No arbitrary JS from the caller.
 * The content-script v1 runner remains available for site-specific legacy flows.
 */
(() => {
  "use strict";
  const POLL_MS = 150;
  const DEFAULT_TIMEOUT = 12000;
  const MAX_TIMEOUT = 30000;
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  const timeoutFor = step => Math.min(MAX_TIMEOUT, Math.max(100, Number(step.timeout) || DEFAULT_TIMEOUT));
  const httpUrl = input => {
    const url = new URL(input);
    if (!["http:", "https:"].includes(url.protocol)) throw new Error("Only HTTP(S) URLs are allowed.");
    return url.toString();
  };

  // This function is serialized into the target frame by chrome.scripting.
  // It must be self-contained and must not close over extension state.
  function performDomStep(step) {
    const element = selector => {
      const parts = String(selector || "").split(/\s*>>>\s*/);
      let root = document, found = null;
      for (let i = 0; i < parts.length; i++) {
        if (!parts[i]) throw new Error("Empty shadow selector.");
        found = root.querySelector(parts[i]);
        if (!found) return null;
        if (i !== parts.length - 1) {
          if (!found.shadowRoot) return null;
          root = found.shadowRoot;
        }
      }
      return found;
    };
    if (step.type === "snapshot") {
      const limit = Math.max(1, Math.min(120, Number(step.limit) || 60));
      const maxLength = Math.max(30, Math.min(250, Number(step.maxTextLength) || 120));
      const safeText = raw => String(raw ?? "").replace(/\s+/g, " ").trim().slice(0, maxLength);
      const roleOf = el => {
        const attr = el.getAttribute("role");
        if (attr) return attr;
        const tag = el.localName;
        if (tag === "a" && el.hasAttribute("href")) return "link";
        if (tag === "button") return "button";
        if (tag === "input") {
          const type = (el.getAttribute("type") || "text").toLowerCase();
          return ["button", "submit", "reset"].includes(type) ? "button" :
            ["checkbox", "radio", "range"].includes(type) ? type :
            type === "password" ? "textbox" : "textbox";
        }
        if (tag === "textarea") return "textbox";
        if (tag === "select") return "combobox";
        if (tag === "img") return "img";
        if (/^h[1-6]$/.test(tag)) return "heading";
        if (tag === "nav") return "navigation";
        if (tag === "main") return "main";
        return "";
      };
      const nameOf = el => {
        const labelledBy = el.getAttribute("aria-labelledby");
        if (labelledBy) {
          const refNames = labelledBy.split(/\s+/).map(id =>
            (el.getRootNode()?.getElementById?.(id) || document.getElementById(id))?.textContent || "");
          if (refNames.some(Boolean)) return safeText(refNames.join(" "));
        }
        const explicit = el.getAttribute("aria-label") || el.getAttribute("alt") ||
          el.getAttribute("title");
        if (explicit) return safeText(explicit);
        if (el.labels?.length) return safeText([...el.labels].map(label => label.textContent).join(" "));
        // Never return input values, editable content, password contents or form secrets.
        if (el.matches("input, textarea, select, [contenteditable]")) {
          return safeText(el.getAttribute("placeholder") || el.getAttribute("name") || "");
        }
        return safeText(el.innerText ?? el.textContent);
      };
      const selectorOf = el => {
        const segments = [];
        let node = el;
        while (node && node.nodeType === 1 && segments.length < 6) {
          const root = node.getRootNode();
          const steps = [];
          let cur = node;
          while (cur && cur.nodeType === 1 && steps.length < 10) {
            let piece = cur.localName;
            if (cur.id) {
              steps.unshift(piece + "#" + CSS.escape(cur.id));
              break;
            }
            const testId = cur.getAttribute("data-testid");
            if (testId) {
              steps.unshift(piece + '[data-testid="' + CSS.escape(testId) + '"]');
              break;
            }
            let prior = cur.previousElementSibling, n = 1;
            while (prior) { if (prior.localName === cur.localName) n++; prior = prior.previousElementSibling; }
            steps.unshift(piece + ":nth-of-type(" + n + ")");
            cur = cur.parentElement;
          }
          segments.unshift(steps.join(" > "));
          node = root.host || null;
        }
        return segments.join(" >>> ");
      };
      const headings = [], controls = [], frames = [];
      let inspected = 0, shadowRootCount = 0, truncated = false;
      const walk = root => {
        const nodes = root.querySelectorAll("*");
        for (const el of nodes) {
          if (inspected++ >= 6000) { truncated = true; return; }
          if (el.closest("[hidden], [aria-hidden='true']")) continue;
          const role = roleOf(el);
          if (role === "heading" && headings.length < 30) {
            headings.push({ level: Number(el.localName?.slice(1)) || undefined, name: nameOf(el) });
          } else if (role && ["button", "link", "textbox", "checkbox", "radio", "combobox", "range"].includes(role)) {
            if (controls.length < limit) {
              controls.push({ role, name: nameOf(el), selector: selectorOf(el),
                disabled: !!el.disabled || el.getAttribute("aria-disabled") === "true" });
            } else truncated = true;
          }
          if (el.localName === "iframe" && frames.length < 15) {
            frames.push({ title: safeText(el.title), srcOrigin: (() => {
              try { return new URL(el.src).origin; } catch { return ""; }
            })() });
          }
          if (el.shadowRoot) {
            shadowRootCount++;
            walk(el.shadowRoot);
          }
        }
      };
      walk(document);
      return { url: location.href, title: safeText(document.title), readyState: document.readyState,
        headings, controls, frames, openShadowRoots: shadowRootCount, inspected,
        truncated, note: "Values, passwords and editable content omitted; cross-origin frame contents not included." };
    }
    if (step.type === "inspect") {
      const el = step.selector ? element(step.selector) : null;
      return { found: !!el, href: el?.closest("a[href]")?.href || "",
        target: el?.closest("a[href]")?.target || "", url: location.href,
        readyState: document.readyState };
    }
    if (step.type === "wait") {
      return { found: !!element(step.selector), url: location.href, readyState: document.readyState };
    }
    if (step.type === "fill") {
      const el = element(step.selector);
      if (!el) throw new Error("Input not found: " + step.selector);
      if (!("value" in el)) throw new Error("Element is not an input: " + step.selector);
      const proto = el.tagName === "SELECT" ? HTMLSelectElement.prototype :
        el.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
      if (setter) setter.call(el, String(step.value ?? ""));
      else el.value = String(step.value ?? "");
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
      return { ok: true };
    }
    if (step.type === "click") {
      const el = element(step.selector);
      if (!el) throw new Error("Click target not found: " + step.selector);
      el.click();
      return { ok: true };
    }
    if (step.type === "scroll") {
      window.scrollBy(0, Number(step.distance) || 400);
      return { ok: true };
    }
    if (step.type === "assert") {
      const el = step.selector ? element(step.selector) : null;
      const exists = step.selector ? !!el : true;
      const text = el ? String(el.innerText ?? el.textContent ?? "") : "";
      return { exists, text: text.slice(0, 1000), url: location.href };
    }
    if (step.type === "scrape") {
      const output = {};
      for (const [key, config] of Object.entries(step.selectors || {})) {
        const raw = typeof config === "string" ? config : config.selector;
        const parts = String(raw).split("|");
        const selector = parts[0].trim();
        const attr = typeof config === "string" ? (parts[1]?.trim() || "innerText") :
          (config.attr || "innerText");
        output[key] = [...document.querySelectorAll(selector)].map(el => {
          const value = attr === "innerText" ? (el.innerText ?? el.textContent) :
            attr === "textContent" ? el.textContent : attr === "innerHTML" ? el.innerHTML :
            el.getAttribute(attr);
          return String(value ?? "").trim();
        }).filter(Boolean);
      }
      return output;
    }
    throw new Error("Unsupported browser step: " + step.type);
  }

  function createController(chromeApi) {
    const active = new Map();
    const journal = globalThis.yanziWorkflowCheckpointsFactory?.(chromeApi);
    const initialized = journal?.interruptUnfinished().catch(() => 0) ?? Promise.resolve(0);

    async function evaluate(tabId, step) {
      const response = await chromeApi.scripting.executeScript({
        target: { tabId }, func: performDomStep, args: [step]
      });
      if (!response?.length) throw new Error("The target tab returned no script result.");
      return response[0].result;
    }

    async function tab(tabId) {
      const info = await chromeApi.tabs.get(tabId);
      if (!info) throw new Error("The workflow tab was closed.");
      return info;
    }

    async function waitForUrl(tabId, expected, previous, timeoutMs, allowAnyChange = false) {
      const until = Date.now() + timeoutMs;
      while (Date.now() < until) {
        const info = await tab(tabId);
        const current = String(info.url || "");
        if (allowAnyChange ? (current && current !== previous && current.startsWith("http")) :
          current.includes(expected)) {
          return current;
        }
        await sleep(POLL_MS);
      }
      throw new Error("Navigation timeout: expected " + (expected || "URL change") +
        ", current " + (await tab(tabId)).url);
    }

    async function waitForSelector(tabId, selector, timeoutMs) {
      const until = Date.now() + timeoutMs;
      let lastError = "";
      while (Date.now() < until) {
        try {
          const result = await evaluate(tabId, { type: "wait", selector });
          if (result?.found) return true;
        } catch (error) {
          // A navigation temporarily destroys the old document; retry on the new one.
          lastError = error.message;
        }
        await sleep(POLL_MS);
      }
      throw new Error("Element wait timed out: " + selector + (lastError ? " (" + lastError + ")" : ""));
    }

    async function stepAction(tabId, step) {
      const type = String(step.type || "").toLowerCase();
      const timeout = timeoutFor(step);
      if (type === "navigate") {
        const url = httpUrl(step.url);
        await chromeApi.tabs.update(tabId, { url });
        await waitForUrl(tabId, url.split("#")[0], "", timeout);
        if (step.selector) await waitForSelector(tabId, step.selector, timeout);
        return {};
      }
      if (type === "waitforurl") {
        await waitForUrl(tabId, String(step.urlIncludes || step.url || ""), "", timeout);
        if (step.selector) await waitForSelector(tabId, step.selector, timeout);
        return {};
      }
      if (type === "wait") {
        if (step.urlIncludes) await waitForUrl(tabId, String(step.urlIncludes), "", timeout);
        if (step.selector) await waitForSelector(tabId, step.selector, timeout);
        else if (!step.urlIncludes) await sleep(Math.min(timeout, 30000));
        // Backwards-compatible strict assertion via wait.contains. The installed
        // older host accepts wait but may not know the new 'assert' step yet.
        if (step.contains !== undefined) {
          if (!step.selector) throw new Error("wait.contains requires selector.");
          const until = Date.now() + timeout;
          let matched = false;
          while (Date.now() < until) {
            try {
              const result = await evaluate(tabId, { type: "assert", selector: step.selector });
              if (result?.exists && result.text.includes(String(step.contains))) {
                matched = true;
                break;
              }
            } catch (_) { /* Page may be navigating; retry. */ }
            await sleep(POLL_MS);
          }
          if (!matched) throw new Error("Text wait timed out: " + step.contains);
        }
        return {};
      }
      if (type === "click") {
        const before = await evaluate(tabId, { type: "inspect", selector: step.selector });
        if (!before.found) throw new Error("Click target not found: " + step.selector);
        const target = step.waitForUrl || "";
        const href = before.href;
        if (before.target === "_blank" && !target)
          throw new Error("Link opens a new tab; popup navigation requires an explicit workflow step.");
        const shouldWait = !!target || (step.expectNavigation !== false &&
          !!href && before.target !== "_blank" && href.split("#")[0] !== before.url.split("#")[0]);
        try { await evaluate(tabId, { ...step, type: "click" }); }
        catch (error) {
          if (!shouldWait) throw error;
          // A successful navigation can invalidate the script response; URL change is the proof.
        }
        if (shouldWait) {
          if (target) await waitForUrl(tabId, String(target), "", timeout);
          else await waitForUrl(tabId, "", before.url, timeout, true);
        }
        return {};
      }
      if (type === "assert") {
        if (step.urlIncludes) {
          const current = (await tab(tabId)).url || "";
          if (!current.includes(String(step.urlIncludes)))
            throw new Error("URL assertion failed: expected " + step.urlIncludes + ", got " + current);
        }
        if (step.selector) {
          if (step.exists !== false) await waitForSelector(tabId, step.selector, timeout);
          const r = await evaluate(tabId, { type: "assert", selector: step.selector });
          if (!r) throw new Error("Page was not ready for assertion: " + step.selector);
          if (step.exists === false ? r.exists : !r.exists)
            throw new Error("Element assertion failed: " + step.selector);
          if (step.contains != null && !r.text.includes(String(step.contains)))
            throw new Error("Text assertion failed: expected " + step.contains + " in " + step.selector);
        }
        return {};
      }
      if (type === "snapshot") {
        await waitForSelector(tabId, "body", timeout);
        return await evaluate(tabId, { ...step, type });
      }
      if (type === "scrape") {
        await waitForSelector(tabId, "body", timeout);
        return await evaluate(tabId, { ...step, type });
      }
      if (type === "fill" || type === "scroll") return await evaluate(tabId, { ...step, type });
      throw new Error("Unsupported workflow step: " + step.type);
    }

    async function run(task) {
      const start = Date.now();
      const deadline = start + Math.min(120, Math.max(5, Number(task.timeoutSeconds) || 30)) * 1000;
      const steps = Array.isArray(task.steps) ? task.steps : [];
      let tabId, completedSteps = 0, currentStep = null;
      let status = "error", data = null, message = "";
      const taskId = String(task.taskId || "");
      await initialized;
      if (taskId && active.has(taskId)) {
        return { status: "error", data: null, message: "Workflow already running: " + taskId };
      }
      // Reserve identity before awaiting tab creation to prevent duplicate concurrent jobs.
      if (taskId) active.set(taskId, "starting");
      try {
        const url = httpUrl(task.url);
        const created = await chromeApi.tabs.create({ url, active: false });
        tabId = created.id;
        if (taskId) active.set(taskId, tabId);
        await journal?.begin(taskId, { tabId, origin: new URL(url).origin, totalSteps: steps.length }).catch(() => {});
        const results = Object.create(null);
        for (let i = 0; i < steps.length; i++) {
          currentStep = { index: i + 1, type: String(steps[i].type || "") };
          await journal?.progress(taskId, completedSteps, currentStep).catch(() => {});
          const remaining = deadline - Date.now() - 800;
          if (remaining <= 0) throw new Error("Workflow exceeded its execution deadline.");
          const partial = await stepAction(tabId, { ...steps[i],
            timeout: Math.min(timeoutFor(steps[i]), remaining) });
          if (currentStep.type.toLowerCase() === "scrape") Object.assign(results, partial);
          if (currentStep.type.toLowerCase() === "snapshot") {
            const key = String(steps[i].key || "snapshot");
            if (["__proto__", "constructor", "prototype"].includes(key))
              throw new Error("Reserved snapshot key is not allowed.");
            results[key] = partial;
          }
          completedSteps = i + 1;
          await journal?.progress(taskId, completedSteps, null).catch(() => {});
          await journal?.progress(taskId, completedSteps, null).catch(() => {});
        }
        status = "success";
        data = { ...results, _workflow: { tabId, finalUrl: (await tab(tabId)).url,
          completedSteps, elapsedMs: Date.now() - start } };
      } catch (error) {
        message = "Step " + (currentStep?.index || 0) + " (" +
          (currentStep?.type || "create") + "): " + error.message;
        data = { _workflow: { tabId, completedSteps, failedStep: currentStep,
          elapsedMs: Date.now() - start } };
      } finally {
        await journal?.finish(taskId, status, completedSteps).catch(() => {});
        if (taskId) active.delete(taskId);
        if (tabId != null && task.closeOnComplete) {
          try { await chromeApi.tabs.remove(tabId); } catch (_) { /* Already closed. */ }
        }
      }
      return { status, data, message };
    }
    async function inspect(taskId) {
      await initialized;
      return { supported: !!journal?.supported, tasks: await journal?.status(taskId) || [] };
    }
    return { run, active, inspect };
  }

  globalThis.yanziWorkflowV2 = createController(chrome);
  // Export the factory only in Node tests, not through an extension messaging API.
  if (typeof module !== "undefined" && module.exports) module.exports = { createController, performDomStep };
})();
