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
      micPick: 'Escolher microfone', outPick: 'Escolher saída de áudio', site: 'Site do Volum', feedback: 'Sugestões e bugs',
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
      supportTitle: 'Apoiar o Volum ♥', supportText: 'O Volum é grátis e vai continuar grátis. Se ele te ajuda, qualquer valor faz diferença.',
      supportPix: 'Copiar código Pix', supportCopied: 'Copiado!', supportKofi: 'Apoiar pelo Ko-fi',
      supportPixHint: 'Agora, no app do seu banco: Pix → Pix Copia e Cola, cole o código e escolha o valor.',
      github: 'Código no GitHub',
      pageIsland: 'Ilha', islandToggle: 'Mostrar a ilha no topo da tela',
      pageFocus: 'Foco', needsIsland: 'Isto aparece na ilha: ligue a ilha em Configurações → Ilha.', calField: 'Endereço da agenda (iCal)', wxField: 'Sua cidade',
      islandHint: 'Passe o mouse para ver a música tocando e a água; os avisos do Volum aparecem nela',
      islandStyle: 'Formato', islandNotch: 'Grudada no topo', islandFloat: 'Flutuante',
      privMic: 'Microfone', privCam: 'Câmera',
      appNotesToggle: 'Notificações dos apps', appNotesHint: 'WhatsApp, Discord, e-mail... aparecem na ilha. Ligue o "Não incomodar" do Windows para elas aparecerem só aqui',
      appNotesStoreOnly: 'Só na versão da Microsoft Store: o Windows exige para deixar um app ler as notificações', appNotesAsking: 'Confirme a permissão na janela do Windows...', appNotesDenied: 'O Windows não deu a permissão. Libere em Configurações do Windows → Privacidade → Notificações',
      calTitle: 'Agenda', calHint: 'Cole o endereço iCal (.ics) da sua agenda. No Google Agenda: Configurações → sua agenda → "Endereço secreto no formato iCal". A ilha avisa 10 min antes de cada compromisso',
      calSave: 'Salvar', calLoading: 'Lendo a agenda...', calOk: 'Pronto! {0} compromisso(s) hoje e amanhã', calError: 'Não consegui abrir esse endereço. Confira se ele está completo', calNotIcs: 'Esse endereço não é de uma agenda iCal (.ics)',
      islandAgenda: 'Agenda', agendaEmpty: 'Nada na agenda hoje', agendaAllDay: 'Dia todo', agendaTomorrow: 'Amanhã', agendaNow: 'Agora',
      wxTitle: 'Clima', wxHint: 'Digite a sua cidade para ver o clima e as próximas horas na ilha. Deixe vazio para desligar', wxPlaceholder: 'Ex.: São Paulo',
      wxLoading: 'Procurando a cidade...', wxOk: 'Pronto! {0}', wxNotFound: 'Não achei essa cidade. Confira o nome', wxError: 'Não consegui buscar o clima agora. Confira a internet',
      islandWeather: 'Clima', wxMax: 'Máx.', wxMin: 'Mín.', wxNow: 'Agora',
      wx_clear: 'Céu limpo', wx_mostly: 'Quase limpo', wx_partly: 'Parcialmente nublado', wx_cloudy: 'Nublado', wx_fog: 'Neblina', wx_drizzle: 'Garoa', wx_rain: 'Chuva', wx_snow: 'Neve', wx_storm: 'Tempestade',
      islandNote: 'Com jogos e vídeos em tela cheia, a ilha some (ou vai para o outro monitor). Ligada, o Volum usa um pouco mais de memória.',
      islandNothing: 'Nada tocando agora', islandPrev: 'Anterior', islandPlay: 'Tocar / pausar', islandNext: 'Próxima',
      focus: 'Foco', focusShort: 'Pausa', focusLong: 'Pausa longa', focusPaused: 'pausado', focusReady: 'Pomodoro',
      focusReadyText: '{0} min de foco', focusCycle: 'Foco {0} de {1}', focusStart: 'Começar', focusPause: 'Pausar',
      focusResume: 'Continuar', focusSkip: 'Pular', focusStop: 'Parar',
      pomoToggle: 'Pomodoro na ilha', pomoHint: 'Foco e pausas com a contagem na ilha', pomoFocus: 'Foco', pomoShort: 'Pausa curta',
      pomoLong: 'Pausa longa', pomoEvery: 'Pausa longa a cada', pomoEveryUnit: 'focos',
      standToggle: 'Lembrete para levantar', standHint: 'Avisa pela ilha depois de um tempo sentado; zera quando você bloqueia a tela',
      standEvery: 'Avisar depois de', standTest: 'Testar aviso de levantar',
      focusNeedsIsland: 'Pomodoro e o lembrete para levantar funcionam com a ilha ligada.',
      pomoSound: 'Som do fim', snd_chime: 'Sininho', snd_drops: 'Gotinhas', snd_bell: 'Campainha', snd_none: 'Sem som',
      osdToggle: 'Indicadores na ilha', osdHint: 'Volume, músicas, brilho, Caps Lock, Num Lock e microfone mudo aparecem na ilha, no lugar dos avisos do Windows',
      otherScreen: 'Outro monitor em tela cheia', otherScreenHint: 'Com um jogo ou vídeo em tela cheia no monitor principal, a ilha vai para o outro monitor',
      waterTest: 'Testar aviso', waterSound: 'Tocar som no aviso',
    },
    en: {
      settings: 'Settings', minimize: 'Minimize', theme: 'Theme', language: 'Language',
      system: 'System', dark: 'Dark', light: 'Light',
      autostart: 'Start with Windows', lite: 'Lite mode', liteHint: 'No glass or animations; uses less memory',
      apps: 'Apps', empty: 'No apps playing audio right now',
      master: 'Master volume', mic: 'Microphone', systemSounds: 'System sounds',
      mute: 'Mute', unmute: 'Unmute', micMute: 'Mute microphone', micUnmute: 'Unmute microphone',
      micPick: 'Choose microphone', outPick: 'Choose audio output', site: 'Volum website', feedback: 'Suggestions and bugs',
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
      supportTitle: 'Support Volum ♥', supportText: 'Volum is free and will stay free. If it helps you, any amount makes a difference.',
      supportPix: 'Copy Pix code', supportCopied: 'Copied!', supportKofi: 'Support on Ko-fi',
      supportPixHint: 'Now, in your bank app: Pix → Pix Copia e Cola, paste the code and choose the amount.',
      github: 'Code on GitHub',
      pageIsland: 'Island', islandToggle: 'Show the island at the top of the screen',
      pageFocus: 'Focus', needsIsland: 'This shows up on the island: turn the island on in Settings → Island.', calField: 'Calendar address (iCal)', wxField: 'Your city',
      islandHint: 'Hover it to see what is playing and your water; Volum alerts show up there',
      islandStyle: 'Shape', islandNotch: 'Attached to the top', islandFloat: 'Floating',
      privMic: 'Microphone', privCam: 'Camera',
      appNotesToggle: 'App notifications', appNotesHint: 'WhatsApp, Discord, email... show up on the island. Turn on Windows "Do not disturb" so they only show up here',
      appNotesStoreOnly: 'Microsoft Store version only: Windows requires it to let an app read notifications', appNotesAsking: 'Confirm the permission in the Windows window...', appNotesDenied: 'Windows did not grant the permission. Allow it in Windows Settings → Privacy → Notifications',
      calTitle: 'Calendar', calHint: 'Paste your calendar iCal (.ics) address. In Google Calendar: Settings → your calendar → "Secret address in iCal format". The island alerts you 10 min before each event',
      calSave: 'Save', calLoading: 'Reading the calendar...', calOk: 'Done! {0} event(s) today and tomorrow', calError: "Couldn't open that address. Check that it's complete", calNotIcs: "That address isn't an iCal (.ics) calendar",
      islandAgenda: 'Calendar', agendaEmpty: 'Nothing on the calendar today', agendaAllDay: 'All day', agendaTomorrow: 'Tomorrow', agendaNow: 'Now',
      wxTitle: 'Weather', wxHint: 'Type your city to see the weather and the next hours on the island. Leave it empty to turn it off', wxPlaceholder: 'E.g.: London',
      wxLoading: 'Looking up the city...', wxOk: 'Done! {0}', wxNotFound: "Couldn't find that city. Check the name", wxError: "Couldn't get the weather right now. Check your internet",
      islandWeather: 'Weather', wxMax: 'H', wxMin: 'L', wxNow: 'Now',
      wx_clear: 'Clear', wx_mostly: 'Mostly clear', wx_partly: 'Partly cloudy', wx_cloudy: 'Cloudy', wx_fog: 'Fog', wx_drizzle: 'Drizzle', wx_rain: 'Rain', wx_snow: 'Snow', wx_storm: 'Thunderstorm',
      islandNote: 'During fullscreen games and videos, the island hides (or moves to the other monitor). When on, Volum uses a bit more memory.',
      islandNothing: 'Nothing playing', islandPrev: 'Previous', islandPlay: 'Play / pause', islandNext: 'Next',
      focus: 'Focus', focusShort: 'Break', focusLong: 'Long break', focusPaused: 'paused', focusReady: 'Pomodoro',
      focusReadyText: '{0} min of focus', focusCycle: 'Focus {0} of {1}', focusStart: 'Start', focusPause: 'Pause',
      focusResume: 'Resume', focusSkip: 'Skip', focusStop: 'Stop',
      pomoToggle: 'Pomodoro on the island', pomoHint: 'Focus and breaks with a countdown on the island', pomoFocus: 'Focus', pomoShort: 'Short break',
      pomoLong: 'Long break', pomoEvery: 'Long break every', pomoEveryUnit: 'focuses',
      standToggle: 'Stand-up reminder', standHint: 'Reminds you on the island after sitting for a while; resets when you lock the screen',
      standEvery: 'Remind after', standTest: 'Test stand-up reminder',
      focusNeedsIsland: 'Pomodoro and the stand-up reminder work while the island is on.',
      pomoSound: 'End sound', snd_chime: 'Chime', snd_drops: 'Drops', snd_bell: 'Bell', snd_none: 'No sound',
      osdToggle: 'Island indicators', osdHint: 'Volume, music, brightness, Caps Lock, Num Lock and a muted microphone show up on the island instead of the Windows pop-ups',
      otherScreen: 'Other monitor in full screen', otherScreenHint: 'With a game or video in full screen on the main monitor, the island moves to the other monitor',
      waterTest: 'Test reminder', waterSound: 'Play a sound',
    },
    es: {
      settings: 'Configuración', minimize: 'Minimizar', theme: 'Tema', language: 'Idioma',
      system: 'Sistema', dark: 'Oscuro', light: 'Claro',
      autostart: 'Iniciar con Windows', lite: 'Modo ligero', liteHint: 'Sin vidrio ni animaciones; usa menos memoria',
      apps: 'Aplicaciones', empty: 'Ninguna app está reproduciendo audio',
      master: 'Volumen general', mic: 'Micrófono', systemSounds: 'Sonidos del sistema',
      mute: 'Silenciar', unmute: 'Activar sonido', micMute: 'Silenciar micrófono', micUnmute: 'Activar micrófono',
      micPick: 'Elegir micrófono', outPick: 'Elegir salida de audio', site: 'Sitio de Volum', feedback: 'Sugerencias y errores',
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
      supportTitle: 'Apoyar a Volum ♥', supportText: 'Volum es gratis y seguirá siendo gratis. Si te ayuda, cualquier monto hace la diferencia.',
      supportPix: 'Copiar código Pix', supportCopied: '¡Copiado!', supportKofi: 'Apoyar en Ko-fi',
      supportPixHint: 'Ahora, en la app de tu banco: Pix → Pix Copia e Cola, pega el código y elige el monto.',
      github: 'Código en GitHub',
      pageIsland: 'Isla', islandToggle: 'Mostrar la isla arriba de la pantalla',
      pageFocus: 'Enfoque', needsIsland: 'Esto aparece en la isla: activa la isla en Configuración → Isla.', calField: 'Dirección de la agenda (iCal)', wxField: 'Tu ciudad',
      islandHint: 'Pasa el mouse para ver lo que suena y el agua; los avisos de Volum aparecen ahí',
      islandStyle: 'Forma', islandNotch: 'Pegada arriba', islandFloat: 'Flotante',
      privMic: 'Micrófono', privCam: 'Cámara',
      appNotesToggle: 'Notificaciones de las apps', appNotesHint: 'WhatsApp, Discord, correo... aparecen en la isla. Activa "No molestar" de Windows para que aparezcan solo aquí',
      appNotesStoreOnly: 'Solo en la versión de Microsoft Store: Windows lo exige para dejar que una app lea las notificaciones', appNotesAsking: 'Confirma el permiso en la ventana de Windows...', appNotesDenied: 'Windows no dio el permiso. Actívalo en Configuración de Windows → Privacidad → Notificaciones',
      calTitle: 'Agenda', calHint: 'Pega la dirección iCal (.ics) de tu agenda. En Google Calendar: Configuración → tu calendario → "Dirección secreta en formato iCal". La isla avisa 10 min antes de cada evento',
      calSave: 'Guardar', calLoading: 'Leyendo la agenda...', calOk: '¡Listo! {0} evento(s) hoy y mañana', calError: 'No pude abrir esa dirección. Revisa que esté completa', calNotIcs: 'Esa dirección no es de una agenda iCal (.ics)',
      islandAgenda: 'Agenda', agendaEmpty: 'Nada en la agenda hoy', agendaAllDay: 'Todo el día', agendaTomorrow: 'Mañana', agendaNow: 'Ahora',
      wxTitle: 'Clima', wxHint: 'Escribe tu ciudad para ver el clima y las próximas horas en la isla. Déjalo vacío para desactivarlo', wxPlaceholder: 'Ej.: Madrid',
      wxLoading: 'Buscando la ciudad...', wxOk: '¡Listo! {0}', wxNotFound: 'No encontré esa ciudad. Revisa el nombre', wxError: 'No pude obtener el clima ahora. Revisa la conexión',
      islandWeather: 'Clima', wxMax: 'Máx.', wxMin: 'Mín.', wxNow: 'Ahora',
      wx_clear: 'Despejado', wx_mostly: 'Casi despejado', wx_partly: 'Parcialmente nublado', wx_cloudy: 'Nublado', wx_fog: 'Niebla', wx_drizzle: 'Llovizna', wx_rain: 'Lluvia', wx_snow: 'Nieve', wx_storm: 'Tormenta',
      islandNote: 'Con juegos y videos en pantalla completa, la isla se esconde (o pasa al otro monitor). Activada, Volum usa un poco más de memoria.',
      islandNothing: 'No suena nada', islandPrev: 'Anterior', islandPlay: 'Reproducir / pausar', islandNext: 'Siguiente',
      focus: 'Enfoque', focusShort: 'Descanso', focusLong: 'Descanso largo', focusPaused: 'en pausa', focusReady: 'Pomodoro',
      focusReadyText: '{0} min de enfoque', focusCycle: 'Enfoque {0} de {1}', focusStart: 'Empezar', focusPause: 'Pausar',
      focusResume: 'Seguir', focusSkip: 'Saltar', focusStop: 'Detener',
      pomoToggle: 'Pomodoro en la isla', pomoHint: 'Enfoque y descansos con la cuenta en la isla', pomoFocus: 'Enfoque', pomoShort: 'Descanso corto',
      pomoLong: 'Descanso largo', pomoEvery: 'Descanso largo cada', pomoEveryUnit: 'enfoques',
      standToggle: 'Recordatorio para levantarse', standHint: 'Avisa en la isla después de un rato sentado; se reinicia al bloquear la pantalla',
      standEvery: 'Avisar después de', standTest: 'Probar aviso de levantarse',
      focusNeedsIsland: 'El Pomodoro y el recordatorio para levantarse funcionan con la isla activada.',
      pomoSound: 'Sonido al terminar', snd_chime: 'Campanita', snd_drops: 'Gotitas', snd_bell: 'Campana', snd_none: 'Sin sonido',
      osdToggle: 'Indicadores en la isla', osdHint: 'El volumen, la música, el brillo, Bloq Mayús, Bloq Num y el micrófono silenciado aparecen en la isla, en lugar de los avisos de Windows',
      otherScreen: 'Otro monitor en pantalla completa', otherScreenHint: 'Con un juego o video en pantalla completa en el monitor principal, la isla pasa al otro monitor',
      waterTest: 'Probar aviso', waterSound: 'Reproducir sonido',
    },
  };

  const t = (lang, key) => (I18N[lang] && I18N[lang][key]) || I18N.pt[key] || key;
  const langTag = (lang) => ({ pt: 'pt-BR', en: 'en', es: 'es' }[lang] || 'pt-BR');

  // Troca os textos marcados com data-i18n / data-i18n-title / data-i18n-aria
  function applyI18n(lang) {
    document.querySelectorAll('[data-i18n]').forEach((el) => { el.textContent = t(lang, el.dataset.i18n); });
    document.querySelectorAll('[data-i18n-title]').forEach((el) => { el.title = t(lang, el.dataset.i18nTitle); });
    document.querySelectorAll('[data-i18n-placeholder]').forEach((el) => { el.placeholder = t(lang, el.dataset.i18nPlaceholder); });
    document.querySelectorAll('[data-i18n-aria]').forEach((el) => { el.setAttribute('aria-label', t(lang, el.dataset.i18nAria)); });
  }

  /* ---------------- Novidades ----------------
     Aparecem no popup do mixer (uma vez, depois de atualizar) e na página "Sobre" das configurações.
     Para a próxima versão: troque "version" e os itens (nos 3 idiomas). O ícone é uma chave de NEWS_ICONS. */
  const NEWS = {
    version: '0.4.0',
    items: [
      { icon: 'island',
        pt: ['Ilha nova', 'Formato novo, animação com mola mais fluida e a opção de ilha flutuante (Configurações → Ilha). Gire a rodinha em cima dela para o volume, arraste para trocar de música e segure para abrir o mixer.'],
        en: ['A new island', 'New shape, a smoother spring animation and a floating option (Settings → Island). Scroll on it for volume, drag to change songs and hold to open the mixer.'],
        es: ['Una isla nueva', 'Forma nueva, animación con resorte más fluida y la opción de isla flotante (Configuración → Isla). Gira la rueda sobre ella para el volumen, arrastra para cambiar de canción y mantén pulsado para abrir el mezclador.'] },
      { icon: 'bolt',
        pt: ['Avisos só na ilha', 'Volume, teclas de música e brilho aparecem na ilha, no lugar dos avisos do Windows.'],
        en: ['Alerts only on the island', 'Volume, media keys and brightness show up on the island instead of the Windows pop-ups.'],
        es: ['Avisos solo en la isla', 'El volumen, las teclas de música y el brillo aparecen en la isla, en lugar de los avisos de Windows.'] },
      { icon: 'sparkle',
        pt: ['Atividades ao vivo', 'A onda acompanha a música na cor da capa, e a ilha avisa quando o microfone ou a câmera estão em uso, quando o carregador é ligado e quando um fone conecta.'],
        en: ['Live activities', 'The wave follows the music in the cover color, and the island shows when the microphone or camera is in use, when the charger is plugged in and when headphones connect.'],
        es: ['Actividades en vivo', 'La onda sigue la música con el color de la portada, y la isla avisa cuando el micrófono o la cámara están en uso, al conectar el cargador y cuando se conectan auriculares.'] },
      { icon: 'calendar',
        pt: ['Agenda na ilha', 'Cole o link iCal do Google Agenda ou do Outlook (Configurações → Agenda) e a ilha avisa 10 min antes de cada compromisso.'],
        en: ['Calendar on the island', 'Paste your Google Calendar or Outlook iCal link (Settings → Calendar) and the island alerts you 10 min before each event.'],
        es: ['Agenda en la isla', 'Pega el enlace iCal de Google Calendar u Outlook (Configuración → Agenda) y la isla te avisa 10 min antes de cada evento.'] },
      // Só na versão da Store (o Windows só deixa ler as notificações com identidade de pacote)
      { icon: 'chat', storeOnly: true,
        pt: ['Notificações dos apps', 'WhatsApp, Discord, e-mail... podem aparecer na ilha. Ligue em Configurações → Ilha.'],
        en: ['App notifications', 'WhatsApp, Discord, email... can show up on the island. Turn it on in Settings → Island.'],
        es: ['Notificaciones de las apps', 'WhatsApp, Discord, correo... pueden aparecer en la isla. Actívalo en Configuración → Isla.'] },
      // Só para quem instalou pelo site/GitHub: o Volum agora também está na Microsoft Store
      { icon: 'store', githubOnly: true, link: 'store',
        pt: ['O Volum está na Microsoft Store', 'Agora dá para instalar e atualizar o Volum direto pela Microsoft Store.', 'Ver na Microsoft Store'],
        en: ['Volum is on the Microsoft Store', 'You can now install and update Volum right from the Microsoft Store.', 'View on Microsoft Store'],
        es: ['Volum está en Microsoft Store', 'Ahora puedes instalar y actualizar Volum directamente desde Microsoft Store.', 'Ver en Microsoft Store'] },
    ],  };
  // Ícones de traço em caixa de 24 (os mesmos do mixer e das configurações; classe .ico no style.css)
  const NEWS_ICONS = {
    keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="3"/><path d="M6.5 10h.01M10 10h.01M14 10h.01M17.5 10h.01" stroke-width="2.2"/><path d="M8.5 14h7"/>',
    bolt: '<path d="M13 2.5 5 13.5h6l-1 8 8-11h-6z"/>',
    dock: '<rect x="3" y="15" width="18" height="5.5" rx="2"/><path d="M12 3v8M8.5 7.5 12 11l3.5-3.5"/>',
    chat: '<path d="M7 4.5h10a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-5.5L7 21v-3.5a3 3 0 0 1-3-3v-7a3 3 0 0 1 3-3z"/>',
    drop: '<path d="M12 2.8c3.7 4.3 6.6 8 6.6 11.4a6.6 6.6 0 0 1-13.2 0C5.4 10.8 8.3 7.1 12 2.8z"/>',
    cup: '<path d="M5 4h14l-1.6 15.2a2 2 0 0 1-2 1.8H8.6a2 2 0 0 1-2-1.8z"/><path d="M5.6 9.5h12.8"/>',
    sparkle: '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M18.4 5.6l-2.8 2.8M8.4 15.6l-2.8 2.8"/>',
    store: '<path d="M5 8h14l-1 12H6z"/><path d="M9 8V6.5a3 3 0 0 1 6 0V8"/>',
    fix: '<path d="M14.7 6.3a4 4 0 0 0-5.4 5.2L4 16.8V20h3.2l5.3-5.3a4 4 0 0 0 5.2-5.4l-2.4 2.4-2.8-.8-.8-2.8z"/>',
    walk: '<circle cx="13" cy="4" r="1.8"/><path d="M9 21l3-6 2.5 2.5V21M12 15l-1.5-5 3.5-2 2.5 3 2.5 1M10.5 10l-3 1.5-1 3"/>',
    island: '<rect x="3" y="8" width="18" height="8" rx="4"/>',
    calendar: '<rect x="3.5" y="5" width="17" height="15.5" rx="3"/><path d="M3.5 10h17M8 3v4M16 3v4"/>',
    timer: '<circle cx="12" cy="13.5" r="7.5"/><path d="M12 9.5v4l2.5 2M9.5 2.5h5M12 2.5V6"/>',
    gear: '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
  };

  // Itens da lista de novidades (<li> com ícone, título e texto), no idioma pedido
  // store: a cópia veio da Microsoft Store (aí os itens "githubOnly" não aparecem)
  function newsItems(lang, store = false) {
    return NEWS.items.filter((item) => !(item.githubOnly && store) && !(item.storeOnly && !store)).map((item) => {
      const [head, text, linkLabel] = item[lang] || item.pt;
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
      if (item.link) {   // botão que abre um endereço fixo pelo C# (openSite)
        const btn = document.createElement('button');
        btn.className = 'news-link';
        btn.dataset.site = item.link;
        btn.textContent = linkLabel;
        li.lastElementChild.append(btn);
      }
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

  /* ---------------- Mascote em pixel art (16 bits) ----------------
     Usado na ilha (ociosa) e no mixer (cabeçalho e popup de novidades). As animações ficam no mascot.css.
     Os desenhos são mapas de caracteres: cada letra é um pixel de uma cor da paleta e "." é vazio. */
  // Junta pixels vizinhos da mesma cor numa linha só (menos <rect> na página)
  function pixels(rows, pal, ox = 0, oy = 0) {
    let out = '';
    rows.forEach((row, y) => {
      for (let x = 0; x < row.length;) {
        const ch = row[x];
        if (ch === '.' || !pal[ch]) { x++; continue; }
        let w = 1;
        while (row[x + w] === ch) w++;
        out += `<rect x="${ox + x}" y="${oy + y}" width="${w}" height="1" fill="${pal[ch]}"/>`;
        x += w;
      }
    });
    return out;
  }

  // Mascote: rosto branco (sombra embaixo) e os olhos em "degradê" de 4 tons, do azul-escuro ao claro
  const M_PAL = { W: '#f9f9f9', S: '#c9ced6', a: '#002e5f', b: '#0d4077', c: '#3d6a99', d: '#a9c0d9' };
  const M_FACE = [
    '..WWWWWWWWWWWW..',
    '.WWWWWWWWWWWWWW.',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'SWWWWWWWWWWWWWWS',
    '.SSSSSSSSSSSSSS.',
  ];
  // Só os olhos mudam de um quadro para outro (linhas 2 a 5 do rosto)
  const M_EYES = {
    open:  ['...aa......aa...', '...bb......bb...', '...cc......cc...', '...dd......dd...'],
    half:  ['................', '...bb......bb...', '...cc......cc...', '...dd......dd...'],   // pálpebra descendo
    blink: ['................', '................', '................', '...bb......bb...'],
    happy: ['................', '...aa......aa...', '..a..a....a..a..', '................'],
    left:  ['..aa......aa....', '..bb......bb....', '..cc......cc....', '..dd......dd....'],
    right: ['....aa......aa..', '....bb......bb..', '....cc......cc..', '....dd......dd..'],
  };
  const MASCOT_PX =
    '<svg class="mascot px" viewBox="0 -1 16 11" aria-hidden="true">' +
    '<g class="px-body">' + pixels(M_FACE, M_PAL) +
    Object.entries(M_EYES).map(([k, rows]) => `<g class="pe pe-${k}">${pixels(rows, M_PAL, 0, 2)}</g>`).join('') +
    '</g></svg>';


  return { I18N, t, langTag, applyI18n, NEWS, newsItems, compareVersions, fmtMl, fmtMin, pixels, MASCOT_PX,
    MASCOT_PARTS: { pal: M_PAL, face: M_FACE, eyes: M_EYES } };   // o site monta o mascote interativo com estas peças
})();
