// Batch all DOM updates into one microtask (big win when many events arrive)
ko.options.deferUpdates = true;

// ---- Custom binding: flash a card when its value changes (skips first render) ----
ko.bindingHandlers.flash = {
  init(el) { el._seen = false; },
  update(el, valueAccessor) {
    ko.unwrap(valueAccessor());
    if (!el._seen) { el._seen = true; return; }
    el.classList.remove('flash'); void el.offsetWidth; el.classList.add('flash');
  }
};

const STATIONS = ['Assembly', 'Test', 'Packing', 'Shipped'];

// One shared ticker for all "x min ago" labels (instead of one timer per card)
const now = ko.observable(Date.now());
setInterval(() => now(Date.now()), 15000);

function UnitVM(d, fresh) {
  const self = this;
  self.id = d.id;
  self.serial = d.serialNumber;
  self.station = ko.observable(d.station);
  self.isDefect = ko.observable(d.isDefect);
  self.updatedBy = ko.observable(d.updatedBy);
  self.updatedAt = ko.observable(d.updatedAt);
  self.isNew = ko.observable(!!fresh);
  if (fresh) setTimeout(() => self.isNew(false), 1500);

  self.ts = ko.pureComputed(() => Date.parse(self.updatedAt()));
  self.canAdvance = ko.pureComputed(() => !self.isDefect() && self.station() !== 'Shipped');
  self.timeAgo = ko.pureComputed(() => {
    const s = Math.max(0, (now() - self.ts()) / 1000);
    return s < 60 ? 'just now' : s < 3600 ? Math.floor(s / 60) + ' min ago' : Math.floor(s / 3600) + ' h ago';
  });
  self.update = d => {
    self.station(d.station); self.isDefect(d.isDefect);
    self.updatedBy(d.updatedBy); self.updatedAt(d.updatedAt);
  };
}

