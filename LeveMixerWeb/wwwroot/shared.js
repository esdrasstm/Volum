/* =========================================================
   Volum — o que o mixer (app.js) e a janela de configurações (settings.js) usam juntos:
   textos nos 3 idiomas, novidades da versão e formatação de água.
   Carregar antes do app.js / settings.js.
   ========================================================= */
window.Volum = (() => {
  'use strict';

  /* ---------------- Idiomas ----------------
     Português é o padrão. Para traduzir um texto novo: coloque data-i18n="chave" no HTML
     (ou data-i18n-title para a dica do mouse) e a chave nos três idiomas abaixo. */
  const I18N = {
    pt: {
      settings: 'Configurações', minimize: 'Minimizar', theme: 'Tema', language: 'Idioma',
      system: 'Sistema', dark: 'Escuro', light: 'Claro',
      autostart: 'Iniciar com o Windows', lite: 'Modo leve', liteHint: 'Sem vidro e animações; usa menos memória',
      apps: 'Aplicativos', empty: 'Nenhum app usando áudio agora',
      master: 'Volume Geral', mic: 'Microfone', systemSounds: 'Sons do sistema',
      mute: 'Silenciar', unmute: 'Ativar som', micMute: 'Desligar microfone', micUnmute: 'Ligar microfone',
      micPick: 'Escolher microfone', site: 'Site do Volum', feedback: 'Sugestões e bugs',
      hotkey: 'Atalho para abrir', hotkeyHint: 'Abre o mixer até no meio de um jogo', hotkeyPress: 'Pressione…',
      hotkeyRecordHint: 'Esc cancela · Backspace desliga', hotkeyNeedMod: 'Use Ctrl ou Alt junto',
      hotkeyBusy: 'Em uso por outro app', hotkeyOff: 'Desligado',
      kbLock: 'Travar teclado para limpar',
      newsEyebrow: 'Versão', newsTitle: 'O que tem de novo', newsOk: 'Entendi!',
      newsAgain: 'O que tem de novo', waterReminder: 'Lembrete de água', waterHint: 'Avisa de tempos em tempos e conta o que você bebeu',
      waterGoal: 'Meta por dia', waterEvery: 'Lembrar a cada', waterCup: 'Tamanho do copo', water: 'Água hoje',
      waterOf: '{0} de {1}', waterDone: 'Meta batida! {0}', waterMinus: 'Tirar um copo',
      pageGeneral: 'Geral', pageShortcut: 'Atalho', pageWater: 'Água', pageAbout: 'Sobre',
      shortcutHint: 'Clique no botão e aperte as teclas que quiser usar. Funciona sobre jogos em janela ou em "janela em tela cheia".',
      waterToday: 'Hoje: {0}', aboutVersion: 'Versão {0}',
      aboutText: 'Mixer de volume leve para Windows, feito por Esdras. Grátis e de código aberto.',
      github: 'Código no GitHub',
      pageIsland: 'Ilha', islandToggle: 'Mostrar a ilha no topo da tela',
      islandHint: 'Passe o mouse para ver a música tocando e a água; os avisos do Volum aparecem nela',
      islandNote: 'A ilha some sozinha com jogos e vídeos em tela cheia. Ligada, o Volum usa um pouco mais de memória. Notificações de outros apps ainda não aparecem nela: o Windows só permite isso para apps da Microsoft Store.',
      islandNothing: 'Nada tocando agora', islandPrev: 'Anterior', islandPlay: 'Tocar / pausar', islandNext: 'Próxima',
      waterTest: 'Testar aviso', waterSound: 'Tocar som no aviso',
    },
    en: {
      settings: 'Settings', minimize: 'Minimize', theme: 'Theme', language: 'Language',
      system: 'System', dark: 'Dark', light: 'Light',
      autostart: 'Start with Windows', lite: 'Lite mode', liteHint: 'No glass or animations; uses less memory',
      apps: 'Apps', empty: 'No apps playing audio right now',
      master: 'Master volume', mic: 'Microphone', systemSounds: 'System sounds',
      mute: 'Mute', unmute: 'Unmute', micMute: 'Mute microphone', micUnmute: 'Unmute microphone',
      micPick: 'Choose microphone', site: 'Volum website', feedback: 'Suggestions and bugs',
      hotkey: 'Shortcut to open', hotkeyHint: 'Opens the mixer even mid-game', hotkeyPress: 'Press keys…',
      hotkeyRecordHint: 'Esc cancels · Backspace turns off', hotkeyNeedMod: 'Add Ctrl or Alt',
      hotkeyBusy: 'Used by another app', hotkeyOff: 'Off',
      kbLock: 'Lock keyboard to clean',
      newsEyebrow: 'Version', newsTitle: 'What’s new', newsOk: 'Got it!',
      newsAgain: 'What’s new', waterReminder: 'Water reminder', waterHint: 'Reminds you now and then and counts what you drink',
      waterGoal: 'Daily goal', waterEvery: 'Remind every', waterCup: 'Glass size', water: 'Water today',
      waterOf: '{0} of {1}', waterDone: 'Goal reached! {0}', waterMinus: 'Remove a glass',
      pageGeneral: 'General', pageShortcut: 'Shortcut', pageWater: 'Water', pageAbout: 'About',
      shortcutHint: 'Click the button and press the keys you want. Works over games in windowed or borderless fullscreen mode.',
      waterToday: 'Today: {0}', aboutVersion: 'Version {0}',
      aboutText: 'A lightweight volume mixer for Windows, made by Esdras. Free and open source.',
      github: 'Code on GitHub',
      pageIsland: 'Island', islandToggle: 'Show the island at the top of the screen',
      islandHint: 'Hover it to see what is playing and your water; Volum alerts show up there',
      islandNote: 'The island hides itself during fullscreen games and videos. When on, Volum uses a bit more memory. Notifications from other apps don’t show up yet: Windows only allows that for Microsoft Store apps.',
      islandNothing: 'Nothing playing', islandPrev: 'Previous', islandPlay: 'Play / pause', islandNext: 'Next',
      waterTest: 'Test reminder', waterSound: 'Play a sound',
    },
    es: {
      settings: 'Configuración', minimize: 'Minimizar', theme: 'Tema', language: 'Idioma',
      system: 'Sistema', dark: 'Oscuro', light: 'Claro',
      autostart: 'Iniciar con Windows', lite: 'Modo ligero', liteHint: 'Sin vidrio ni animaciones; usa menos memoria',
      apps: 'Aplicaciones', empty: 'Ninguna app está reproduciendo audio',
      master: 'Volumen general', mic: 'Micrófono', systemSounds: 'Sonidos del sistema',
      mute: 'Silenciar', unmute: 'Activar sonido', micMute: 'Silenciar micrófono', micUnmute: 'Activar micrófono',
      micPick: 'Elegir micrófono', site: 'Sitio de Volum', feedback: 'Sugerencias y errores',
      hotkey: 'Atajo para abrir', hotkeyHint: 'Abre el mezclador incluso en pleno juego', hotkeyPress: 'Pulsa las teclas…',
      hotkeyRecordHint: 'Esc cancela · Retroceso desactiva', hotkeyNeedMod: 'Usa Ctrl o Alt',
      hotkeyBusy: 'En uso por otra app', hotkeyOff: 'Desactivado',
      kbLock: 'Bloquear teclado para limpiar',
      newsEyebrow: 'Versión', newsTitle: 'Novedades', newsOk: '¡Entendido!',
      newsAgain: 'Novedades', waterReminder: 'Recordatorio de agua', waterHint: 'Te avisa cada tanto y cuenta lo que bebes',
      waterGoal: 'Meta diaria', waterEvery: 'Recordar cada', waterCup: 'Tamaño del vaso', water: 'Agua hoy',
      waterOf: '{0} de {1}', waterDone: '¡Meta cumplida! {0}', waterMinus: 'Quitar un vaso',
      pageGeneral: 'General', pageShortcut: 'Atajo', pageWater: 'Agua', pageAbout: 'Acerca de',
      shortcutHint: 'Haz clic en el botón y pulsa las teclas que quieras. Funciona sobre juegos en ventana o en pantalla completa sin bordes.',
      waterToday: 'Hoy: {0}', aboutVersion: 'Versión {0}',
      aboutText: 'Un mezclador de volumen ligero para Windows, hecho por Esdras. Gratis y de código abierto.',
      github: 'Código en GitHub',
      pageIsland: 'Isla', islandToggle: 'Mostrar la isla arriba de la pantalla',
      islandHint: 'Pasa el mouse para ver lo que suena y el agua; los avisos de Volum aparecen ahí',
      islandNote: 'La isla se esconde sola con juegos y videos en pantalla completa. Activada, Volum usa un poco más de memoria. Las notificaciones de otras apps aún no aparecen: Windows solo lo permite para apps de Microsoft Store.',
      islandNothing: 'No suena nada', islandPrev: 'Anterior', islandPlay: 'Reproducir / pausar', islandNext: 'Siguiente',
      waterTest: 'Probar aviso', waterSound: 'Reproducir sonido',
    },
  };

  const t = (lang, key) => (I18N[lang] && I18N[lang][key]) || I18N.pt[key] || key;
  const langTag = (lang) => ({ pt: 'pt-BR', en: 'en', es: 'es' }[lang] || 'pt-BR');

  // Troca os textos marcados com data-i18n / data-i18n-title / data-i18n-aria
  function applyI18n(lang) {
    document.querySelectorAll('[data-i18n]').forEach((el) => { el.textContent = t(lang, el.dataset.i18n); });
    document.querySelectorAll('[data-i18n-title]').forEach((el) => { el.title = t(lang, el.dataset.i18nTitle); });
    document.querySelectorAll('[data-i18n-aria]').forEach((el) => { el.setAttribute('aria-label', t(lang, el.dataset.i18nAria)); });
  }

  /* ---------------- Novidades ----------------
     Aparecem no popup do mixer (uma vez, depois de atualizar) e na página "Sobre" das configurações.
     Para a próxima versão: troque "version" e os itens (nos 3 idiomas). O ícone é uma chave de NEWS_ICONS. */
  const NEWS = {
    version: '0.2.0',
    items: [
      { icon: 'island',
        pt: ['Ilha no topo da tela', 'Ligue em Configurações → Ilha: música tocando, contador de água e os avisos do Volum, num toque do mouse.'],
        en: ['Island at the top of the screen', 'Turn it on in Settings → Island: what’s playing, your water and Volum alerts, one hover away.'],
        es: ['Isla arriba de la pantalla', 'Actívala en Configuración → Isla: lo que suena, el agua y los avisos de Volum, al pasar el mouse.'] },
      { icon: 'gear',
        pt: ['Configurações numa janela própria', 'A engrenagem no topo do mixer abre todas as opções, organizadas por assunto.'],
        en: ['Settings in their own window', 'The gear at the top of the mixer opens every option, sorted by topic.'],
        es: ['Configuración en su propia ventana', 'El engranaje arriba del mezclador abre todas las opciones, ordenadas por tema.'] },
      { icon: 'drop',
        pt: ['Lembrete de água', 'Ligue em Configurações → Água: escolha a meta do dia e de quanto em quanto tempo quer ser lembrado, do jeito que quiser.'],
        en: ['Water reminder', 'Turn it on in Settings → Water: pick your daily goal and how often you want a reminder.'],
        es: ['Recordatorio de agua', 'Actívalo en Configuración → Agua: elige tu meta diaria y cada cuánto quieres el aviso.'] },
      { icon: 'cup',
        pt: ['Contador de água', 'Marque cada copo direto no mixer e veja quanto falta para a meta.'],
        en: ['Water counter', 'Log each glass right in the mixer and see how much is left to your goal.'],
        es: ['Contador de agua', 'Marca cada vaso en el mezclador y mira cuánto falta para la meta.'] },
      { icon: 'keyboard',
        pt: ['Trave o teclado para limpar', 'O ícone de teclado no topo trava tudo por até 10 minutos. O mouse continua livre.'],
        en: ['Lock the keyboard to clean it', 'The keyboard icon at the top locks it for up to 10 minutes. The mouse keeps working.'],
        es: ['Bloquea el teclado para limpiarlo', 'El ícono de teclado arriba lo bloquea hasta 10 minutos. El mouse sigue funcionando.'] },
      // soon: ainda não existe, aparece com a etiqueta "Em breve"
      { icon: 'timer', soon: true,
        pt: ['Pomodoro na ilha', 'Um timer de foco direto pela ilha: comece, pause e veja o tempo sem sair do que está fazendo.'],
        en: ['Pomodoro on the island', 'A focus timer right on the island: start, pause and check the time without leaving what you’re doing.'],
        es: ['Pomodoro en la isla', 'Un temporizador de enfoque desde la isla: empieza, pausa y mira el tiempo sin dejar lo que haces.'] },
    ],
  };
  // Ícones de traço em caixa de 24 (os mesmos do mixer e das configurações; classe .ico no style.css)
  const NEWS_ICONS = {
    keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="3"/><path d="M6.5 10h.01M10 10h.01M14 10h.01M17.5 10h.01" stroke-width="2.2"/><path d="M8.5 14h7"/>',
    bolt: '<path d="M13 2.5 5 13.5h6l-1 8 8-11h-6z"/>',
    dock: '<rect x="3" y="15" width="18" height="5.5" rx="2"/><path d="M12 3v8M8.5 7.5 12 11l3.5-3.5"/>',
    chat: '<path d="M7 4.5h10a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-5.5L7 21v-3.5a3 3 0 0 1-3-3v-7a3 3 0 0 1 3-3z"/>',
    drop: '<path d="M12 2.8c3.7 4.3 6.6 8 6.6 11.4a6.6 6.6 0 0 1-13.2 0C5.4 10.8 8.3 7.1 12 2.8z"/>',
    cup: '<path d="M5 4h14l-1.6 15.2a2 2 0 0 1-2 1.8H8.6a2 2 0 0 1-2-1.8z"/><path d="M5.6 9.5h12.8"/>',
    sparkle: '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M18.4 5.6l-2.8 2.8M8.4 15.6l-2.8 2.8"/>',
    island: '<rect x="3" y="8" width="18" height="8" rx="4"/>',
    timer: '<circle cx="12" cy="13.5" r="7.5"/><path d="M12 9.5v4l2.5 2M9.5 2.5h5M12 2.5V6"/>',
    gear: '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
  };

  // Itens da lista de novidades (<li> com ícone, título e texto), no idioma pedido
  function newsItems(lang) {
    return NEWS.items.map((item) => {
      const [head, text] = item[lang] || item.pt;
      const li = document.createElement('li');
      li.innerHTML = '<div class="news-icon"><svg class="ico" viewBox="0 0 24 24">' + NEWS_ICONS[item.icon] +
        '</svg></div><div><b></b><span></span></div>';
      li.querySelector('b').textContent = head;
      if (item.soon) {
        const tag = document.createElement('em');
        tag.className = 'news-soon';
        tag.textContent = { pt: 'Em breve', en: 'Coming soon', es: 'Próximamente' }[lang] || 'Em breve';
        li.querySelector('b').append(' ', tag);
      }
      li.querySelector('span').textContent = text;
      return li;
    });
  }

  // Compara versões tipo "0.1.10" e "0.1.9" número a número (negativo: a é menor)
  function compareVersions(a, b) {
    const pa = String(a).split('.').map(Number), pb = String(b).split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
      const d = (pa[i] || 0) - (pb[i] || 0);
      if (d) return d;
    }
    return 0;
  }

  /* ---------------- Água ---------------- */
  // "250 ml", "2 L", "1,25 L" (vírgula ou ponto conforme o idioma) e "45 min", "1 h", "1 h 30"
  const fmtMl = (ml, lang) => (ml < 1000 ? `${ml} ml`
    : `${(ml / 1000).toLocaleString(langTag(lang), { maximumFractionDigits: 2 })} L`);
  const fmtMin = (m) => (m < 60 ? `${m} min` : m % 60 ? `${Math.floor(m / 60)} h ${m % 60}` : `${m / 60} h`);

  return { I18N, t, langTag, applyI18n, NEWS, newsItems, compareVersions, fmtMl, fmtMin };
})();
