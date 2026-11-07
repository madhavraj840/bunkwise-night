(function () {
  var root = document.documentElement, b = document.body;
  function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); } catch (e) {} }
  var t = store('nm-theme');
  if (!t) t = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  root.setAttribute('data-theme', t);
  var tb = document.getElementById('themeBtn');
  if (tb) tb.onclick = function () { t = t === 'dark' ? 'light' : 'dark'; root.setAttribute('data-theme', t); store('nm-theme', t); };
  var bg = document.getElementById('burger');
  if (bg) bg.onclick = function () { b.classList.toggle('nav-open'); };
  var q = document.getElementById('q'), box = document.getElementById('results'), idx = null;
  if (!q) return;
  function load(cb) {
    if (idx) return cb();
    fetch('/assets/search.json').then(function (r) { return r.json(); }).then(function (j) { idx = j; cb(); }).catch(function () { idx = []; cb(); });
  }
  function run() {
    var words = q.value.toLowerCase().split(/\s+/).filter(function (w) { return w.length > 1; });
    if (!words.length) { box.classList.remove('open'); return; }
    var hits = [];
    idx.forEach(function (d) {
      var tl = d.t.toLowerCase(), tx = d.x.toLowerCase(), s = 0;
      for (var i = 0; i < words.length; i++) {
        var w = words[i];
        if (tl.indexOf(w) > -1) s += 10;
        if (tx.indexOf(w) > -1) s += 2; else { s = 0; break; }
      }
      if (s) hits.push({ d: d, s: s });
    });
    hits.sort(function (a, c) { return c.s - a.s; });
    box.innerHTML = hits.length ? hits.slice(0, 8).map(function (h) {
      var p = h.d.x.toLowerCase().indexOf(words[0]), sn = h.d.x.substr(Math.max(0, p - 40), 120);
      return '<a href="' + h.d.u + '"><b>' + h.d.t + '</b><small>' + sn.replace(/</g, '&lt;') + '…</small></a>';
    }).join('') : '<a href="#" onclick="return false"><small>No results found</small></a>';
    box.classList.add('open');
  }
  q.addEventListener('input', function () { load(run); });
  q.addEventListener('focus', function () { load(run); });
  document.addEventListener('click', function (e) { if (!e.target.closest('.md-search')) box.classList.remove('open'); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { box.classList.remove('open'); q.blur(); } });
})();
