import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { PlayerProvider, usePlayer, type Track } from './PlayerContext';
import { streamUrl } from '../api/client';

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true }) }));
vi.mock('../api/client', () => ({ streamUrl: vi.fn() }));
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
