// A defensive normal-speed guard for HTML media in the native embed document.
// This cannot correct emulator audio clocks, WebAudio buffers or source recordings.
const spotifyAudioGuard = r'''
(() => {
  if (window.__moodifyAudioGuard) return;
  window.__moodifyAudioGuard = true;
  const attached = new WeakSet();
  function normalSpeed(media) {
    if (!(media instanceof HTMLMediaElement)) return;
    if (media.defaultPlaybackRate !== 1) media.defaultPlaybackRate = 1;
    if (media.playbackRate !== 1) media.playbackRate = 1;
    if ('preservesPitch' in media) media.preservesPitch = true;
    if ('webkitPreservesPitch' in media) media.webkitPreservesPitch = true;
  }
  function bind(media) {
    if (attached.has(media)) return;
    attached.add(media);
    normalSpeed(media);
    for (const event of ['play', 'loadedmetadata', 'ratechange']) {
      media.addEventListener(event, () => normalSpeed(media));
    }
  }
  function scan(root) {
    if (root instanceof HTMLMediaElement) bind(root);
    root.querySelectorAll?.('audio,video').forEach(bind);
  }
  // Spotify preview uses new Audio() without attaching it to the document.
  // Preserve the original play promise and browser user-gesture restrictions.
  const originalPlay = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function(...args) {
    bind(this);
    normalSpeed(this);
    return Reflect.apply(originalPlay, this, args);
  };
  scan(document);
  new MutationObserver(records => {
    for (const record of records) for (const node of record.addedNodes) scan(node);
  }).observe(document.documentElement, {childList: true, subtree: true});
})();
''';
