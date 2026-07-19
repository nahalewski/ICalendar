/* FamilyHub browser dashboard.
 *
 * Written for a Raspberry Pi Zero W: single-core ARMv6, 512MB, running Chromium. That budget drives
 * every decision here — no framework, no build step, no polyfills, and above all no per-second
 * network traffic. The server sends block times as minutes past midnight and the browser computes
 * progress locally, so the ticking indicator costs one small DOM write per second instead of a
 * request. Full state is refetched slowly; news slower still.
 */
'use strict';

var STATE_POLL_MS = 30000;   // full dashboard state
var NEWS_POLL_MS = 600000;   // headlines change slowly and the payload is the largest
var ROTATE_MS = 90000;       // fallback rotation when the server has not pinned a screen

var PAGES = ['Daily', 'Weekly', 'Monthly', 'Weather', 'ClassBlock', 'News'];
var LABELS = {
  Daily: 'Today', Weekly: 'Week', Monthly: 'Month',
  Weather: 'Weather', ClassBlock: 'Class Block', News: 'News'
};

var state = null;
var news = [];
var page = 'Daily';
var manualUntil = 0;       // pause auto-rotation briefly after a tap
var lastServerScreen = null;
var failures = 0;

function $(id) { return document.getElementById(id); }
function el(tag, cls, text) {
  var n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined && text !== null) n.textContent = text;
  return n;
}
function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }

/* Navigation ---------------------------------------------------------------------------------- */

function buildNav() {
  var nav = $('nav');
  clear(nav);
  PAGES.forEach(function (name) {
    var b = el('button', null, LABELS[name]);
    b.onclick = function () { show(name); manualUntil = Date.now() + 120000; };
    b.setAttribute('data-page', name);
    nav.appendChild(b);
  });
}

function show(name) {
  page = name;
  PAGES.forEach(function (p) {
    var section = $('page-' + p);
    if (section) section.className = 'page' + (p === name ? ' on' : '');
  });
  var buttons = document.querySelectorAll('nav button');
  for (var i = 0; i < buttons.length; i++) {
    buttons[i].className = buttons[i].getAttribute('data-page') === name ? 'on' : '';
  }
}

/* Fetch --------------------------------------------------------------------------------------- */

function getJson(url, onOk) {
  var xhr = new XMLHttpRequest();
  xhr.open('GET', url + '?t=' + Date.now(), true);
  xhr.timeout = 15000;
  xhr.onreadystatechange = function () {
    if (xhr.readyState !== 4) return;
    if (xhr.status >= 200 && xhr.status < 300) {
      failures = 0;
      $('offline').className = 'banner hidden';
      try { onOk(JSON.parse(xhr.responseText)); } catch (e) { /* keep last good render */ }
    } else {
      markOffline();
    }
  };
  xhr.ontimeout = xhr.onerror = markOffline;
  xhr.send();
}

function markOffline() {
  failures++;
  // One blip on a domestic wi-fi link is not worth shouting about; a sustained outage is.
  if (failures >= 3) $('offline').className = 'banner';
}

function pollState() {
  getJson('/api/view/state', function (data) {
    state = data;
    render();
  });
}

function pollNews() {
  getJson('/api/view/news', function (data) {
    news = (data && data.stories) || [];
    renderNews();
  });
}

/* Render -------------------------------------------------------------------------------------- */

function render() {
  if (!state) return;

  $('date').textContent = state.todayLongDate || '';
  $('headNote').textContent = state.todayNote || '';
  $('monthTitle').textContent = state.monthTitle || '';
  $('weekTitle').textContent = state.weekTitle || '';
  $('cbHeader').textContent = state.classBlockHeader || '';

  var w = state.weather;
  if (w) {
    $('pillIcon').textContent = w.icon;
    $('pillTemp').textContent = w.temperature;
    $('pillLoc').textContent = w.location;
  }

  renderToday();
  renderMonth();
  renderWeek();
  renderWeather();
  renderStudents();

  // The Android remote pins a screen; follow it when it changes, otherwise keep rotating.
  if (state.currentScreen && state.currentScreen !== lastServerScreen) {
    lastServerScreen = state.currentScreen;
    if (PAGES.indexOf(state.currentScreen) >= 0) show(state.currentScreen);
  }

  $('status').textContent = 'Updated ' + new Date().toLocaleTimeString() +
    ' • serving from FamilyHub';
}

