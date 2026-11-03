// "Save locally" payload, injected by the wrapper over CDP into the music-application://desktop page.
// Injector substitutes the markers __LOCALE__ (locale JSON) and __SVC__ ({ url, token } of the wrapper service; main tells
// whether the bridge is installed in the music app's main process: then it writes the files, otherwise the wrapper service does).
// Expression result: { ok: true } when installed; { ok: false, error } when the app is not ready yet,
// and Injector retries in 3 s.
(() => {
  const VERSION = 99;
  const LOC = __LOCALE__;
  const SVC = __SVC__;
  if (window.__ymSave && window.__ymSave.version === VERSION && window.__ymSave.lang === LOC.lang_code) return { ok: true, already: true };
  const T = (k, v) => { let s = LOC[k] || k; if (v) for (const kk in v) s = s.split('{' + kk + '}').join(v[kk]); return s; };
  const esc = (s) => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const errText = (e) => String(e && e.message || e);

  // ── dependencies from the app, found at injection time: nothing is taken from minified names
  // (window.__ymDeps shows what was found) ──
  const findFiberRoot = () => {
    const el = document.documentElement;
    const fk = Object.keys(el).find(k => k.startsWith('__reactFiber$'));
    if (!fk) return null;
    let f = el[fk];
    while (f && f.return) f = f.return;
    return f && f.stateNode && f.stateNode.current ? f.stateNode.current : f;
  };
  // DI container: a React context value with .get/.registerMany (outside render _currentValue = null, hence the fiber walk)
  const findContainer = () => {
    if (window.__ymContainer) return window.__ymContainer;
    const root = findFiberRoot();
    if (!root) return null;
    const seen = new Set(); const stack = [root];
    const isC = (v) => v && typeof v === 'object' && typeof v.get === 'function' && typeof v.registerMany === 'function';
    while (stack.length) {
      const node = stack.pop();
      if (!node || seen.has(node)) continue;
      seen.add(node);
      const d = node.dependencies;
      if (d && d.firstContext) {
        let c = d.firstContext, g = 0;
        while (c && g++ < 50) { if (isC(c.memoizedValue)) { window.__ymContainer = c.memoizedValue; return c.memoizedValue; } c = c.next; }
      }
      if (node.child) stack.push(node.child);
      if (node.sibling) stack.push(node.sibling);
    }
    return null;
  };
  // root store (isRootModel): mirror of track download states
  const findStore = () => {
    if (window.__ymStore) return window.__ymStore;
    const root = findFiberRoot();
    if (!root) return null;
    const seen = new Set(); const stack = [root];
    while (stack.length) {
      const node = stack.pop();
      if (!node || seen.has(node)) continue;
      seen.add(node);
      const p = node.memoizedProps;
      if (p && typeof p === 'object' && p.value && p.value.isRootModel) { window.__ymStore = p.value; return p.value; }
      let hs = node.memoizedState, h = 0;
      while (hs && h++ < 30) {
        const v = hs.memoizedState;
        if (v && typeof v === 'object' && v.isRootModel) { window.__ymStore = v; return v; }
        hs = hs.next;
      }
      if (node.child) stack.push(node.child);
      if (node.sibling) stack.push(node.sibling);
    }
    return null;
  };

  // The container registers services under readable string tokens. The token is tried first; if it was renamed,
  // the service is recognized by its shape among the instances the container has already created
  // (get() on every binding would also create the ones the app has not created yet).
  const resolve = (container, token, fits) => {
    try { if (container.has(token)) { const v = container.get(token); if (fits(v)) return v; } } catch (e) {}
    for (const m of Object.values(container)) {
      if (!(m instanceof Map)) continue;
      for (const v of m.values()) { try { if (fits(v)) return v; } catch (e) {} }
    }
    return null;
  };
  const container = findContainer();
  if (!container) return { ok: false, error: 'DI container not found' };
  let deps;
  try {
    const store = findStore();
    const slam = resolve(container, 'Slam', v => !!(v && v.tracksController && typeof v.tracksController.download === 'function'));
    const fileInfo = resolve(container, 'GetFileInfoResource',
      v => !!(v && v.localFileInfoProvider && typeof v.localFileInfoProvider.getLocalFileDownloadInfo === 'function'));
    const tc = slam && slam.tracksController;
    deps = {
      tc,
      downloader: tc && tc.trackManager && tc.trackManager.downloader, // the app's download task queue
      reader: fileInfo && fileInfo.localFileInfoProvider,
      playlistRes: resolve(container, 'PlaylistResource', v => !!(v && typeof v.getPlaylist === 'function')),
      mirror: store && store.slam && store.slam.tracks,
    };
  } catch (e) { return { ok: false, error: 'deps: ' + errText(e) }; }
  const missing = Object.keys(deps).filter(k => !deps[k]);
  if (missing.length) return { ok: false, error: 'deps not ready: ' + missing.join(',') };
  // every app method and list the payload uses: a renamed one is reported at installation, not when the user saves
  const at = (path) => path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), deps);
  const broken = ['tc.download', 'tc.stopDownload', 'tc.deleteTrack', 'tc.tracksResource.getTracksMeta', 'tc.trackManager.getAllIds',
    'tc.trackManager.hooks.afterError.tap', 'tc.trackManager.getTrackArtists', 'tc.trackManager.getTrackAlbums',
    'tc.trackManager.repositoryContainer.tracksRepository.get', 'reader.isTrackDownloaded',
    'reader.getLocalFileDownloadInfo', 'playlistRes.getPlaylist', 'mirror.get'].filter(p => typeof at(p) !== 'function')
    .concat(['downloader.queue', 'downloader.activeTasks'].filter(p => !Array.isArray(at(p))));
  if (broken.length) return { ok: false, error: 'app API changed: ' + broken.join(', ') };
  window.__ymDeps = deps; // for debugging over CDP

  // ── removing the previous version (version or language change) ──
  const old = window.__ymSave;
  if (old) {
    old.cancelled = true;
    try { old.mo.disconnect(); } catch (e) {}
    try { clearInterval(old.menuGuard); } catch (e) {}
  }
  document.querySelectorAll('#ym-save-panel, #ym-save-dialog, [data-ym-save], [data-ym-del]').forEach(n => n.remove());

  const FINAL = ['done', 'error', 'cancelled'];
  const S = window.__ymSave = {
    version: VERSION, lang: LOC.lang_code,
    jobs: [], total: 0, deadTotal: 0, cancelled: false, working: false, mode: null,
    opSeq: 0, maxTracks: 300, usedNames: new Set(), menuEntity: null, events: [],
    // read by the wrapper on every tick: problem — something of ours does not work (menu, self-checks);
    // saveError — { id, text } of the first error of the latest save or delete operation
    problem: null, problems: {}, saveError: null,
  };
  S.setProblem = (kind, text) => {
    S.problems[kind] = text || null;
    S.problem = Object.entries(S.problems).filter(([, v]) => v).map(([k, v]) => k + ': ' + v).join('; ') || null;
    if (text) S.ev(kind + ': ' + text);
  };
  S.reportSaveError = (text) => {
    if (S.saveError && S.saveError.op === S.opSeq) return; // one report per operation, the panel lists the rest
    S.saveError = { op: S.opSeq, id: Date.now() + ':' + Math.random(), text: String(text).slice(0, 200) };
  };
  S.ev = (what) => { S.events.push({ t: Date.now(), what }); if (S.events.length > 10) S.events.shift(); };

  // ── progress panel ──
  const el = document.createElement('div');
  el.id = 'ym-save-panel';
  el.innerHTML = `
<style>
 /* colors and sizes come from the music app's own CSS variables (theme on body), with dark theme fallbacks */
 #ym-save-panel{
  --p-bg:var(--ym-background-color-primary-enabled-popover,#1a1a1a);
  --p-text:var(--ym-controls-color-secondary-on_default-enabled,#e6e6e6);
  --p-text2:var(--ym-controls-color-secondary-text-enabled,rgba(255,255,255,.5));
  --p-fill:var(--ym-controls-color-secondary-default-enabled,rgba(255,255,255,.08));
  --p-fill-hover:var(--ym-controls-color-secondary-default-hovered,rgba(255,255,255,.12));
  --p-ok:var(--ym-message-color-successful-text-enabled,#37c84f);
  --p-err:var(--ym-message-color-error-text-enabled,#fb2904);
  --p-accent:var(--ym-controls-color-primary-default-enabled,#ff0);
  --p-head:var(--ym-font-heading,var(--ym-font-text,sans-serif));
  position:fixed;right:12px;bottom:16px;width:340px;box-sizing:border-box;z-index:2147483647;
  background:var(--p-bg);color:var(--p-text);border-radius:var(--ym-radius-size-xl,16px);
  box-shadow:0 8px 40px var(--ym-shadow-menu,rgba(0,0,0,.48));
  font:400 13px/1.4 var(--ym-font-text,sans-serif);padding:16px;display:none}
 #ym-save-panel button{font:inherit;cursor:pointer;border:0}
 #ym-save-panel .ymhead{display:flex;justify-content:space-between;align-items:center;margin-bottom:14px}
 #ym-title{font:700 16px/1.25 var(--p-head)}
 #ym-close{width:28px;height:28px;border-radius:50%;background:transparent;color:var(--p-text2);font-size:12px}
 #ym-close:hover{background:var(--p-fill);color:var(--p-text)}
 #ym-fail{color:var(--p-err);margin-bottom:10px}
 .ymtop{display:flex;gap:14px;align-items:center;margin-bottom:12px}
 .ymtop>div{flex:1;min-width:0}
 .ymtop svg{flex:none}
 #ym-count{font:700 22px/1.2 var(--p-head)}
 .ymtop .yminfo{color:var(--p-text2);margin-top:2px;font-size:12px;overflow-wrap:anywhere}
 #ym-ring-track{stroke:var(--p-fill)} #ym-ring-done{stroke:var(--p-ok)} #ym-ring-err{stroke:var(--p-err)}
 #ym-pct{fill:var(--p-text);font-family:var(--p-head)}
 #ym-cur{background:var(--p-fill);border-radius:var(--ym-radius-size-l,12px);padding:10px 12px;margin-bottom:10px;min-height:46px}
 .ymcurrow{display:flex;gap:8px;align-items:baseline}
 #ym-curname{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;height:18px}
 #ym-curpct{flex:none;color:var(--p-text2);font-size:12px}
 /* steps shrink with an ellipsis instead of overflowing the block */
 .ymsteps{display:flex;align-items:center;gap:6px;margin-top:6px;height:14px;min-width:0}
 .ymstep{display:flex;align-items:center;gap:4px;font-size:11px;color:var(--p-text2);min-width:0;flex:0 1 auto}
 .ymstep span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
 .ymstep i{width:6px;height:6px;border-radius:50%;background:var(--p-fill-hover);flex:none}
 .ymstep.done i{background:var(--p-ok)}
 .ymstep.act{color:var(--p-text)}.ymstep.act i{background:var(--p-accent);animation:ympulse 1.1s infinite}
 .ymsep{flex:1 1 6px;min-width:6px;height:1px;background:var(--p-fill)}
 @keyframes ympulse{50%{opacity:.35}}
 #ym-errors{margin-bottom:6px}
 #ym-err-toggle{color:var(--p-err);font-size:12px;cursor:pointer}
 #ym-err-list{max-height:100px;overflow:auto;margin-top:4px}
 .ymerr{padding:3px 0;border-top:1px solid var(--p-fill);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--p-err)}
 .ymbtns{display:flex;justify-content:flex-end}
 #ym-cancel{background:var(--p-fill);color:var(--p-text);border-radius:var(--ym-radius-size-xxxl,100px);padding:8px 18px;font-weight:500}
 #ym-cancel:hover{background:var(--p-fill-hover)}
 #ym-save-panel .ym-ok{color:var(--p-ok)} #ym-save-panel .ym-err{color:var(--p-err)} #ym-save-panel .ym-muted{color:var(--p-text2)}
</style>
<div class="ymhead"><b id="ym-title"></b><button id="ym-close">✕</button></div>
<div id="ym-fail" style="display:none"></div>
<div class="ymtop" id="ym-top">
 <svg width="64" height="64" viewBox="0 0 64 64">
  <circle id="ym-ring-track" cx="32" cy="32" r="26" fill="none" stroke-width="6"/>
  <circle id="ym-ring-done" cx="32" cy="32" r="26" fill="none" stroke-width="6" stroke-linecap="round" pathLength="100" stroke-dasharray="0 100" stroke-dashoffset="25" style="transition:stroke-dasharray .3s,stroke-dashoffset .3s"/>
  <circle id="ym-ring-err" cx="32" cy="32" r="26" fill="none" stroke-width="6" stroke-linecap="round" pathLength="100" stroke-dasharray="0 100" stroke-dashoffset="25" style="transition:stroke-dasharray .3s,stroke-dashoffset .3s"/>
  <text id="ym-pct" x="32" y="37" text-anchor="middle" font-size="15" font-weight="700">0%</text>
 </svg>
 <div><div id="ym-count">0/0</div><div class="yminfo" id="ym-info"></div></div>
</div>
<div id="ym-cur"><div class="ymcurrow"><div id="ym-curname">—</div><div id="ym-curpct"></div></div><div class="ymsteps" id="ym-steps"></div></div>
<div id="ym-errors"><div id="ym-err-toggle"></div><div id="ym-err-list"></div></div>
<div class="ymbtns"><button id="ym-cancel"></button></div>`;
  document.body.appendChild(el);
  const $ = (q) => el.querySelector(q);
  $('#ym-title').textContent = T('panel_title_save');
  $('#ym-cancel').textContent = T('btn_cancel');
  // React may recreate the root: keep exactly one panel in the document; the panel sits above the player
  const ensurePanel = () => {
    document.querySelectorAll('#ym-save-panel').forEach(p => { if (p !== el) p.remove(); });
    if (!el.isConnected) document.body.appendChild(el);
    const bar = document.querySelector('[class*="PlayerBarDesktop"]');
    const top = bar && bar.offsetHeight ? bar.getBoundingClientRect().top : innerHeight;
    el.style.bottom = Math.max(16, innerHeight - top + 12) + 'px';
  };

  // line under the counter: base (text) + optional suffix (cancelled)
  S.setInfo = (text) => { S._infoBase = esc(text || ''); $('#ym-info').innerHTML = S._infoBase; };
  S.appendInfo = (suffixHtml) => { $('#ym-info').innerHTML = (S._infoBase || '') + suffixHtml; };
  // fatal error before start: message only, no counters
  S.fail = (msg) => {
    S.jobs = []; S.total = 0;
    ensurePanel(); el.style.display = 'block';
    for (const id of ['#ym-top', '#ym-cur', '#ym-errors', '#ym-cancel']) $(id).style.display = 'none';
    $('#ym-fail').style.display = '';
    $('#ym-fail').textContent = msg;
    S.ev('fail: ' + msg);
  };
  // prepare the panel for a new operation
  const resetPanel = (title) => {
    ensurePanel();
    $('#ym-title').textContent = title;
    $('#ym-top').style.display = '';
    $('#ym-fail').style.display = 'none';
    S.setInfo('');
  };
  S.render = () => {
    ensurePanel();
    el.style.display = S.jobs.length ? 'block' : 'none';
    const total = S.total || S.jobs.length;
    const done = S.jobs.filter(j => j.status === 'done').length;
    const err = S.jobs.filter(j => j.status === 'error').length;
    const dPct = total ? done / total * 100 : 0;
    const ePct = total ? err / total * 100 : 0;
    $('#ym-ring-done').style.visibility = dPct > 0 ? '' : 'hidden';
    $('#ym-ring-err').style.visibility = ePct > 0 ? '' : 'hidden';
    $('#ym-ring-done').setAttribute('stroke-dasharray', dPct + ' ' + (100 - dPct));
    $('#ym-ring-err').setAttribute('stroke-dasharray', ePct + ' ' + (100 - ePct));
    $('#ym-ring-err').setAttribute('stroke-dashoffset', 25 - dPct);
    $('#ym-pct').textContent = Math.round(total ? (done + err) / total * 100 : 0) + '%';
    const deadTag = S.deadTotal ? ' <span class="ym-muted" title="' + esc(T('dead_tooltip')) + '">+' + S.deadTotal + '✕</span>' : '';
    $('#ym-count').innerHTML = '<span class="ym-ok">' + done + '</span>/'
      + (err > 0 ? '<span class="ym-err">' + err + '</span>/' : '') + total + deadTag;
    const cur = S.jobs.find(j => ['downloading', 'decrypting', 'saving', 'deleting'].includes(j.status)) || S.jobs.find(j => j.status === 'queued');
    $('#ym-cur').style.display = S.working ? '' : 'none';
    $('#ym-curname').textContent = cur ? cur.name : '—';
    let stepsHtml;
    if (S.mode === 'delete') {
      stepsHtml = '<span class="ymstep act"><i></i><span>' + esc(T('step_delete')) + '</span></span>';
    } else {
      const STN = [T('step_download'), T('step_decrypt'), T('step_save')];
      const si = cur ? ({ downloading: 0, decrypting: 1, saving: 2 }[cur.status] ?? -1) : -1;
      stepsHtml = STN.map((n, k) => {
        const cls = k < si ? 'done' : (k === si ? 'act' : '');
        return '<span class="ymstep ' + cls + '"><i></i><span>' + esc(n) + '</span></span>' + (k < 2 ? '<span class="ymsep"></span>' : '');
      }).join('');
    }
    $('#ym-steps').innerHTML = stepsHtml;
    $('#ym-curpct').textContent = cur && cur.status === 'downloading' ? Math.round(cur.progress || 0) + '%' : '';
    const errs = S.jobs.filter(j => j.status === 'error');
    $('#ym-errors').style.display = errs.length ? '' : 'none';
    if (errs.length) {
      $('#ym-err-toggle').textContent = T('errors_toggle') + ' (' + errs.length + ')' + (S.errExpanded ? '  ▴' : '  ▾');
      $('#ym-err-list').style.display = S.errExpanded ? '' : 'none';
      $('#ym-err-list').innerHTML = errs.map(j => { const t = esc(j.name + (j.error ? ' — ' + j.error : '')); return '<div class="ymerr" title="' + t + '">' + t + '</div>'; }).join('');
    }
    $('#ym-cancel').style.display = S.working ? '' : 'none'; // by the operation flag, so the button does not blink between tracks
  };
  S.addJob = (name) => { const j = { id: Math.random().toString(36).slice(2), name, status: 'queued', progress: 0 }; S.jobs.push(j); S.render(); return j; };
  S.setJob = (j, patch) => { Object.assign(j, patch); S.render(); };

  $('#ym-cancel').onclick = () => {
    S.cancelled = true;
    S.jobs.forEach(j => { if (!FINAL.includes(j.status)) j.status = 'cancelled'; });
    S.appendInfo(' · <span class="ym-err" style="font-weight:600">' + esc(T('cancelled')) + '</span>');
    S.render();
  };
  $('#ym-close').onclick = () => { el.style.display = 'none'; };
  $('#ym-err-toggle').onclick = () => { S.errExpanded = !S.errExpanded; S.render(); };

  // ── entity of the open context menu: the menu component receives track/playlist/album as props ──
  S.entityFromMenu = (menuEl) => {
    const fk = Object.keys(menuEl).find(k => k.startsWith('__reactFiber$'));
    if (!fk) return null;
    const seen = new Set();
    const stack = [menuEl[fk]];
    let visited = 0, sawMeta = false;
    while (stack.length && visited < 800) {
      const node = stack.pop();
      if (!node || seen.has(node)) continue;
      seen.add(node); visited++;
      const p = node.memoizedProps;
      if (p && typeof p === 'object') {
        // entityMeta is the app's own entity descriptor in context menus (playlist of the day, albums, etc.)
        const em = p.entityMeta;
        if (em && em.variant) sawMeta = true;
        const path = em ? String(em.path || '') : '';
        // vibes/radio/waves are endless generated streams: the check is structural (path), not by name
        if (em && em.variant && !/\/(vibe|radio|rotor|wave|feed|generation|mix)(\/|$)/i.test(path)) {
          const mp = path.match(/playlists\/([^/?]+)/);
          if (em.variant === 'playlist' && mp) return { kind: 'playlist', uuid: mp[1], name: em.title };
          const ma = path.match(/album\/(\d+)/);
          if (em.variant === 'album' && (em.id != null || ma)) return { kind: 'album', id: String(em.id ?? ma[1]), name: em.title };
          const mt = path.match(/track\/(\d+)/);
          if (em.variant === 'track' && (em.id != null || mt)) return { kind: 'track', id: String(em.id ?? mt[1]), title: em.title, artists: [] };
        }
        if (p.track && p.track.id != null && !Array.isArray(p.track) && (p.track.title || p.track.artists))
          return { kind: 'track', id: String(p.track.id), title: p.track.title, artists: (p.track.artists || []).map(a => a.name) };
        if (p.playlist && p.playlist.uuid && /^(lk|ps)\./.test(String(p.playlist.uuid)))
          return { kind: 'playlist', uuid: p.playlist.uuid, name: p.playlist.title };
        if (p.album && p.album.id != null)
          return { kind: 'album', id: String(p.album.id), name: p.album.title };
      }
      if (node.child) stack.push(node.child);
      if (node.sibling) stack.push(node.sibling);
    }
    // a menu of an entity we do not save (artist, vibe…) is not an error; a menu without any entity data is
    return sawMeta ? { skip: true } : null;
  };
  // the item goes after "Share", otherwise after the last item
  S.findAnchor = (menuEl) => {
    const share = menuEl.querySelector('[data-test-id="CONTEXT_MENU_SHARE_BUTTON"]');
    if (share) return share;
    const items = menuEl.querySelectorAll('[role=menuitem]');
    return items.length ? items[items.length - 1] : null;
  };
  const closeMenu = () => {
    document.querySelectorAll('[role=menu]').forEach(m => { m.style.display = 'none'; m.style.pointerEvents = 'none'; });
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  };
  const menuItem = (proto, attr, iconId, label, onClick) => {
    const btn = proto.cloneNode(true);
    btn.setAttribute(attr, '1');
    const span = btn.querySelector('span');
    if (span) {
      const icon = span.querySelector('svg');
      const ih = icon ? icon.outerHTML.replace(/#([\w-]+)_xxs/, '#' + iconId + '_xxs')
        : '<svg focusable="false" aria-hidden="true"><use xlink:href="/icons/sprite.svg#' + iconId + '_xxs"></use></svg>';
      span.innerHTML = ih + esc(label);
    }
    btn.addEventListener('click', (ev) => { ev.stopPropagation(); ev.preventDefault(); closeMenu(); onClick(); });
    return btn;
  };
  S.injectItem = (anchor, ent) => {
    const menuEl = anchor.closest('[role=menu]') || anchor.parentElement;
    if (menuEl && menuEl.querySelector('[data-ym-save]')) return;
    const proto = anchor.cloneNode(true);
    for (const a of ['data-test-id', 'aria-haspopup', 'aria-expanded', 'disabled', 'data-disabled', 'data-nested']) proto.removeAttribute(a);
    proto.setAttribute('aria-disabled', 'false');
    // for some entities "Share" is grayed out (disabled), so take the classes of a regular item
    const norms = [...menuEl.querySelectorAll('[role=menuitem]')].filter(x => x !== anchor && !x.hasAttribute('data-ym-save'));
    const donor = norms.find(x => !x.hasAttribute('data-disabled') && !x.hasAttribute('data-nested')) || norms.find(x => !x.hasAttribute('data-disabled')) || norms[norms.length - 1];
    if (donor) {
      proto.className = donor.className;
      const ds = donor.querySelector('span'), bs = proto.querySelector('span');
      if (ds && bs) bs.className = ds.className;
    }
    proto.style.opacity = '1';
    proto.style.cursor = 'pointer';
    const save = menuItem(proto, 'data-ym-save', 'download', T('menu_save'), () => S.pickNow(Object.assign({}, ent)));
    anchor.after(save);
    // "Delete downloads" for playlists/albums (the app has no such item)
    if ((ent.kind === 'playlist' || ent.kind === 'album') && !menuEl.querySelector('[data-ym-del]'))
      save.after(menuItem(proto, 'data-ym-del', 'close', T('menu_del'), () => S.removeDownloads(Object.assign({}, ent))));
  };
  S.mo = new MutationObserver((muts) => {
    for (const m of muts) for (const n of m.addedNodes) {
      if (n.nodeType !== 1 || !n.querySelectorAll) continue;
      for (const menuEl of n.querySelectorAll('[role="menu"]')) {
        const ent = S.entityFromMenu(menuEl);
        // otherwise the guard would add our item for the previous menu's entity to this menu
        if (!ent || ent.skip) S.menuEntity = null;
        if (ent && ent.skip) continue;
        if (!ent) {
          S.ev('menu: entity not found');
          // a context menu with "Share" belongs to some entity: if none is recognized, the app changed
          if (menuEl.querySelector('[data-test-id="CONTEXT_MENU_SHARE_BUTTON"]')) S.setProblem('menu', 'entity not found');
          continue;
        }
        S.menuEntity = Object.assign({ ts: Date.now() }, ent);
        S.ev('menu ' + ent.kind + ': ' + String(ent.name || ent.title || ent.id).slice(0, 40));
        S.startMenuGuard();
        const anchor = S.findAnchor(menuEl);
        if (anchor) S.injectItem(anchor, ent);
        // self-check: the item must be in the open menu (the guard retries for a while if items render late)
        setTimeout(() => {
          if (menuEl.isConnected) S.setProblem('menu', menuEl.querySelector('[data-ym-save]') ? null : 'item not added');
        }, 1000);
        return;
      }
    }
  });
  S.mo.observe(document.body, { childList: true, subtree: true });
  // watchdog: while the menu is open the item must exist (React re-renders erase it)
  S.startMenuGuard = () => {
    if (S.menuGuard) return;
    let gone = 0;
    S.menuGuard = setInterval(() => {
      const menus = document.querySelectorAll('[role="menu"]');
      if (!menus.length) { if (++gone > 10) { clearInterval(S.menuGuard); S.menuGuard = null; } return; }
      gone = 0;
      if (!S.menuEntity || Date.now() - S.menuEntity.ts > 30000) return;
      for (const menuEl of menus) {
        if (menuEl.querySelector('[data-ym-save]')) continue;
        const anchor = S.findAnchor(menuEl);
        if (anchor) S.injectItem(anchor, S.menuEntity);
      }
    }, 150);
  };

  // ── download and decryption with the app's own means ──
  S.sniff = (buf) => {
    const s4 = (o) => String.fromCharCode(buf[o], buf[o + 1], buf[o + 2], buf[o + 3]);
    if (buf.length > 8 && s4(4) === 'ftyp') return 'm4a';
    if (s4(0).slice(0, 3) === 'ID3') return 'mp3';
    if (buf[0] === 0xff && (buf[1] & 0xe0) === 0xe0) return (buf[1] === 0xf1 || buf[1] === 0xf9) ? 'aac' : 'mp3';
    if (s4(0) === 'fLaC') return 'flac';
    if (s4(0) === 'OggS') return 'ogg';
    if (s4(0) === 'RIFF') return 'wav';
    return 'bin';
  };
  S.u8b64 = (u8) => { let s = ''; const CH = 0x8000; for (let i = 0; i < u8.length; i += CH) s += String.fromCharCode.apply(null, u8.subarray(i, i + CH)); return btoa(s); };
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  const loadingState = (id) => { const st = deps.mirror.get(id); return st && st.loadingState; };
  const hasTask = (id) => deps.downloader.queue.some(t => t.id === id) || deps.downloader.activeTasks.some(t => t.id === id);

  // ── temporary API failures: 429 (rate limited by the service) and 5xx are retried with growing delays ──
  // The app's HTTP client does not retry them itself; its errors read "Request failed with status code N".
  const RETRY_DELAYS = [3000, 10000, 30000];
  const httpStatus = (e) => {
    const m = /status code (\d{3})/.exec(String(e && (e.message || e)) + ' ' + String(e && e.cause));
    return m ? +m[1] : (e && (e.status || e.statusCode)) || 0;
  };
  const isTransient = (st) => st === 429 || st >= 500;
  const failText = (e) => httpStatus(e) === 429 ? T('err_rate_limited') : T('err_prefix', { msg: errText(e) });
  const waitRetry = async (ms) => {
    S.ev('retry in ' + ms);
    ensurePanel(); el.style.display = 'block';
    S.appendInfo(' · ' + esc(T('info_retry', { s: Math.round(ms / 1000) })));
    await sleep(ms);
    S.appendInfo('');
  };
  const withRetry = async (fn) => {
    for (let i = 0; ; i++) {
      try { return await fn(); }
      catch (e) {
        if (!isTransient(httpStatus(e)) || i >= RETRY_DELAYS.length || S.cancelled) throw e;
        await waitRetry(RETRY_DELAYS[i]);
      }
    }
  };

  // The download task failure event carries no reason: it goes to the app's error hook
  // (DownloaderException: data.id, cause). There is one subscription per page, and it survives payload reinstallation.
  if (!window.__yamuFailCause) {
    window.__yamuFailCause = new Map();
    try {
      deps.tc.trackManager.hooks.afterError.tap('YaMu+', (e) => {
        if (e && e.data && e.data.id != null) window.__yamuFailCause.set(String(e.data.id), String(e.cause || e.message));
      });
    } catch (e) {}
  }
  const failCause = window.__yamuFailCause;
  // the track cannot be downloaded (removed, blocked, no rights); the app reports this itself
  class DeadTrack extends Error {}
  const isDeadCause = (c) => /not available|not defined|download info/i.test(c || '');

  // Download with the app's own downloader. Track its task, not timers: until the first chunk
  // (queue, meta, file-info, first 1 MB) the state may stay unchanged for any length of time, and on failure
  // DOWNLOAD_FAILED is immediately overwritten with IDLE by an asynchronous deleteTrack.
  const ensureDownloaded = async (id, j) => {
    if (loadingState(id) === 'DOWNLOADED' || await deps.reader.isTrackDownloaded(id)) return;
    for (let attempt = 0; ; attempt++) {
      failCause.delete(id);
      deps.tc.download(id);
      if (!hasTask(id)) {
        // download() does not queue a task from the DOWNLOADING state, which can get stuck after an interrupted download
        deps.tc.stopDownload(id);
        deps.tc.download(id);
      }
      S.setJob(j, { status: 'downloading', progress: 0 });
      let lastMove = Date.now();
      while (hasTask(id)) {
        if (S.cancelled) { deps.tc.stopDownload(id); throw new Error(T('err_op_cancelled')); }
        // no task progress for 3 minutes means the network hung; ponytail: the threshold leaves room for the queue
        if (Date.now() - lastMove > 180000) { deps.tc.stopDownload(id); throw new Error(T('err_timeout')); }
        const st = deps.mirror.get(id);
        if (st && typeof st.progress === 'number' && st.progress > (j.progress || 0)) { S.setJob(j, { progress: st.progress }); lastMove = Date.now(); }
        await sleep(300);
      }
      if (loadingState(id) === 'DOWNLOADED') return; // set before the task is removed
      const cause = failCause.get(id);
      if (isDeadCause(cause)) throw new DeadTrack(cause);
      if (!isTransient(httpStatus(cause))) throw new Error(T('err_track_unavailable'));
      if (attempt >= RETRY_DELAYS.length || S.cancelled) throw new Error(failText(cause));
      await waitRetry(RETRY_DELAYS[attempt]); // during the delay the app cleans up after the failed task
    }
  };

  // Track data from the app database (written by the download task), for the file name when it was not known beforehand.
  const dbMeta = async (id) => {
    const tm = deps.tc.trackManager;
    const rec = await tm.repositoryContainer.tracksRepository.get(id);
    if (!rec || !rec.meta) return null;
    const [artists, albums] = await Promise.all([tm.getTrackArtists(id), tm.getTrackAlbums(id)]);
    return { title: rec.meta.title, version: rec.meta.version || '', artists: (artists || []).map(a => a.name), album: (albums && albums[0] && albums[0].title) || '' };
  };
  // "Artist - Title (Version)"; version can be "Rmx By X/Y", so slashes and control characters are replaced
  const fileBase = (m) => ((m.artists && m.artists.length ? m.artists.join(', ') + ' - ' : '') + (m.title || T('name_unknown'))
    + (m.version ? ' (' + m.version + ')' : '')).replace(/[\\/\u0000-\u001f]/g, '_');
  // a file/folder name valid on any OS (like the wrapper's SafeName): forbidden characters → "_",
  // no trailing dots or spaces (Windows silently strips them), empty → "_"
  const safeName = (s) => String(s || '').replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').trim().replace(/[. ]+$/, '') || '_';

  // Decryption: localFileInfoProvider reads the file from OPFS, decrypts it (AES-128-CTR) and returns a blob: URL.
  // The app sets DOWNLOADED without waiting for the file write (writeFile is not awaited): while the write
  // is in progress the file is missing or empty (the swap on close is atomic), so retry until data arrives.
  const readDecrypted = async (id) => {
    let err = null;
    for (let i = 0; i < 40; i++) { // ponytail: ~20 s, an OPFS write takes seconds even for lossless
      if (S.cancelled) throw new Error(T('err_op_cancelled'));
      try {
        const info = await deps.reader.getLocalFileDownloadInfo(id);
        try {
          const buf = new Uint8Array(await (await fetch(info.urls[0])).arrayBuffer());
          if (buf.length >= 1024) return buf; // empty decryption: do not write a 0-byte file
          err = new Error(T('err_track_unavailable'));
        } finally { URL.revokeObjectURL(info.urls[0]); } // otherwise every track stays in the page's memory
      } catch (e) { err = e; }
      await sleep(500);
    }
    throw err;
  };

  S.downloadAndExtract = async (id, j) => {
    await ensureDownloaded(id, j);
    S.setJob(j, { status: 'decrypting', progress: 100 });
    return readDecrypted(id);
  };
  // a name unique within the operation: on a clash add [Album], then (2), (3)…
  S.uniqName = (base, album, ext) => {
    const take = (nm) => { nm = safeName(nm); if (S.usedNames.has(nm.toLowerCase())) return null; S.usedNames.add(nm.toLowerCase()); return nm; };
    let r = take(base + '.' + ext) || (album && take(base + ' [' + album + '].' + ext));
    for (let n = 2; !r; n++) r = take(base + ' (' + n + ').' + ext);
    return r;
  };

  // ── entity track list: { name: default folder name, tracks: [{ id, meta | null, dead }] } ──
  // meta = { title, version, album, artists }; null means it is read from the app database after the download.
  const metaOf = (t, album) => ({
    title: t.title, version: t.version || '', // "2010 Remaster" etc., as in the app's list
    album: album != null ? album : (t.albums && t.albums[0] && t.albums[0].title) || '',
    artists: (t.artists || []).map(a => a.name),
  });
  const resolveTracks = async (ent) => {
    if (ent.kind === 'track')
      return { name: '', tracks: [{ id: String(ent.id), meta: { title: ent.title || ('track_' + ent.id), version: '', album: '', artists: ent.artists || [] }, dead: false }] };
    if (ent.kind === 'album') {
      // the with-tracks API is the main source (getAlbum often returns null for albums outside the user's library)
      const res = await withRetry(async () => {
        const r = await fetch('https://api.music.yandex.net/albums/' + ent.id + '/with-tracks', { credentials: 'include' });
        if (!r.ok) throw Object.assign(new Error(T('err_album_unavailable', { status: r.status })), { status: r.status });
        return (await r.json()).result;
      });
      if (!res || !Array.isArray(res.volumes)) throw new Error(T('err_album_not_found'));
      const list = res.volumes.flat();
      if (!list.length) throw new Error(T('err_album_empty'));
      const an = (res.artists || []).map(a => a.name);
      return {
        name: (an.length ? an.join(', ') + ' - ' : '') + (res.title || T('name_album')) + (res.year ? ' (' + res.year + ')' : ''),
        tracks: list.map(t => ({ id: String(t.id ?? t.trackId), meta: metaOf(t, res.title || ''), dead: t.available === false })),
      };
    }
    if (ent.kind === 'playlist') {
      // The open playlist: the page model already holds the ids of all tracks in order, and the data
      // is in the rendered rows (the list is lazy). The app's download task writes the rest to the database.
      // Unavailable tracks are known in advance only for rendered rows; the rest turn up during the download.
      const page = (findStore() || {}).playlist;
      if (page && page.uuid === ent.uuid && page.meta && page.items && page.items.length === page.meta.tracksCount) {
        const tracks = page.items.map(it => {
          const t = it.data && it.data.title ? it.data : null;
          return { id: String(it.id), meta: t && metaOf(t), dead: !!t && (t.isAvailable === false || t.isRemoved === true) };
        });
        // Availability of unrendered tracks: downloaded ones by the local database (no network needed). Blocked tracks
        // never reach the database (the download task fails before saving data), so the remaining ones, not downloaded or
        // unavailable, are checked with one batch request rather than a download task per track.
        const unknown = tracks.filter(t => !t.meta);
        const local = await Promise.all(unknown.map(t => deps.reader.isTrackDownloaded(t.id).catch(() => false)));
        // names of downloaded tracks come from the database right away: the save loop must not pause between tracks,
        // otherwise the panel briefly has no active task and flickers
        await Promise.all(unknown.map(async (t, i) => { if (local[i]) t.meta = await dbMeta(t.id).catch(() => null); }));
        const check = unknown.filter((t, i) => !local[i]);
        for (let i = 0; i < check.length; i += 100) {
          const part = check.slice(i, i + 100);
          const meta = await withRetry(() => deps.tc.tracksResource.getTracksMeta({ trackIds: part.map(t => t.id) }));
          const byId = new Map((meta || []).map(m => [String(m.id), m]));
          for (const t of part) {
            const m = byId.get(t.id);
            if (m) { t.meta = metaOf(m); t.dead = m.available === false; } else t.dead = true; // missing from the response means removed
          }
        }
        S.ev('playlist from page: ' + tracks.length + ', checked online: ' + check.length);
        return { name: ent.name || page.meta.title || '', tracks };
      }
      const pl = await withRetry(() => deps.playlistRes.getPlaylist({ playlistUuid: ent.uuid, richTracks: true }));
      return {
        name: ent.name || '',
        tracks: (pl.tracks || []).map(t => {
          const tr = t.track || t;
          // removed from the service: grayed out in the list, not playable
          return { id: String(tr.id ?? t.id), meta: metaOf({ ...tr, artists: tr.artists || t.artists, title: tr.title ?? t.title }), dead: tr.available === false };
        }),
      };
    }
    throw new Error(T('err_unsupported', { kind: ent.kind }));
  };

  // request to the wrapper service; the token tells us apart from other pages
  const svc = async (path, body) => {
    const r = await fetch(SVC.url + path, {
      method: 'POST', headers: { 'Content-Type': 'text/plain' },
      body: JSON.stringify(Object.assign({ token: SVC.token }, body)),
    });
    return r.json();
  };

  // ── save dialog file operations: start (remembered folder or Downloads), pick (system folder
  // picker), mkdir, write. The main path is the bridge in the music app's main process (SVC.main): the message goes over the
  // files:save-png IPC (musicDesktop.files.savePng) with the "yamu:token:" prefix, and the reply arrives in window.__yamuFs.
  // The fallback is the wrapper service /fs. Neither overwrites anything: a taken name → "Name (2)" ──
  const FS_PREFIX = 'yamu:' + SVC.token + ':';
  const fsWait = new Map();
  let fsSeq = 0;
  window.__yamuFs = (r) => {
    const w = fsWait.get(r.id);
    if (!w) return;
    fsWait.delete(r.id); clearTimeout(w.timer);
    if (r.error) w.reject(new Error(r.error)); else w.resolve(r.result);
  };
  const fsMain = (op, args, bytes) => new Promise((resolve, reject) => {
    const id = ++fsSeq;
    // the folder picker waits for the user with no time limit; everything else, including writing a large file, up to 2 minutes
    const timer = op === 'pick' ? 0 : setTimeout(() => { fsWait.delete(id); reject(new Error('timeout')); }, 120000);
    fsWait.set(id, { resolve, reject, timer });
    window.musicDesktop.files.savePng(FS_PREFIX + JSON.stringify({ id, op, args }), bytes || new Uint8Array(0));
  });
  const fsSvc = async (op, args, bytes) => {
    const r = await svc('/fs', { op, args, b64: bytes ? S.u8b64(bytes) : '' });
    if (!r.ok) throw new Error(r.error || '?');
    return r.result;
  };
  const fsCall = (op, args, bytes) => (SVC.main && window.musicDesktop ? fsMain : fsSvc)(op, args, bytes);

  // ── save dialog styled like the music app's modal windows (--ym-* tokens, dark theme fallbacks):
  // name, "Where" with the system folder picker ("Change…"), "Cancel" / "Save" / "Save + numbering"
  // (the last one for collections). The default folder is the last one used, otherwise Downloads.
  // opts: { name, label, numbering } → null (cancel) | { dir, name, numbered } ──
  const askSave = (opts) => new Promise((resolve) => {
    let numbered = false, last = '';
    try { numbered = localStorage.getItem('yamu.numbered') === '1'; last = localStorage.getItem('yamu.dir') || ''; } catch (e) {}
    const d = document.createElement('div');
    d.id = 'ym-save-dialog';
    d.innerHTML = `
<style>
 #ym-save-dialog{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;
  background:var(--ym-background-color-primary-enabled-overlay,rgba(0,0,0,.5));
  font:400 var(--ym-font-size-label-l,1rem)/1.3 var(--ym-font-text,sans-serif);color:var(--ym-controls-color-primary-text-enabled_variant,#e6e6e6)}
 #ym-save-dialog .ymd{width:540px;max-width:calc(100vw - 32px);box-sizing:border-box;padding:var(--ym-spacer-size-xxl,1.5rem);
  display:flex;flex-direction:column;background:var(--ym-background-color-primary-enabled-popover,#1a1a1a);border-radius:var(--ym-radius-size-xl,1rem);
  box-shadow:0 .0625rem 1.5rem 0 var(--ym-shadow-modal-xl,rgba(0,0,0,.48)),0 -.0625rem 1.5rem 0 var(--ym-shadow-modal-s,rgba(0,0,0,.24))}
 #ym-save-dialog .ymd-title{font:700 var(--ym-font-size-headline-s,1.375rem)/1.2 var(--ym-font-heading,var(--ym-font-text,sans-serif));
  margin-bottom:var(--ym-spacer-size-xxl,1.5rem)}
 #ym-save-dialog .ymd-label{display:block;color:var(--ym-controls-color-primary-text-enabled,rgba(255,255,255,.5));
  font-size:var(--ym-font-size-label-m,.875rem);margin-bottom:var(--ym-spacer-size-m,.75rem);overflow-wrap:anywhere}
 #ym-save-dialog .ymd-where-l{margin-top:var(--ym-spacer-size-l,1rem)}
 #ym-save-dialog .ymd-name,#ym-save-dialog .ymd-path{display:block;width:100%;box-sizing:border-box;height:48px;padding:0 var(--ym-spacer-size-l,1rem);
  border:0;outline:0;font:inherit;color:inherit;background:var(--ym-controls-color-secondary-default-enabled,rgba(255,255,255,.08));
  border-radius:var(--ym-radius-size-m,.5rem)}
 #ym-save-dialog .ymd-name:focus{box-shadow:inset 0 0 0 .125rem var(--ym-controls-color-secondary-default-hovered,rgba(255,255,255,.12))}
 #ym-save-dialog .ymd-where{display:flex;gap:var(--ym-spacer-size-xs,.5rem);align-items:center}
 #ym-save-dialog .ymd-path{flex:1;min-width:0;line-height:48px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
 #ym-save-dialog .ymd-err{display:none;margin-top:var(--ym-spacer-size-m,.75rem);font-size:var(--ym-font-size-label-m,.875rem);
  color:var(--ym-message-color-error-text-enabled,#fb2904);overflow-wrap:anywhere}
 #ym-save-dialog .ymd-btns{display:flex;gap:var(--ym-spacer-size-xs,.5rem);justify-content:flex-end;margin-top:var(--ym-spacer-size-xxl,1.5rem)}
 #ym-save-dialog button{flex:none;white-space:nowrap;height:40px;padding:0 var(--ym-spacer-size-xl,1.25rem);border:0;cursor:pointer;
  border-radius:var(--ym-radius-size-xxxl,6.25rem);font:var(--ym-font-weight-medium,500) var(--ym-font-size-label-m,.875rem)/1 var(--ym-font-text,sans-serif);
  transition:background .15s}
 #ym-save-dialog .ymd-sec{background:var(--ym-controls-color-secondary-default-enabled,rgba(255,255,255,.08));color:var(--ym-controls-color-secondary-on_default-enabled,#e6e6e6)}
 #ym-save-dialog .ymd-sec:hover{background:var(--ym-controls-color-secondary-default-hovered,rgba(255,255,255,.12))}
 #ym-save-dialog .ymd-pri{background:var(--ym-controls-color-primary-default-enabled,#ff0);color:var(--ym-controls-color-primary-on_default-enabled,#000)}
 #ym-save-dialog .ymd-pri:hover{background:var(--ym-controls-color-primary-default-hovered,#dada0b)}
 #ym-save-dialog button:disabled{opacity:.4;cursor:default}
 #ym-save-dialog .ymd-where button{height:48px}
</style>
<div class="ymd" role="dialog" aria-modal="true">
 <div class="ymd-title"></div>
 <label><span class="ymd-label ymd-name-l"></span><input class="ymd-name" spellcheck="false"></label>
 <span class="ymd-label ymd-where-l"></span>
 <div class="ymd-where"><div class="ymd-path"></div><button class="ymd-sec ymd-change"></button></div>
 <div class="ymd-err"></div>
 <div class="ymd-btns"><button></button><button></button><button></button></div>
</div>`;
    const q = (sel) => d.querySelector(sel);
    q('.ymd-title').textContent = T('dlg_title');
    q('.ymd-name-l').textContent = opts.label;
    q('.ymd-where-l').textContent = T('dlg_where_label');
    const input = q('.ymd-name'), pathEl = q('.ymd-path'), change = q('.ymd-change'), errEl = q('.ymd-err');
    input.value = opts.name;
    change.textContent = T('btn_change');
    // the same order as the wrapper's dialogs had: "Cancel", "Save", "Save + numbering";
    // the yellow one is the last choice (Enter presses it too)
    const [cancel, plain, withNum] = q('.ymd-btns').querySelectorAll('button');
    cancel.textContent = T('btn_cancel'); plain.textContent = T('btn_save'); withNum.textContent = T('btn_save_num');
    const def = opts.numbering && numbered;
    for (const b of [cancel, plain, withNum]) b.className = 'ymd-sec';
    (def ? withNum : plain).className = 'ymd-pri';
    if (!opts.numbering) withNum.style.display = 'none';

    let dir = '', busy = false;
    const err = (m) => { errEl.textContent = m || ''; errEl.style.display = m ? 'block' : 'none'; };
    const setDir = (p) => { dir = p; pathEl.textContent = p; pathEl.title = p; err(''); };
    const close = (r) => { d.remove(); resolve(r); };
    fsCall('start', { path: last }).then(setDir, e => err(errText(e)));
    change.onclick = async () => {
      if (busy) return;
      busy = true;
      try { const p = await fsCall('pick', { path: dir, title: T('dlg_pick_title') }); if (p) setDir(p); }
      catch (e) { err(errText(e)); }
      finally { busy = false; input.focus(); }
    };
    const go = (num) => {
      if (busy || !dir || !input.value.trim()) return;
      try { localStorage.setItem('yamu.dir', dir); if (opts.numbering) localStorage.setItem('yamu.numbered', num ? '1' : '0'); } catch (e) {}
      close({ dir, name: safeName(input.value).slice(0, 150), numbered: num });
    };
    // while the folder picker is open the dialog ignores everything: the wrapper's picker window (fallback path)
    // is not attached to the music app window, and without this the dialog would close underneath it
    const abort = () => { if (!busy) close(null); };
    cancel.onclick = abort;
    plain.onclick = () => go(false);
    withNum.onclick = () => go(true);
    d.onclick = (e) => { if (e.target === d) abort(); }; // a click outside the window cancels, like the music app's windows
    d.onkeydown = (e) => { if (e.key === 'Enter') go(def); else if (e.key === 'Escape') abort(); };
    input.oninput = () => { plain.disabled = withNum.disabled = !input.value.trim(); };
    document.body.appendChild(d);
    input.focus(); input.select();
  });

  // ── saving a track or collection: dialog → (collection: new folder) → download → write;
  // the extension follows the file contents ──
  S.save = async (ent) => {
    if (S.jobs.length && S.jobs.every(j => FINAL.includes(j.status))) S.jobs = [];
    resetPanel(T('panel_title_save'));
    S.mode = null; S.total = 0; S.cancelled = false; S.usedNames = new Set();

    let info;
    try { info = await resolveTracks(ent); }
    catch (e) { S.fail(failText(e)); return 'resolve-failed'; }
    S.deadTotal = info.tracks.filter(t => t.dead).length;
    const files = info.tracks.filter(t => t.id && t.id !== 'undefined' && !t.dead);
    if (!files.length) { S.fail(T('err_no_tracks')); return 'empty'; }
    if (files.length > S.maxTracks) { S.fail(T('err_too_many', { n: files.length, max: S.maxTracks })); return 'too-many'; }

    const single = ent.kind === 'track', count = files.length, dead = S.deadTotal;
    const countText = dead > 0 ? T('dlg_count_of', { count, total: count + dead, dead }) : T('dlg_count_tracks', { count });
    const t = await askSave(single
      ? { name: safeName(fileBase(files[0].meta)), label: T('dlg_file_name'), numbering: false }
      : { name: safeName(info.name || ent.name || T('name_collection')).slice(0, 100),
          label: T('dlg_name', { count: countText }).replace(/:\s*$/, ''), numbering: true });
    if (!t) { el.style.display = 'none'; return 'cancel'; }
    let dir = t.dir;
    const numbered = !single && t.numbered;
    if (!single) {
      try { dir = (await fsCall('mkdir', { parent: t.dir, name: t.name })).path; }
      catch (e) { S.fail(T('err_prefix', { msg: errText(e) })); return 'mkdir-failed'; }
    }
    S.setInfo(single ? T('info_file', { name: t.name }) : T('info_folder', { name: dir }));

    const myOp = ++S.opSeq;
    S.working = true;
    S.total = files.length; // the counter's denominator is known before start
    const width = String(files.length).length; // 9→1, 99→01, 999→001
    let pos = 0; // number among available tracks: tracks found unavailable during the download do not take a number
    for (const wn of files) {
      if (S.cancelled || myOp !== S.opSeq) break;
      const j = S.addJob(wn.meta ? fileBase(wn.meta) : T('name_track') + ' ' + wn.id);
      try {
        const buf = await S.downloadAndExtract(wn.id, j);
        if (myOp !== S.opSeq) break;
        S.setJob(j, { status: 'saving', progress: 100 });
        const meta = wn.meta || await dbMeta(wn.id) || { title: 'track_' + wn.id, artists: [] };
        const base = fileBase(meta), ext = S.sniff(buf);
        S.setJob(j, { name: base });
        // a track uses the name from the dialog (a manually typed audio extension is dropped), a collection a name unique in the folder
        let name = single ? safeName(t.name.replace(/\.(mp3|m4a|flac|aac|ogg|wav)$/i, '') + '.' + ext) : S.uniqName(base, meta.album, ext);
        if (numbered) name = String(pos + 1).padStart(width, '0') + '. ' + name;
        const r = await fsCall('write', { dir, name }, buf);
        if (single) S.setInfo(T('info_file', { name: r.name }));
        pos++;
        S.setJob(j, { status: 'done', progress: 100 });
      } catch (e) {
        if (e instanceof DeadTrack) { // unavailable: like the ones known in advance, it goes to "+N✕", not to errors
          S.jobs.splice(S.jobs.indexOf(j), 1); S.total--; S.deadTotal++; S.render();
          continue;
        }
        pos++;
        S.setJob(j, S.cancelled ? { status: 'cancelled' } : { status: 'error', error: errText(e).slice(0, 140) });
        if (!S.cancelled) S.reportSaveError('track ' + wn.id + ' (' + j.name + '): ' + errText(e));
      }
    }
    S.working = false;
    S.render();
    return 'done';
  };

  // Menu item click: in-page save dialog; files are written by the music app's main process or the wrapper service
  S.pickNow = async (ent) => {
    ensurePanel(); el.style.display = 'none';
    if (!SVC.main) { // fallback path: the wrapper service must be alive
      let alive = false;
      try { alive = (await fetch(SVC.url + '/ping', { signal: AbortSignal.timeout(1200) })).ok; } catch (e) {}
      if (!alive) { S.fail(T('err_no_svc')); return; }
    }
    try { await S.save(ent); }
    catch (e) { S.ev('save-err: ' + errText(e)); S.fail(failText(e)); if (!S.cancelled) S.reportSaveError(errText(e)); }
  };

  // ── "Delete downloads": clears the app's download cache for a collection ──
  S.removeDownloads = async (ent) => {
    ensurePanel(); el.style.display = 'none';
    let items;
    try { items = (await resolveTracks(ent)).tracks; }
    catch (e) { resetPanel(T('panel_title_del')); S.fail(failText(e)); return; }
    const dl = [];
    for (const it of items) {
      try { if (await deps.reader.isTrackDownloaded(it.id)) dl.push(it); } catch (e) {}
    }
    // names come from the app database in advance (the tracks are downloaded), so the loop does not pause between tracks
    await Promise.all(dl.map(async it => { if (!it.meta) it.meta = await dbMeta(it.id).catch(() => null); }));
    resetPanel(T('panel_title_del'));
    if (!dl.length) { S.fail(T('err_none_downloaded')); return; }
    S.mode = 'delete'; S.working = true; S.cancelled = false;
    S.jobs = []; S.deadTotal = 0; S.total = dl.length;
    S.setInfo(T('del_info', { n: dl.length }));
    const myOp = ++S.opSeq;
    for (const it of dl) {
      if (S.cancelled || myOp !== S.opSeq) break;
      const j = S.addJob(it.meta ? fileBase(it.meta) : it.id);
      try { S.setJob(j, { status: 'deleting' }); await deps.tc.deleteTrack(it.id); S.setJob(j, { status: 'done', progress: 100 }); }
      catch (e) { S.setJob(j, { status: 'error', error: errText(e).slice(0, 120) }); S.reportSaveError('delete ' + it.id + ': ' + errText(e)); }
    }
    S.working = false; S.mode = null; S.render();
  };

  // ── self-check of the save chain without network and without user files: a track the app has already downloaded
  // is decrypted in memory and its header must be audio (keys and the decryptor work); a no-op call goes through
  // the file bridge (the main process answers). Downloading itself cannot be checked without a real download,
  // its errors are reported by the save operations (saveError) ──
  S.selfCheck = async () => {
    try {
      let checked = false;
      for (const id of (await deps.tc.trackManager.getAllIds()).map(String).slice(0, 20)) {
        if (S.cancelled) return;
        if (!(await deps.reader.isTrackDownloaded(id).catch(() => false))) continue;
        const info = await deps.reader.getLocalFileDownloadInfo(id);
        try {
          const head = new Uint8Array(await (await (await fetch(info.urls[0])).blob()).slice(0, 16).arrayBuffer());
          S.setProblem('decrypt check', S.sniff(head) === 'bin' ? 'track ' + id + ' does not decrypt to audio' : null);
        } finally { URL.revokeObjectURL(info.urls[0]); }
        checked = true;
        break;
      }
      if (!checked) S.setProblem('decrypt check', null); // nothing downloaded yet: nothing to check
    } catch (e) { S.setProblem('decrypt check', errText(e)); }
    try {
      await Promise.race([fsCall('start', { path: '' }), sleep(15000).then(() => { throw new Error('no answer in 15 s'); })]);
      S.setProblem('file bridge check', null);
    } catch (e) { S.setProblem('file bridge check', errText(e)); }
  };
  S.selfCheck();

  return { ok: true };
})()
