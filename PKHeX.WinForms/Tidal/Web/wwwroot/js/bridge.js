// TidalHeX bridge: JSON RPC to the C# host (WebView2), or the dev mock in a normal browser.
window.Tidal = (() => {
  const wv = window.chrome && window.chrome.webview;
  const pending = new Map();
  const listeners = new Map();
  let nextId = 1;

  function emit(event, data) {
    for (const fn of listeners.get(event) ?? []) {
      try { fn(data); } catch (e) { console.error(e); }
    }
  }

  if (wv) {
    wv.addEventListener('message', e => {
      const m = e.data;
      if (!m) return;
      if (m.event) { emit(m.event, m.data); return; }
      const p = pending.get(m.id);
      if (!p) return;
      pending.delete(m.id);
      m.ok ? p.resolve(m.result) : p.reject(new Error(m.error || 'Unknown error'));
    });
  }

  function call(method, args) {
    if (!wv) {
      const mock = window.TidalMock;
      if (!mock) return Promise.reject(new Error('Host not available'));
      return mock.call(method, args ?? null, emit);
    }
    return new Promise((resolve, reject) => {
      const id = nextId++;
      pending.set(id, { resolve, reject });
      wv.postMessage({ id, method, args: args ?? null });
    });
  }

  /** Sends a notification that expects no reply (e.g. the answer to an in-page dialog). */
  function notify(method, args) {
    if (!wv) { window.TidalMock?.call(method, args ?? null, emit); return; }
    wv.postMessage({ method, args: args ?? null });
  }

  /** Sends dropped files to the host: opened normally, or e.g. `box.dropFile` with `{ slot }` to place them in a slot. */
  function dropFiles(files, method = 'openDropped', args = null) {
    if (!wv) return call(method, { ...(args ?? {}), names: [...files].map(f => f.name) });
    return new Promise((resolve, reject) => {
      const id = nextId++;
      pending.set(id, { resolve, reject });
      wv.postMessageWithAdditionalObjects({ id, method, args }, files);
    });
  }

  function on(event, fn) {
    if (!listeners.has(event)) listeners.set(event, new Set());
    listeners.get(event).add(fn);
    return () => listeners.get(event).delete(fn);
  }

  // Image URLs: served by the host; the dev mock only has a small sprite sample.
  const url = {
    species: (id, form = 0, shiny = false, gender = null) => (wv ? `/sprite/species/${id}/${form}?shiny=${shiny ? 1 : 0}${gender != null ? '&gender=' + gender : ''}` : `mock/sprites/${id}${shiny ? 's' : ''}.png`),
    ball: id => (wv ? `/sprite/ball/${id}` : `mock/sprites/ball${id}.png`),
    item: id => (wv ? `/sprite/item/${id}` : `mock/sprites/item${id}.png`),
    enc: token => `/sprite/enc/${token}`,
    /** Same sprite without PKHeX's slot overlays (held item, shiny sparkle, Tera stripe). */
    clean: src => (src && src.includes('?') ? `${src}&clean=1` : src),
  };

  return { call, notify, on, dropFiles, url, isHost: !!wv };
})();
