import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

// URL state preserves searches when navigating back from an album or artist.
export function useSearchFilter(key = 'q') {
    const [params, setParams] = useSearchParams();
    const value = params.get(key) ?? '';
    const [input, setInput] = useState(value);
    useEffect(() => { setInput(value); }, [value]);
    useEffect(() => {
        if (input === value) return;
        const timer = setTimeout(() => setParams(previous => {
            const next = new URLSearchParams(previous);
            if (input.trim()) next.set(key, input.trim()); else next.delete(key);
            next.delete('page');
            return next;
        }, { replace: true }), 300);
        return () => clearTimeout(timer);
    }, [input, value, key, setParams]);
    return { input, setInput, value };
}