function renderToday() {
  var note = $('todayNote');
  if (state.todayNote) { note.textContent = state.todayNote; note.className = 'callout'; }
  else { note.className = 'callout hidden'; }

  var list = $('agenda');
  clear(list);
  var items = state.agenda || [];
  if (!items.length) { list.appendChild(el('div', 'empty', 'Nothing scheduled today.')); return; }
  items.forEach(function (a) {
    var row = el('div', 'item');
    row.style.borderLeft = '5px solid ' + (a.color || '#ddd');
    row.appendChild(el('div', 'when', a.time));
    row.appendChild(el('div', null, a.title));
    if (a.person || a.location) {
      row.appendChild(el('div', 'muted small', [a.person, a.location].filter(Boolean).join(' • ')));
    }
    list.appendChild(row);
  });
}

function renderMonth() {
  var grid = $('monthGrid');
  clear(grid);
  (state.calendarDays || []).forEach(function (d) {
    var cell = el('div', 'cell' + (d.isCurrentMonth ? '' : ' dim') + (d.isToday ? ' today' : ''));
    cell.appendChild(el('div', 'n', d.day));
    (d.chips || []).forEach(function (c) {
      var chip = el('div', 'chip', c.text);
      chip.style.background = c.color;
      cell.appendChild(chip);
    });
    grid.appendChild(cell);
  });
}

function renderWeek() {
  var grid = $('weekGrid');
  clear(grid);
  var names = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  names.forEach(function (n) {
    var col = el('div', 'col');
    col.appendChild(el('h3', null, n));
    grid.appendChild(col);
  });
}

function renderWeather() {
  var w = state.weather;
  if (!w) return;
  $('wxIcon').textContent = w.icon;
  $('wxTemp').textContent = w.temperature;
  $('wxCond').textContent = w.condition;
  $('wxFeels').textContent = w.feelsLike;
  $('wxLoc').textContent = w.location;
  $('wxStatus').textContent = w.status;

  var stats = $('wxStats');
  clear(stats);
  [['Humidity', w.humidity], ['Wind', w.wind], ['Gust', w.gust],
   ['Cloud', w.cloudCover], ['UV', w.uv], ['Sunrise', w.sunrise]].forEach(function (pair) {
    var s = el('div', 'stat');
    s.appendChild(el('div', 'k', pair[0]));
    s.appendChild(el('div', 'v', pair[1] || '—'));
    stats.appendChild(s);
  });

  var fc = $('forecast');
  clear(fc);
  (w.forecast || []).forEach(function (d) {
    var c = el('div', 'fc');
    c.appendChild(el('div', 'd', d.day));
    c.appendChild(el('div', 'i', d.icon));
    c.appendChild(el('div', 't', d.high + '°'));
    c.appendChild(el('div', 'muted small', d.low + '° • ' + d.rainChance + '%'));
    fc.appendChild(c);
  });
}

/* Class block: progress is computed here, once a second, with no network traffic. --------------- */

function nowMinutes() {
  var d = new Date();
  return d.getHours() * 60 + d.getMinutes() + d.getSeconds() / 60;
}

function studentProgress(stu, minute) {
  if (stu.closureReason) return { head: stu.closureReason, detail: '', block: 0, day: 0, show: false };

  var blocks = (stu.blocks || []).filter(function (b) { return b.isStudentTime; });
  if (!blocks.length) return { head: 'No schedule configured', detail: '', block: 0, day: 0, show: false };

  var first = blocks[0].startMinute;
  var last = blocks[blocks.length - 1].endMinute;

  if (minute < first) {
    return { head: 'Starts ' + fmt(first), detail: blocks[0].name, block: 0, day: 0, show: false };
  }
  if (minute >= last) {
    return { head: 'Day complete', detail: '', block: 1, day: 1, show: false };
  }

  var dayFraction = (minute - first) / (last - first);
  for (var i = 0; i < blocks.length; i++) {
    var b = blocks[i];
    if (minute >= b.startMinute && minute < b.endMinute) {
      var left = Math.ceil(b.endMinute - minute);
      return {
        head: b.name,
        detail: fmt(b.startMinute) + ' – ' + fmt(b.endMinute) + ' • ' + left + ' min left',
        block: (minute - b.startMinute) / (b.endMinute - b.startMinute),
        day: dayFraction,
        show: true
      };
    }
    if (minute < b.startMinute) {
      return {
        head: 'Passing → ' + b.name,
        detail: b.name + ' at ' + fmt(b.startMinute),
        block: 0, day: dayFraction, show: true
      };
    }
  }
  return { head: '', detail: '', block: 0, day: dayFraction, show: false };
}