function DashboardVM() {
  const self = this;
  const byId = new Map();                              // O(1) lookup
  const recentLeft = new Map();                        // to ignore page refreshes in join/leave notifications
  let feedOpen = false;

  self.units = ko.observableArray([]);
  self.users = ko.observableArray([]);
  self.toasts = ko.observableArray([]);
  self.feed = ko.observableArray([]);
  self.unread = ko.observable(0);
  self.newSerial = ko.observable('');
  self.search = ko.observable('').extend({ rateLimit: { timeout: 150, method: 'notifyWhenChangesStop' } });
  self.connection = ko.observable('connecting');
  self.loading = ko.observable(true);
  self.history = ko.observableArray([]);
  self.historyTitle = ko.observable('');
  self.activeTab = ko.observable(STATIONS[0]);        // mobile tab
  self.setTab = c => self.activeTab(c.name);
  self.canAdd = ko.pureComputed(() => self.newSerial().trim().length >= 3);
  self.isGuestName = n => n.startsWith('guest-');

  // Group + filter + sort in ONE pass
  const grouped = ko.pureComputed(() => {
    const q = self.search().trim().toUpperCase();
    const g = {}; STATIONS.forEach(s => g[s] = []);
    for (const u of self.units()) {
      if (q && !u.serial.includes(q)) continue;
      (g[u.station()] || (g[u.station()] = [])).push(u);
    }
    STATIONS.forEach(s => g[s].sort((a, b) => b.ts() - a.ts()));
    return g;
  });
  self.columns = STATIONS.map(name => ({ name, units: ko.pureComputed(() => grouped()[name]) }));

  self.stats = ko.pureComputed(() => {
    let shipped = 0, defects = 0;
    const list = self.units();
    for (const u of list) { if (u.station() === 'Shipped') shipped++; if (u.isDefect()) defects++; }
    return { total: list.length, shipped, defects, inProgress: list.length - shipped };
  });

  // ---- Notifications (toast + activity feed + unread badge) ----
  const notify = (kind, icon, text) => {
    const item = { kind, icon, text, time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) };
    self.feed.unshift(item);
    if (self.feed().length > 30) self.feed.pop();
    if (!feedOpen) self.unread(self.unread() + 1);
    self.toasts.push(item);
    if (self.toasts().length > 3) self.toasts.shift();     // never flood the screen
    setTimeout(() => self.toasts.remove(item), 3500);
  };
  const feedEl = document.getElementById('feedPanel');
  feedEl.addEventListener('shown.bs.offcanvas', () => { feedOpen = true; self.unread(0); });
  feedEl.addEventListener('hidden.bs.offcanvas', () => { feedOpen = false; });

  // If the session is gone (e.g. guest deleted) go back to the landing page
  const sessionLost = r => { if (r.redirected || r.status === 401) { location.href = '/'; return true; } return false; };

  const api = async (url, method = 'POST', body) => {
    const r = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': window.CSRF },
      body: body ? JSON.stringify(body) : undefined
    });
    if (sessionLost(r)) return null;
    if (!r.ok) { notify('danger', 'bi-exclamation-circle', (await r.text()) || 'Error ' + r.status); return null; }
    return r.json();
  };

  self.addUnit = async () => {
    if (await api('/api/units', 'POST', { serialNumber: self.newSerial() })) self.newSerial('');
  };
  self.advance = u => api(`/api/units/${u.id}/advance`);
  self.flag = u => api(`/api/units/${u.id}/defect`);
  self.showHistory = async u => {
    const r = await fetch(`/api/units/${u.id}/history`);
    if (sessionLost(r)) return;
    const list = await r.json();
    self.historyTitle(u.serial);
    self.history(list.map(e => ({ action: e.action, user: e.user, when: new Date(e.at).toLocaleString() })));
    bootstrap.Modal.getOrCreateInstance(document.getElementById('historyModal')).show();
  };

  // ---- ONE delegated click listener for every card button ----
  document.getElementById('board').addEventListener('click', async e => {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const u = byId.get(+btn.closest('.unit-card').dataset.id);
    if (!u) return;
    btn.disabled = true;                                    // prevent double clicks
    try {
      const act = btn.dataset.act;
      if (act === 'advance') await self.advance(u);
      else if (act === 'flag') await self.flag(u);
      else if (act === 'history') await self.showHistory(u);
    } finally { btn.disabled = false; }
  });

  // ---- SignalR ----
  const hub = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/trace')
    .withAutomaticReconnect()
    .build();

  hub.on('UnitChanged', d => {
    const existing = byId.get(d.id);
    if (existing) existing.update(d);
    else { const vm = new UnitVM(d, true); byId.set(d.id, vm); self.units.unshift(vm); }
    if (d.updatedBy !== window.ME)                          // no notification for my own actions
      notify(d.isDefect ? 'danger' : 'unit', d.isDefect ? 'bi-exclamation-triangle' : 'bi-arrow-right-circle',
             `${d.serialNumber} → ${d.station}${d.isDefect ? ' (DEFECT)' : ''} by ${d.updatedBy}`);
  });

  hub.on('PresenceChanged', list => self.users(list));

  // Someone else just logged in. Ignore a refresh (left < 6 s ago).
  hub.on('UserJoined', (name, isGuest) => {
    const t = recentLeft.get(name);
    if (t && Date.now() - t < 6000) { recentLeft.delete(name); return; }
    notify('join', 'bi-box-arrow-in-right', `${name}${isGuest ? ' (guest)' : ''} just logged in`);
  });
  hub.on('UserLeft', name => {
    recentLeft.set(name, Date.now());
    setTimeout(() => {
      if (recentLeft.delete(name) && !self.users().includes(name))
        notify('leave', 'bi-box-arrow-left', `${name} left`);
    }, 6000);
  });

  hub.onreconnecting(() => self.connection('reconnecting'));
  hub.onreconnected(() => { self.connection('connected'); notify('info', 'bi-wifi', 'Back online'); self.load(); });
  hub.onclose(() => self.connection('disconnected'));

  self.load = async () => {
    const r = await fetch('/api/units');
    if (sessionLost(r)) return;
    const data = await r.json();
    byId.clear();
    const vms = data.map(d => { const vm = new UnitVM(d); byId.set(d.id, vm); return vm; });
    self.units(vms);                                        // single write = single re-render
    self.loading(false);
  };

  hub.start().then(() => { self.connection('connected'); return self.load(); })
     .catch(() => { self.connection('disconnected'); self.loading(false); });
}

ko.applyBindings(new DashboardVM());

// Keyboard shortcut: "/" focuses the search box
document.addEventListener('keydown', e => {
  if (e.key === '/' && !/INPUT|TEXTAREA/.test(document.activeElement.tagName)) {
    e.preventDefault(); document.getElementById('search').focus();
  }
});
