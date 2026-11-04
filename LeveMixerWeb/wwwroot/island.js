/* =========================================================
   Volum — ilha (island.html). Textos e formatação vêm do shared.js (window.Volum).
   Fala com o C# (IslandForm.cs) por mensagens JSON:
     C# -> JS : settings | mode | media | water | battery | notice
     JS -> C# : ready | height | hover | playPause | next | prev | openMixer | waterAdd | noticeAction | noticeClose
   ========================================================= */
(() => {
  'use strict';

  const V = window.Volum;
  const $ = (id) => document.getElementById(id);
  const post = (msg) => window.chrome.webview.postMessage(msg);
  const island = $('island');

  let lang = 'pt';
  const t = (key) => V.t(lang, key);
  let mode = 'closed';
  let media = null;
  let water = { enabled: false, goalMl: 2000, cupMl: 250, todayMl: 0 };
  let battery = { present: false, percent: 100, charging: false };
  let soundOn = false;   // tem som saindo agora (o C# lê o medidor do Windows)

  /* ---------------- Hora e data (atualiza só na virada do minuto) ---------------- */
  // Data curta sem pontos, como "qua 4 nov 2026"
  function shortDate(d) {
    const parts = new Intl.DateTimeFormat(V.langTag(lang), { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
      .formatToParts(d).filter((p) => p.type !== 'literal').map((p) => p.value.replace('.', ''));
    return parts.join(' ');
  }

  let clockTimer = 0;
  function tickClock() {
    clearTimeout(clockTimer);
    const now = new Date();
    const time = now.toLocaleTimeString(V.langTag(lang), { hour: '2-digit', minute: '2-digit' });
    $('clock').textContent = time;
    $('time').textContent = time;
    $('date').textContent = shortDate(now);
    clockTimer = setTimeout(tickClock, (60 - now.getSeconds()) * 1000 + 50);
  }

  /* ---------------- Bateria ---------------- */
  function batterySvg() {
    const low = battery.percent <= 20 && !battery.charging;
    const color = low ? 'var(--battery-low)' : 'var(--battery)';
    const c = 2 * Math.PI * 8;
    const bolt = battery.charging
      ? '<path d="M10.8 5.5 7.6 10.6h2.6l-1 3.9 3.2-5.1h-2.6z" fill="' + color + '"/>'
      : '<text x="10" y="13" text-anchor="middle" font-size="7" font-weight="700" fill="' + color + '">' + battery.percent + '</text>';
    return '<svg viewBox="0 0 20 20"><circle cx="10" cy="10" r="8" fill="none" stroke="rgba(255,255,255,.18)" stroke-width="2"/>' +
      '<circle cx="10" cy="10" r="8" fill="none" stroke="' + color + '" stroke-width="2" stroke-linecap="round" ' +
      'stroke-dasharray="' + (c * battery.percent / 100) + ' ' + c + '" transform="rotate(-90 10 10)"/>' + bolt + '</svg>';
  }

  function showBattery() {
    const svg = batterySvg();   // feito aqui só com números: nada de fora entra no HTML
    for (const el of [$('battery'), $('miniBattery')]) el.innerHTML = svg;
    $('battery').hidden = !battery.present;
    showMini();
  }

  // Fechada, à esquerda: a capa (com música) ou a bateria
  function showMini() {
    const cover = !!(media && media.cover);
    $('miniCover').hidden = !cover;
    $('miniBattery').hidden = cover || !battery.present;
  }

  /* ---------------- Onda de áudio: só aparece (e anda) com som saindo de verdade ---------------- */
  function showWave() {
    for (const w of [$('wave'), $('miniWave')]) {
      w.hidden = !soundOn;
      w.classList.toggle('playing', soundOn);
    }
  }

  /* ---------------- Música ---------------- */
  const fmtTime = (s) => {
    s = Math.max(0, Math.floor(s));
    const m = Math.floor(s / 60), r = String(s % 60).padStart(2, '0');
    return m >= 60 ? `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}:${r}` : `${String(m).padStart(2, '0')}:${r}`;
  };

  // Posição agora: a que o player informou + o tempo que passou desde então (se está tocando)
  const position = () => {
    if (!media) return 0;
    const p = media.position + (media.playing ? (Date.now() - media.updatedAt) / 1000 : 0);
    return media.duration > 0 ? Math.min(p, media.duration) : p;
  };

  function showProgress() {
    const has = media && media.duration > 0;
    $('progress').hidden = !has;
    if (!has) return;
    const p = position();
    $('elapsed').textContent = fmtTime(p);
    $('remaining').textContent = '-' + fmtTime(media.duration - p);
    $('barFill').style.width = (p / media.duration) * 100 + '%';
  }

  // O tempo só anda com a ilha aberta e a música tocando
  let progressTimer = 0;
  function syncProgressTimer() {
    clearInterval(progressTimer);
    if (mode === 'open' && media && media.playing && media.duration > 0) progressTimer = setInterval(showProgress, 1000);
  }

  function showMedia() {
    const has = !!media;
    showWave();
    for (const img of [$('cover'), $('miniCover')]) {
      img.hidden = !(has && media.cover);
      if (has && media.cover && img.src !== media.cover) img.src = media.cover;
    }
    $('title').textContent = has ? media.title : t('islandNothing');
    $('subtitle').textContent = has ? [media.artist, media.album].filter(Boolean).join(' - ') || media.app : '';
    $('play').classList.toggle('playing', has && media.playing);
    $('play').disabled = !has;
    $('prev').disabled = !has || !media.canPrev;
    $('next').disabled = !has || !media.canNext;
    showMini();
    showProgress();
    syncProgressTimer();
    reportHeight();
  }

  $('play').addEventListener('click', () => post({ type: 'playPause' }));
  $('next').addEventListener('click', () => post({ type: 'next' }));
  $('prev').addEventListener('click', () => post({ type: 'prev' }));
  $('openMixer').addEventListener('click', () => post({ type: 'openMixer' }));

  /* ---------------- Água ---------------- */
  function showWater() {
    const done = water.todayMl >= water.goalMl;
    $('waterAmount').textContent = done
      ? t('waterDone').replace('{0}', V.fmtMl(water.todayMl, lang))
      : t('waterOf').replace('{0}', V.fmtMl(water.todayMl, lang)).replace('{1}', V.fmtMl(water.goalMl, lang));
    $('waterFill').style.width = Math.min(100, (water.todayMl / water.goalMl) * 100) + '%';
    $('waterPlus').textContent = '+ ' + V.fmtMl(water.cupMl, lang);
    $('waterMinus').disabled = water.todayMl <= 0;
    $('waterQuick').hidden = !water.enabled;
    $('waterQuick').title = '+ ' + V.fmtMl(water.cupMl, lang);
    showPages();
  }

  const addWater = (ml) => {
    water.todayMl = Math.max(0, water.todayMl + ml);
    showWater();
    post({ type: 'waterAdd', ml });
  };
  $('waterPlus').addEventListener('click', () => addWater(water.cupMl));
  $('waterQuick').addEventListener('click', () => addWater(water.cupMl));
  $('waterMinus').addEventListener('click', () => addWater(-Math.min(water.cupMl, water.todayMl)));

  /* ---------------- Páginas (setas ‹ › e bolinhas) ---------------- */
  let page = 0;
  const pages = () => [...document.querySelectorAll('.page')].filter((p) => p.dataset.page !== 'water' || water.enabled);

  function showPages() {
    const list = pages();
    page = Math.min(page, list.length - 1);
    document.querySelectorAll('.page').forEach((p) => { p.hidden = p !== list[page]; });
    const many = list.length > 1;
    $('pagePrev').hidden = !many;
    $('pageNext').hidden = !many;
    $('dots').replaceChildren(...(many ? list.map((_, i) => {
      const dot = document.createElement('i');
      dot.className = i === page ? 'on' : '';
      dot.addEventListener('click', () => { page = i; showPages(); });
      return dot;
    }) : []));
    reportHeight();
  }

  const turn = (step) => { const n = pages().length; page = (page + step + n) % n; showPages(); };
  $('pagePrev').addEventListener('click', () => turn(-1));
  $('pageNext').addEventListener('click', () => turn(1));

  /* ---------------- Avisos do Volum ---------------- */
  const NOTICE_ICONS = {
    drop: '<svg viewBox="0 0 24 24"><path d="M12 2.8c3.6 4.4 6.2 7.8 6.2 11a6.2 6.2 0 0 1-12.4 0c0-3.2 2.6-6.6 6.2-11z" fill="#38bdf8"/></svg>',
    keyboard: '<svg viewBox="0 0 16 16"><g fill="none" stroke="#ffffff" stroke-width="1.4" stroke-linecap="round"><rect x="1.75" y="4" width="12.5" height="8" rx="1.5"/><path d="M4.5 6.75h.01M7 6.75h.01M9.5 6.75h.01M12 6.75h.01M5.5 9.5h5"/></g></svg>',
    sparkle: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M10 3c.8 4.5 2.5 6.2 7 7-4.5.8-6.2 2.5-7 7-.8-4.5-2.5-6.2-7-7 4.5-.8 6.2-2.5 7-7z"/><path d="M19 15v6M16 18h6"/></g></svg>',
    bell: '<svg viewBox="0 0 16 16"><path d="M4 11V7a4 4 0 0 1 8 0v4l1 1.5H3zM6.5 14h3" fill="none" stroke="#ffffff" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  };

  function showNotice(n) {
    const on = !!n.icon;
    $('notice').hidden = !on;
    if (!on) return;
    $('notice').dataset.kind = n.icon;   // água: botão azul; o resto, branco
    $('noticeIcon').innerHTML = NOTICE_ICONS[n.icon] || NOTICE_ICONS.bell;   // ícones fixos daqui, não vêm de fora
    $('noticeTitle').textContent = n.title || '';
    $('noticeBody').textContent = n.text || '';
    $('noticeAction').hidden = !n.action;
    $('noticeAction').textContent = n.action || '';
  }

  $('noticeAction').addEventListener('click', () => post({ type: 'noticeAction' }));
  $('noticeClose').addEventListener('click', () => post({ type: 'noticeClose' }));

  /* ---------------- Abrir e fechar ---------------- */
  // O C# cresce a janela e manda "mode"; aqui a pílula anima (CSS) até o tamanho novo.
  document.documentElement.addEventListener('mouseenter', () => post({ type: 'hover', on: true }));
  document.documentElement.addEventListener('mouseleave', () => post({ type: 'hover', on: false }));

  function setMode(m) {
    mode = m;
    island.classList.remove('tucked', 'closed', 'alerting', 'open');
    island.classList.add(m === 'notice' ? 'alerting' : m);   // "notice" vira "alerting": a classe .notice já tem outro uso
    if (m === 'open') showProgress();
    syncProgressTimer();
  }

  // Altura da ilha aberta (muda com a página e com a água): o C# usa para o tamanho da janela
  let lastH = 0;
  function reportHeight() {
    const h = Math.ceil($('full').offsetHeight);
    island.style.setProperty('--open-h', h + 'px');
    if (h && h !== lastH) { lastH = h; post({ type: 'height', h }); }
  }

  /* ---------------- Mensagens do C# ---------------- */
  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    switch (m.type) {
      case 'settings':
        lang = V.I18N[m.language] ? m.language : 'pt';
        document.documentElement.lang = V.langTag(lang);
        document.documentElement.classList.toggle('lite', !!m.lite);
        V.applyI18n(lang);
        tickClock();
        showMedia();
        showWater();
        break;
      case 'mode':    setMode(m.mode); break;
      case 'media':   media = m.media; showMedia(); break;
      case 'water':   water = m.water; showWater(); break;
      case 'battery': battery = m; showBattery(); break;
      case 'sound':   soundOn = m.on; showWave(); break;
      case 'notice':  showNotice(m); break;
    }
  });

  document.addEventListener('contextmenu', (e) => e.preventDefault());
  post({ type: 'ready' });
})();
