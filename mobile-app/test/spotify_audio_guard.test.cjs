const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../lib/services/playback/spotify_audio_guard.dart'), 'utf8');
const script = source.match(/r'''([\s\S]*?)'''/)[1];
test('normalizes existing and newly added media without seeking or starting playback', () => {
  class Media extends EventTarget {
    playbackRate = 2;
    defaultPlaybackRate = 2;
    preservesPitch = false;
    currentTime = 12;
    paused = true;
    play() { this.paused = false; return Promise.resolve(); }
  }
  const initial = new Media();
  let observe;
  const scope = {window: {}, HTMLMediaElement: Media,
    document: {documentElement: {}, querySelectorAll: () => [initial]},
    MutationObserver: class { constructor(callback) { observe = callback; } observe() {} },
  };
  vm.runInNewContext(script, scope);
  assert.equal(initial.playbackRate, 1);
  assert.equal(initial.defaultPlaybackRate, 1);
  assert.equal(initial.preservesPitch, true);
  assert.equal(initial.currentTime, 12);
  assert.equal(initial.paused, true);
  initial.playbackRate = 3;
  initial.dispatchEvent(new Event('ratechange'));
  assert.equal(initial.playbackRate, 1);
  const dynamic = new Media();
  observe([{addedNodes: [dynamic]}]);
  assert.equal(dynamic.playbackRate, 1);
  const detached = new Media();
  const playResult = detached.play();
  assert.ok(playResult instanceof Promise);
  assert.equal(detached.playbackRate, 1);
  assert.equal(detached.preservesPitch, true);
  assert.equal(detached.currentTime, 12);
  assert.equal(detached.paused, false);
  detached.playbackRate = 2;
  detached.dispatchEvent(new Event('ratechange'));
  assert.equal(detached.playbackRate, 1);
  const observer = observe;
  vm.runInNewContext(script, scope);
  assert.equal(observe, observer, 'must not install duplicate observers');
});
