import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { useSearchFilter } from './useSearchFilter';

function SearchProbe() {
    const { input, setInput, value } = useSearchFilter();
    const location = useLocation();
    const navigate = useNavigate();
    return <><input aria-label="Search" value={input} onChange={e => setInput(e.target.value)} />
        <output aria-label="Query">{value}</output><output aria-label="Location">{location.search}</output>
        <button onClick={() => navigate('/tracks?q=other')}>Other search</button><button onClick={() => navigate(-1)}>Back</button></>;
}
beforeEach(() => vi.useFakeTimers());
afterEach(() => { cleanup(); vi.useRealTimers(); });
test('debounces searches and resets pagination while preserving sorting', () => {
    render(<MemoryRouter initialEntries={['/tracks?page=4&sort=artist']}><SearchProbe /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'Coastline' } });
    expect(screen.getByLabelText('Query').textContent).toBe('');
    act(() => vi.advanceTimersByTime(300));
    expect(screen.getByLabelText('Query').textContent).toBe('Coastline');
    expect(screen.getByLabelText('Location').textContent).toBe('?sort=artist&q=Coastline');
});
test('back navigation restores the visible search input', () => {
    render(<MemoryRouter initialEntries={['/tracks?q=original']}><SearchProbe /></MemoryRouter>);
    fireEvent.click(screen.getByText('Other search'));
    expect((screen.getByLabelText('Search') as HTMLInputElement).value).toBe('other');
    fireEvent.click(screen.getByText('Back'));
    act(() => vi.advanceTimersByTime(300));
    expect((screen.getByLabelText('Search') as HTMLInputElement).value).toBe('original');
    expect(screen.getByLabelText('Location').textContent).toBe('?q=original');
});
