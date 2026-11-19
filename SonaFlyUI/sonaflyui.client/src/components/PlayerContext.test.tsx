import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { PlayerProvider, usePlayer, type Track } from './PlayerContext';
import { streamUrl } from '../api/client';

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true }) }));
vi.mock('../api/client', () => ({ streamUrl: vi.fn(), reportPlay: vi.fn(), artworkUrl: vi.fn() }));
let player: ReturnType<typeof usePlayer>;
let audio: FakeAudio;
class FakeAudio extends EventTarget {
    src = ''; currentTime = 0; duration = 120; volume = 1; readyState = 1;
    onloadedmetadata: (() => void) | null = null;
    static instances: FakeAudio[] = [];
    constructor() { super(); FakeAudio.instances.push(this); }
    play = vi.fn(async () => { this.onloadedmetadata?.(); this.dispatchEvent(new Event('playing')); });
    pause() { this.dispatchEvent(new Event('pause')); }
    load() { /* no network in unit tests */ }
    removeAttribute(name: string) { if (name === 'src') this.src = ''; }
    getAttribute(name: string) { return name === 'src' ? this.src : null; }
}
const first: Track = { id: 'first', title: 'First' };
const second: Track = { id: 'second', title: 'Second' };
function Probe() { player = usePlayer(); return null; }
beforeEach(() => {
    vi.stubGlobal('Audio', FakeAudio);
    vi.mocked(streamUrl).mockReset().mockResolvedValue('/authorized-audio');
    render(<PlayerProvider><Probe /></PlayerProvider>);
    audio = FakeAudio.instances[FakeAudio.instances.length - 1];
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

test('a slow previous request cannot replace the newly selected track', async () => {
    let resolveFirst!: (url: string) => void;
    vi.mocked(streamUrl).mockImplementationOnce(() => new Promise(resolve => { resolveFirst = resolve; }));
    act(() => player.play(first));
    await act(async () => player.play(second));
    await act(async () => resolveFirst('/stale-audio'));
    expect(player.currentTrack?.id).toBe('second');
    expect(audio.src).toBe('/authorized-audio');
});

test('stopping during loading prevents playback after the URL arrives', async () => {
    let resolve!: (url: string) => void;
    vi.mocked(streamUrl).mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    act(() => player.play(first));
    act(() => player.stop());
    await act(async () => resolve('/late-audio'));
    expect(player.currentTrack).toBeNull();
    expect(audio.play).not.toHaveBeenCalled();
});

test('single-track playback replaces the old queue', async () => {
    await act(async () => player.play(first, [first, second]));
    await act(async () => player.play(second));
    expect(player.queue).toEqual([second]);
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(vi.mocked(streamUrl)).toHaveBeenCalledTimes(2);
});

test('duplicate tracks in a queue advance by position', async () => {
    await act(async () => player.play(first, [first, first, second]));
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(player.currentIndex).toBe(1);
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(player.currentIndex).toBe(2);
    expect(player.currentTrack?.id).toBe('second');
});

test('room playback seeks on load and waits for the server at track end', async () => {
    await act(async () => player.playShared(first, 30, 'Test room'));
    expect(audio.currentTime).toBeGreaterThanOrEqual(30);
    act(() => player.seek(0));
    expect(audio.currentTime).toBeGreaterThanOrEqual(30);
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(vi.mocked(streamUrl)).toHaveBeenCalledTimes(1);
    expect(player.sharedRoom).toBe('Test room');
});

test('streaming failures have a visible error state and can be retried', async () => {
    vi.mocked(streamUrl).mockRejectedValueOnce(new Error('No access to this track'));
    await act(async () => player.play(first));
    expect(player.playbackError).toBe('No access to this track');
    expect(player.isBuffering).toBe(false);
    await act(async () => player.retry());
    expect(player.playbackError).toBeNull();
    expect(player.isPlaying).toBe(true);
});

const third: Track = { id: 'third', title: 'Third' };

test('repeat-all wraps to the start, and repeat-one replays the same track', async () => {
    await act(async () => player.play(second, [first, second]));
    expect(player.hasNext).toBe(false);

    act(() => player.cycleRepeat());
    expect(player.repeat).toBe('all');
    expect(player.hasNext).toBe(true);
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(player.currentTrack?.id).toBe('first');

    act(() => player.cycleRepeat());
    expect(player.repeat).toBe('one');
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(player.currentTrack?.id).toBe('first');
    expect(player.currentIndex).toBe(0);

    act(() => player.cycleRepeat());
    expect(player.repeat).toBe('off');
});

test('the end of the queue stops there when repeat is off', async () => {
    await act(async () => player.play(second, [first, second]));
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(player.currentTrack?.id).toBe('second');
    expect(player.isPlaying).toBe(false);
    expect(vi.mocked(streamUrl)).toHaveBeenCalledTimes(1);
});

test('play next goes straight after the current track, and add to queue goes last', async () => {
    await act(async () => player.play(first, [first, second]));
    act(() => player.playNext(third));
    act(() => player.addToQueue([first]));
    expect(player.queue.map(t => t.id)).toEqual(['first', 'third', 'second', 'first']);
    // The track that was playing keeps playing.
    expect(player.currentTrack?.id).toBe('first');
    expect(vi.mocked(streamUrl)).toHaveBeenCalledTimes(1);
    await act(async () => audio.dispatchEvent(new Event('ended')));
    expect(player.currentTrack?.id).toBe('third');
});

test('queueing with nothing playing starts playback', async () => {
    await act(async () => player.addToQueue([second, third]));
    expect(player.currentTrack?.id).toBe('second');
    expect(player.queue).toHaveLength(2);
});

test('turning shuffle on keeps the current track first, and off restores the order', async () => {
    const list = [first, second, third];
    await act(async () => player.play(second, list));

    act(() => player.toggleShuffle());
    expect(player.shuffle).toBe(true);
    expect(player.currentIndex).toBe(0);
    expect(player.queue[0].id).toBe('second');
    expect(player.queue.map(t => t.id).sort()).toEqual(['first', 'second', 'third']);
    expect(vi.mocked(streamUrl)).toHaveBeenCalledTimes(1);

    act(() => player.toggleShuffle());
    expect(player.shuffle).toBe(false);
    expect(player.queue.map(t => t.id)).toEqual(['first', 'second', 'third']);
    expect(player.currentIndex).toBe(1);
});

test('previous restarts a track that is under way, and goes back when it has only just begun', async () => {
    await act(async () => player.play(second, [first, second]));
    audio.currentTime = 42;
    act(() => player.previous());
    expect(audio.currentTime).toBe(0);
    expect(player.currentTrack?.id).toBe('second');

    await act(async () => player.previous());
    expect(player.currentTrack?.id).toBe('first');
});

test('space pauses and resumes, but not while typing in a field', async () => {
    await act(async () => player.play(first));
    const pauseSpy = vi.spyOn(audio, 'pause');
    act(() => { window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true })); });
    expect(pauseSpy).toHaveBeenCalledTimes(1);

    const input = document.createElement('input');
    document.body.appendChild(input);
    act(() => { input.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true })); });
    expect(pauseSpy).toHaveBeenCalledTimes(1);
    input.remove();
});