function fmt(minute) {
  var h = Math.floor(minute / 60), m = Math.floor(minute % 60);
  var ampm = h >= 12 ? 'PM' : 'AM';
  var hh = h % 12; if (hh === 0) hh = 12;
  return hh + ':' + (m < 10 ? '0' : '') + m + ' ' + ampm;
}

/* Cards are built once and then only their text and bar widths are touched, so the per-second
   update does not re-create DOM on a slow device. */
function renderStudents() {
  var host = $('students');
  var students = state.students || [];
  if (host.childElementCount !== students.length) {
    clear(host);
    students.forEach(function (s) {
      var card = el('div', 'stu');
      var head = el('div', 'stu-head');
      var av = el('div', 'avatar', s.initials);
      av.style.background = s.color;
      head.appendChild(av);
      var who = el('div');
      who.appendChild(el('div', 'stu-name', s.name));
      who.appendChild(el('div', 'stu-level', s.level + ' • ' + s.schoolName));
      head.appendChild(who);
      card.appendChild(head);

      var now = el('div', 'stu-now'); now.style.color = s.color;
      card.appendChild(now);
      card.appendChild(el('div', 'stu-detail'));

      var bar = el('div', 'bar'); var fill = el('i');
      fill.style.background = s.color; bar.appendChild(fill); card.appendChild(bar);

      card.appendChild(el('div', 'bar-label', 'Day progress'));
      var dbar = el('div', 'bar day'); var dfill = el('i');
      dfill.style.background = '#092354'; dbar.appendChild(dfill); card.appendChild(dbar);

      host.appendChild(card);
    });
  }
  tickStudents();
}

function tickStudents() {
  if (!state) return;
  var minute = nowMinutes();
  var host = $('students');
  var students = state.students || [];
  for (var i = 0; i < students.length && i < host.childElementCount; i++) {
    var card = host.children[i];
    var p = studentProgress(students[i], minute);
    card.children[1].textContent = p.head;                       // stu-now
    card.children[2].textContent = p.detail;                     // stu-detail
    card.children[3].firstChild.style.width = (p.show ? p.block * 100 : 0) + '%';
    card.children[5].firstChild.style.width = (p.show ? p.day * 100 : 0) + '%';
  }
}

function renderNews() {
  var host = $('news');
  clear(host);
  if (!news.length) { host.appendChild(el('div', 'empty', 'No headlines available.')); return; }
  news.forEach(function (n) {
    var row = el('div', 'item');
    var src = el('span', 'src', n.source);
    src.style.background = n.color;
    row.appendChild(src);
    row.appendChild(el('span', 'muted small', '  ' + n.lean + '  ' + n.published));
    row.appendChild(el('div', 'news-title', n.title));
    row.appendChild(el('div', 'news-sum', n.summary));
    row.appendChild(el('div', 'news-link', n.link));
    row.appendChild(el('div', 'disclaimer', n.disclaimer));
    host.appendChild(row);
  });
}

/* Loop ---------------------------------------------------------------------------------------- */

function tickClock() {
  var d = new Date();
  var h = d.getHours() % 12; if (h === 0) h = 12;
  var m = d.getMinutes();
  $('clock').textContent = h + ':' + (m < 10 ? '0' : '') + m + ' ' + (d.getHours() >= 12 ? 'PM' : 'AM');
  tickStudents();
}

function rotate() {
  if (Date.now() < manualUntil) return;
  if (state && state.rotationEnabled === false) return;
  var i = PAGES.indexOf(page);
  show(PAGES[(i + 1) % PAGES.length]);
}

buildNav();
show('Daily');
tickClock();
pollState();
pollNews();

setInterval(tickClock, 1000);
setInterval(pollState, STATE_POLL_MS);
setInterval(pollNews, NEWS_POLL_MS);
setInterval(rotate, ROTATE_MS);
