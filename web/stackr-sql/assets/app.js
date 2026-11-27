// StackR SQL — interface (vanilla JS). Tout texte venant du serveur passe par textContent.
(function () {
  'use strict';
  var csrf = document.querySelector('meta[name="csrf"]').content;
  var body = document.body;
  var view = document.getElementById('view');
  var tree = document.getElementById('tree');
  var crumbs = document.getElementById('crumbs');
  var tabs = document.getElementById('tabs');
  var state = { databases: [], db: null, table: null, tab: 'overview', tables: {}, sort: null, dir: 'asc', page: 1, sql: '' };

  // --- Utilitaires ---------------------------------------------------------------------------
  function h(tag, attrs) {
    var el = document.createElement(tag);
    if (attrs) {
      Object.keys(attrs).forEach(function (k) {
        var v = attrs[k];
        if (v === null || v === undefined || v === false) return;
        if (k === 'text') el.textContent = v;
        else if (k === 'class') el.className = v;
        else if (k.slice(0, 2) === 'on') el.addEventListener(k.slice(2), v);
        else el.setAttribute(k, v === true ? '' : v);
      });
    }
    for (var i = 2; i < arguments.length; i++) {
      var c = arguments[i];
      if (c === null || c === undefined || c === false) continue;
      (Array.isArray(c) ? c : [c]).forEach(function (x) {
        if (x !== null && x !== undefined && x !== false) el.appendChild(typeof x === 'string' ? document.createTextNode(x) : x);
      });
    }
    return el;
  }
  function size(n) {
    if (!n) return '0 o';
    var u = ['o', 'Ko', 'Mo', 'Go', 'To'], i = 0;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return (i ? n.toFixed(n < 10 ? 1 : 0) : n) + ' ' + u[i];
  }
  function num(n) { return n === null || n === undefined ? '—' : Number(n).toLocaleString('fr-FR'); }
  function toast(msg, kind) {
    var t = h('div', { class: 'toast ' + (kind || ''), text: msg, role: kind === 'error' ? 'alert' : 'status' });
    document.getElementById('toasts').appendChild(t);
    setTimeout(function () { t.classList.add('out'); setTimeout(function () { t.remove(); }, 260); }, kind === 'error' ? 6000 : 3200);
  }
  function api(op, data) {
    var payload = Object.assign({ op: op }, data || {});
    return fetch('?action=api', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf },
      body: JSON.stringify(payload)
    }).then(function (r) {
      return r.json().catch(function () { return { error: 'Réponse invalide du serveur (' + r.status + ').' }; }).then(function (j) {
        if (j && j.login) { location.reload(); }
        if (!r.ok || (j && j.error)) { var e = new Error((j && j.error) || ('Erreur ' + r.status)); throw e; }
        return j;
      });
    });
  }
  function swap(node) {
    view.replaceChildren(node);
    node.classList.add('fade-in');
  }
  function skeleton() {
    return h('div', null, h('div', { class: 'skel', style: null }), h('div', { class: 'skel block' }));
  }
  function isNum(v) { return typeof v === 'string' && /^-?\d+(\.\d+)?$/.test(v) && v.length < 20; }

  // --- Navigation ----------------------------------------------------------------------------
  function setUrl() {
    var url = state.db ? '?db=' + encodeURIComponent(state.db) : location.pathname;
    try { history.replaceState(null, '', url); } catch (e) { /* ignoré */ }
  }
  function updateChrome() {
    crumbs.replaceChildren();
    if (!state.db) crumbs.appendChild(h('span', { class: 'muted', text: 'Choisis une base de données' }));
    else {
      crumbs.appendChild(h('span', { text: state.db }));
      if (state.table) { crumbs.appendChild(h('span', { class: 'sep', text: '/' })); crumbs.appendChild(h('span', { text: state.table })); }
    }
    tabs.querySelectorAll('button').forEach(function (b) {
      var t = b.dataset.tab;
      b.disabled = (t === 'data' || t === 'structure') && !state.table;
      b.classList.toggle('active', t === state.tab);
      b.setAttribute('aria-selected', t === state.tab ? 'true' : 'false');
    });
    tree.querySelectorAll('.node').forEach(function (n) {
      var active = n.dataset.table ? (n.dataset.db === state.db && n.dataset.table === state.table) : (n.dataset.db === state.db && !state.table);
      n.classList.toggle('active', active);
    });
    setUrl();
  }
  function go(tab) {
    state.tab = tab;
    updateChrome();
    if (tab === 'overview') renderOverview();
    else if (tab === 'data') renderData();
    else if (tab === 'structure') renderStructure();
    else renderSql();
  }
  tabs.addEventListener('click', function (ev) {
    var b = ev.target.closest('button');
    if (b && !b.disabled) go(b.dataset.tab);
  });

  function openDb(name) {
    state.db = name; state.table = null;
    expand(name, true);
    go('overview');
  }
  function openTable(db, table) {
    state.db = db; state.table = table; state.sort = null; state.dir = 'asc'; state.page = 1;
    go('data');
  }

  // --- Barre latérale -----------------------------------------------------------------------
  function loadDatabases() {
    return api('databases').then(function (j) {
      state.databases = j.databases;
      renderTree();
    }).catch(function (e) { tree.replaceChildren(h('div', { class: 'empty-hint', text: e.message })); });
  }
  function renderTree() {
    var f = document.getElementById('filter').value.trim().toLowerCase();
    tree.replaceChildren();
    state.databases.forEach(function (d) {
      var tbls = state.tables[d.name];
      var tMatch = tbls ? tbls.filter(function (t) { return !f || t.name.toLowerCase().indexOf(f) >= 0; }) : [];
      if (f && d.name.toLowerCase().indexOf(f) < 0 && !tMatch.length) return;
      var item = h('div', { class: 'db-item' + (d.open ? ' open' : '') });
      var node = h('button', { class: 'node' + (d.system ? ' system' : ''), 'data-db': d.name, title: d.name + ' — ' + d.collation, onclick: function () {
        if (state.db === d.name && !state.table) { d.open = !d.open; item.classList.toggle('open', d.open); return; }
        openDb(d.name);
      } },
        h('span', { class: 'chev', text: '›' }), h('span', { class: 'dot' }), h('span', { class: 'name', text: d.name }), h('span', { class: 'meta', text: size(d.size) }));
      var inner = h('div');
      if (tbls) {
        (f ? tMatch : tbls).forEach(function (t) {
          inner.appendChild(h('button', { class: 'node', 'data-db': d.name, 'data-table': t.name, title: t.name, onclick: function () { openTable(d.name, t.name); } },
            h('span', { class: 'tico' }), h('span', { class: 'name', text: t.name }), h('span', { class: 'meta', text: t.view ? 'vue' : num(t.rows) })));
        });
        if (!tbls.length) inner.appendChild(h('div', { class: 'empty-hint', text: 'Aucune table' }));
      } else if (d.open) inner.appendChild(h('div', { class: 'skeleton-list' }, h('i'), h('i')));
      item.appendChild(node);
      item.appendChild(h('div', { class: 'tables' }, inner));
      tree.appendChild(item);
    });
    if (!tree.children.length) tree.appendChild(h('div', { class: 'empty-hint', text: 'Aucun résultat' }));
    updateChrome();
  }
  function expand(name, force) {
    var d = state.databases.find(function (x) { return x.name === name; });
    if (!d) return Promise.resolve();
    d.open = force ? true : !d.open;
    renderTree();
    return loadTables(name);
  }
  function loadTables(name) {
    return api('tables', { db: name }).then(function (j) {
      state.tables[name] = j.tables;
      var d = state.databases.find(function (x) { return x.name === name; });
      if (d) Object.assign(d, j.db, { open: d.open });
      renderTree();
      return j;
    });
  }
  document.getElementById('filter').addEventListener('input', renderTree);
  document.getElementById('new-db').addEventListener('click', createDbDialog);

  // --- Dialogues -------------------------------------------------------------------------------
  var dialog = document.getElementById('dialog');
  function showDialog(content) { dialog.replaceChildren(content); dialog.showModal(); }
  function createDbDialog() {
    var input = h('input', { placeholder: 'mon_projet', required: true, pattern: '[A-Za-z0-9_$\\-]{1,64}', maxlength: '64' });
    var form = h('form', { method: 'dialog', onsubmit: function (ev) {
      ev.preventDefault();
      api('createDb', { name: input.value.trim() }).then(function (j) {
        dialog.close(); toast('Base « ' + j.created + ' » créée.', 'ok');
        return loadDatabases().then(function () { openDb(j.created); });
      }).catch(function (e) { toast(e.message, 'error'); });
    } },
      h('h3', { text: 'Nouvelle base de données' }),
      h('p', { text: 'Encodage utf8mb4, interclassement utf8mb4_unicode_ci.' }),
      h('label', null, 'Nom', input),
      h('div', { class: 'actions' }, h('button', { type: 'button', class: 'btn', text: 'Annuler', onclick: function () { dialog.close(); } }), h('button', { type: 'submit', class: 'btn primary', text: 'Créer' })));
    showDialog(form);
    input.focus();
  }
  function dropDbDialog(name) {
    var go2 = h('button', { type: 'submit', class: 'btn danger', text: 'Supprimer définitivement', disabled: true });
    var input = h('input', { autocomplete: 'off', spellcheck: 'false', oninput: function () { go2.disabled = input.value !== name; } });
    var form = h('form', { method: 'dialog', onsubmit: function (ev) {
      ev.preventDefault();
      api('dropDb', { db: name, confirm: input.value }).then(function () {
        dialog.close(); toast('Base « ' + name + ' » supprimée.', 'ok');
        delete state.tables[name]; state.db = null; state.table = null;
        loadDatabases().then(function () { go('overview'); });
      }).catch(function (e) { toast(e.message, 'error'); });
    } },
      h('h3', { text: 'Supprimer « ' + name + ' » ?' }),
      h('p', { text: 'Toutes ses tables et données seront effacées. Cette action est irréversible. Tape le nom de la base pour confirmer.' }),
      h('label', null, 'Nom de la base', input),
      h('div', { class: 'actions' }, h('button', { type: 'button', class: 'btn', text: 'Annuler', onclick: function () { dialog.close(); } }), go2));
    showDialog(form);
    input.focus();
  }

  // --- Vue d'ensemble ---------------------------------------------------------------------------
  function renderOverview() {
    if (!state.db) {
      var total = state.databases.reduce(function (a, d) { return a + d.size; }, 0);
      var user = state.databases.filter(function (d) { return !d.system; });
      var grid = h('div', { class: 'grid-cards' });
      user.forEach(function (d) {
        grid.appendChild(h('button', { class: 'card tcard', onclick: function () { openDb(d.name); } },
          h('strong', { text: d.name }), h('small', { text: num(d.tables) + ' tables · ' + size(d.size) })));
      });
      swap(h('div', null,
        h('div', { class: 'card hero' },
          h('div', { class: 'head' }, h('div', null, h('h2', { text: 'Bienvenue dans StackR SQL' }), h('p', { class: 'muted', text: 'Choisis une base à gauche, ou crée-en une nouvelle.' })),
            h('button', { class: 'btn primary', text: 'Nouvelle base', onclick: createDbDialog })),
          h('div', { class: 'stats' }, h('div', { class: 'stat' }, h('span', { text: 'Bases' }), h('strong', { text: num(user.length) })),
            h('div', { class: 'stat' }, h('span', { text: 'Taille totale' }), h('strong', { text: size(total) })))),
        user.length ? h('h3', { class: 'section-title', text: 'Tes bases' }) : h('div', { class: 'empty' }, h('h2', { text: 'Aucune base pour l\'instant' }), h('p', { text: 'Crée ta première base avec le bouton « Nouvelle base ».' })),
        grid));
      return;
    }
    var db = state.db;
    swap(skeleton());
    var p = state.tables[db] ? Promise.resolve({ tables: state.tables[db], db: state.databases.find(function (x) { return x.name === db; }) }) : loadTables(db);
    p.then(function (j) {
      if (state.db !== db || state.tab !== 'overview') return;
      var info = j.db || {};
      var grid = h('div', { class: 'grid-cards' });
      j.tables.forEach(function (t) {
        grid.appendChild(h('button', { class: 'card tcard', onclick: function () { openTable(db, t.name); } },
          h('strong', { text: t.name }), h('small', { text: (t.view ? 'vue' : num(t.rows) + ' lignes · ' + size(t.size)) + (t.engine ? ' · ' + t.engine : '') })));
      });
      var dbRef = state.databases.find(function (x) { return x.name === db; }) || {};
      swap(h('div', null,
        h('div', { class: 'card hero' },
          h('div', { class: 'head' }, h('div', null, h('h2', { text: db }), h('p', { class: 'muted', text: 'Base de données MySQL' })),
            h('div', null, h('button', { class: 'btn', text: 'Console SQL', onclick: function () { go('sql'); } }), ' ',
              dbRef.system ? null : h('button', { class: 'btn ghost', text: 'Supprimer…', onclick: function () { dropDbDialog(db); } }))),
          h('div', { class: 'stats' },
            h('div', { class: 'stat' }, h('span', { text: 'Taille' }), h('strong', { text: size(info.size) })),
            h('div', { class: 'stat' }, h('span', { text: 'Tables' }), h('strong', { text: num(j.tables.length) })),
            h('div', { class: 'stat' }, h('span', { text: 'Interclassement' }), h('strong', { class: 'mono', text: info.collation || '—' })))),
        j.tables.length ? h('h3', { class: 'section-title', text: 'Tables' }) : h('div', { class: 'empty' }, h('h2', { text: 'Base vide' }), h('p', { text: 'Crée une table depuis la console SQL.' })),
        grid));
    }).catch(function (e) { swap(h('div', { class: 'error-box', text: e.message })); });
  }

  // --- Données ---------------------------------------------------------------------------------
  function renderData() {
    var db = state.db, table = state.table;
    if (!view.querySelector('.grid-wrap')) swap(skeleton());
    api('browse', { db: db, table: table, page: state.page, sort: state.sort, dir: state.dir }).then(function (j) {
      if (state.db !== db || state.table !== table || state.tab !== 'data') return;
      state.page = j.page;
      var head = h('tr', null, h('th', { text: '#' }));
      j.columns.forEach(function (c) {
        var arrow = j.sort === c ? h('span', { class: 'arrow', text: j.dir === 'desc' ? '↓' : '↑' }) : null;
        head.appendChild(h('th', { class: 'sortable', title: 'Trier par ' + c, onclick: function () {
          if (state.sort === c) state.dir = state.dir === 'asc' ? 'desc' : 'asc'; else { state.sort = c; state.dir = 'asc'; }
          renderData();
        } }, c, arrow));
      });
      var tbody = h('tbody');
      var offset = (j.page - 1) * j.pageSize;
      j.rows.forEach(function (r, i) {
        var tr = h('tr', null, h('td', { class: 'rownum', text: String(offset + i + 1) }));
        r.forEach(function (v) {
          tr.appendChild(v === null ? h('td', { class: 'null', text: 'NULL' }) : h('td', { class: isNum(v) ? 'num' : '', title: v.length > 40 ? v : null, text: v }));
        });
        tbody.appendChild(tr);
      });
      var gridEl = j.rows.length ? h('div', { class: 'card grid-wrap' }, h('table', { class: 'grid' }, h('thead', null, head), tbody))
        : h('div', { class: 'card empty' }, h('h2', { text: 'Table vide' }), h('p', { text: 'Aucune ligne à afficher.' }));
      var pager = h('div', { class: 'pager' },
        h('button', { class: 'btn small', text: '‹ Précédent', disabled: j.page <= 1, onclick: function () { state.page--; renderData(); } }),
        h('span', { text: 'Page ' + j.page + ' / ' + j.pages }),
        h('button', { class: 'btn small', text: 'Suivant ›', disabled: j.page >= j.pages, onclick: function () { state.page++; renderData(); } }));
      var node = h('div', null,
        h('div', { class: 'toolbar' }, h('span', { class: 'pill ok', text: num(j.total) + ' lignes' }), pager, h('span', { class: 'spacer' }),
          h('button', { class: 'btn small', text: 'Exporter SQL', onclick: function () { exportTable('sql'); } }),
          h('button', { class: 'btn small', text: 'Exporter CSV', onclick: function () { exportTable('csv'); } })),
        gridEl);
      var keep = view.querySelector('.grid-wrap');
      if (keep) { view.replaceChildren(node); } else swap(node);
    }).catch(function (e) { swap(h('div', { class: 'error-box', text: e.message })); });
  }
  function exportTable(format) {
    var f = document.getElementById('export-form');
    f.elements.db.value = state.db; f.elements.table.value = state.table; f.elements.format.value = format;
    f.submit();
    toast('Export ' + format.toUpperCase() + ' en cours de téléchargement…');
  }

  // --- Structure -------------------------------------------------------------------------------
  function renderStructure() {
    var db = state.db, table = state.table;
    swap(skeleton());
    api('structure', { db: db, table: table }).then(function (j) {
      if (state.db !== db || state.table !== table || state.tab !== 'structure') return;
      var cols = h('tbody');
      j.columns.forEach(function (c) {
        var key = c.col_key === 'PRI' ? h('span', { class: 'pill pk', text: 'PRIMAIRE' }) : c.col_key === 'UNI' ? h('span', { class: 'pill', text: 'UNIQUE' }) : c.col_key === 'MUL' ? h('span', { class: 'pill', text: 'INDEX' }) : null;
        cols.appendChild(h('tr', null,
          h('td', null, h('strong', { text: c.name })), h('td', { class: 'mono', text: c.type }),
          h('td', { text: c.nullable === 'YES' ? 'oui' : 'non' }),
          c.def === null ? h('td', { class: 'null', text: 'NULL' }) : h('td', { class: 'mono', text: c.def }),
          h('td', null, key), h('td', { class: 'muted', text: c.extra || '' }), h('td', { class: 'muted', text: c.comment || '' })));
      });
      var idx = h('tbody');
      j.indexes.forEach(function (i) {
        idx.appendChild(h('tr', null, h('td', null, h('strong', { text: i.name })), h('td', { class: 'mono', text: i.columns }),
          h('td', null, h('span', { class: 'pill' + (i.name === 'PRIMARY' ? ' pk' : ''), text: i.name === 'PRIMARY' ? 'PRIMAIRE' : i.unique ? 'UNIQUE' : 'INDEX' })), h('td', { class: 'muted', text: i.type })));
      });
      function th(labels) { var tr = h('tr'); labels.forEach(function (l) { tr.appendChild(h('th', { text: l })); }); return h('thead', null, tr); }
      swap(h('div', null,
        h('h3', { class: 'section-title', text: 'Colonnes (' + j.columns.length + ')' }),
        h('div', { class: 'card grid-wrap' }, h('table', { class: 'grid' }, th(['Nom', 'Type', 'Nul', 'Défaut', 'Clé', 'Extra', 'Commentaire']), cols)),
        h('h3', { class: 'section-title', text: 'Index (' + j.indexes.length + ')', style: null }),
        j.indexes.length ? h('div', { class: 'card grid-wrap' }, h('table', { class: 'grid' }, th(['Nom', 'Colonnes', 'Genre', 'Type']), idx)) : h('p', { class: 'muted', text: 'Aucun index.' })));
      var titles = view.querySelectorAll('.section-title');
      if (titles[1]) titles[1].style.marginTop = '24px';
    }).catch(function (e) { swap(h('div', { class: 'error-box', text: e.message })); });
  }

  // --- Console SQL -----------------------------------------------------------------------------
  var KW = /\b(SELECT|FROM|WHERE|AND|OR|NOT|NULL|IS|IN|LIKE|BETWEEN|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|DATABASE|DROP|ALTER|ADD|COLUMN|INDEX|PRIMARY|KEY|FOREIGN|REFERENCES|UNIQUE|DEFAULT|AUTO_INCREMENT|JOIN|LEFT|RIGHT|INNER|OUTER|ON|AS|GROUP|BY|ORDER|ASC|DESC|LIMIT|OFFSET|HAVING|DISTINCT|COUNT|SUM|AVG|MIN|MAX|UNION|ALL|SHOW|TABLES|DATABASES|DESCRIBE|EXPLAIN|USE|INT|BIGINT|VARCHAR|TEXT|DATETIME|TIMESTAMP|DATE|DECIMAL|BOOLEAN|CASE|WHEN|THEN|ELSE|END|IF|EXISTS|TRUNCATE|ENGINE|CHARSET|COLLATE|WITH|CHARACTER|UNSIGNED|CURRENT_TIMESTAMP|NOW)\b/i;
  var TOKENS = /(--[^\n]*|#[^\n]*|\/\*[\s\S]*?\*\/)|('(?:[^'\\]|\\.)*'?|"(?:[^"\\]|\\.)*"?)|(\b\d+(?:\.\d+)?\b)|(\b[A-Za-z_]+\b)/g;
  function highlight(pre, text) {
    pre.replaceChildren();
    var last = 0, m;
    TOKENS.lastIndex = 0;
    while ((m = TOKENS.exec(text)) !== null) {
      if (m.index > last) pre.appendChild(document.createTextNode(text.slice(last, m.index)));
      var cls = m[1] ? 'tok-com' : m[2] ? 'tok-str' : m[3] ? 'tok-num' : (m[4] && KW.test(m[4]) ? 'tok-kw' : null);
      pre.appendChild(cls ? h('span', { class: cls, text: m[0] }) : document.createTextNode(m[0]));
      last = m.index + m[0].length;
    }
    pre.appendChild(document.createTextNode(text.slice(last) + '\n'));
  }
  function renderSql() {
    var pre = h('pre', { 'aria-hidden': 'true' });
    var ta = h('textarea', { spellcheck: 'false', 'aria-label': 'Requête SQL', placeholder: state.table ? 'SELECT * FROM `' + state.table + '` LIMIT 10' : 'SHOW DATABASES' });
    ta.value = state.sql;
    var out = h('div');
    var dbSel = h('select', { 'aria-label': 'Base utilisée' }, h('option', { value: '', text: '(aucune base)' }));
    state.databases.forEach(function (d) { dbSel.appendChild(h('option', { value: d.name, text: d.name, selected: d.name === state.db })); });
    function sync() { state.sql = ta.value; highlight(pre, ta.value); pre.scrollTop = ta.scrollTop; }
    ta.addEventListener('input', sync);
    ta.addEventListener('scroll', function () { pre.scrollTop = ta.scrollTop; });
    var runBtn = h('button', { class: 'btn primary', text: 'Exécuter', onclick: run });
    ta.addEventListener('keydown', function (ev) {
      if ((ev.ctrlKey || ev.metaKey) && ev.key === 'Enter') { ev.preventDefault(); run(); }
      else if (ev.key === 'Tab' && !ev.shiftKey) { ev.preventDefault(); document.execCommand ? document.execCommand('insertText', false, '    ') : null; }
    });
    function run() {
      var sql = ta.value.trim() || ta.placeholder;
      runBtn.disabled = true; runBtn.textContent = 'Exécution…';
      out.replaceChildren(h('div', { class: 'skel block' }));
      api('query', { db: dbSel.value || null, sql: sql }).then(function (j) {
        var meta = h('div', { class: 'result-meta' }, h('span', { class: 'pill ok', text: j.kind === 'rows' ? num(j.rows.length) + ' ligne(s)' + (j.truncated ? ' (limitées à ' + j.limit + ')' : '') : num(j.affected) + ' ligne(s) affectée(s)' }),
          h('span', { class: 'pill', text: j.ms.toLocaleString('fr-FR') + ' ms' }));
        if (j.kind !== 'rows') { out.replaceChildren(meta); out.classList.add('fade-in'); loadDatabases(); if (state.db) delete state.tables[state.db]; return; }
        var head = h('tr'); j.columns.forEach(function (c) { head.appendChild(h('th', { text: c })); });
        var tb = h('tbody');
        j.rows.forEach(function (r) {
          var tr = h('tr');
          r.forEach(function (v) { tr.appendChild(v === null ? h('td', { class: 'null', text: 'NULL' }) : h('td', { class: isNum(v) ? 'num' : '', text: v })); });
          tb.appendChild(tr);
        });
        out.replaceChildren(meta, h('div', { class: 'card grid-wrap fade-in' }, h('table', { class: 'grid' }, h('thead', null, head), tb)));
      }).catch(function (e) {
        out.replaceChildren(h('div', { class: 'error-box fade-in', role: 'alert', text: e.message }));
      }).finally(function () { runBtn.disabled = false; runBtn.textContent = 'Exécuter'; });
    }
    swap(h('div', null,
      h('div', { class: 'toolbar' }, dbSel, h('span', { class: 'spacer' }), h('span', { class: 'muted' }, h('span', { class: 'kbd', text: 'Ctrl' }), ' + ', h('span', { class: 'kbd', text: 'Entrée' })), runBtn),
      h('div', { class: 'editor-shell' }, h('div', { class: 'editor' }, pre, ta)),
      out));
    dbSel.style.width = 'auto';
    sync();
    ta.focus();
  }

  // --- Démarrage -------------------------------------------------------------------------------
  var initial = body.dataset.initialDb;
  if (body.dataset.notice) toast(body.dataset.notice, 'error');
  loadDatabases().then(function () {
    if (initial && state.databases.some(function (d) { return d.name === initial; })) openDb(initial);
    else go('overview');
  });
})();
