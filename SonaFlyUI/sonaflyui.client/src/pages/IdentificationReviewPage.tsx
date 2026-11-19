import React, { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
    Alert, Box, Button, Card, CardContent, Checkbox, Chip, Stack, Table, TableBody, TableCell,
    TableContainer, TableHead, TableRow, ToggleButton, ToggleButtonGroup, Tooltip, Typography,
} from '@mui/material';
import { ArrowForward, Check, Close, DoneAll, Undo } from '@mui/icons-material';
import { identificationApi } from '../api/client';
import type { ReviewDecision, ReviewItemDto, ReviewOutcomeDto } from '../api/types';
import { EmptyState, PageHeader, PageLoading, QueryError } from '../components/PageParts';
import { errorMessage, notify } from '../components/Feedback';

const PAGE_SIZE = 50;
// The server takes at most this many decisions in one request.
const BATCH = 500;

const BANDS = [
    { value: '', label: 'All' },
    { value: 'High', label: 'High confidence' },
    { value: 'Review', label: 'Needs a look' },
    { value: 'Unresolved', label: 'Weak' },
];

const bandColor = (band: string | null): 'success' | 'warning' | 'default' =>
    band === 'High' ? 'success' : band === 'Review' ? 'warning' : 'default';

const decisionFor = (item: ReviewItemDto): ReviewDecision =>
    ({ workItemId: item.workItemId, candidateId: item.candidateId, title: true, artist: true });

/** Says what happened, including anything the server declined and why. */
const report = (verb: string, outcome: ReviewOutcomeDto) => {
    const skipped = outcome.skipped.length;
    if (skipped === 0) {
        notify(`${verb} ${outcome.done} ${outcome.done === 1 ? 'track' : 'tracks'}.`);
        return;
    }
    const reason = outcome.skipped[0].reason;
    notify(`${verb} ${outcome.done}; ${skipped} skipped. ${skipped === 1 ? reason : `First reason: ${reason}`}`, 'info');
};

/** One side of a proposed change: the value, emphasised when it is the part that changes. */
const Value: React.FC<{ text: string | null; changed: boolean; muted?: boolean }> = ({ text, changed, muted }) => (
    <Typography variant="body2" noWrap title={text ?? ''}
        sx={{ fontWeight: changed && !muted ? 600 : 400, color: muted ? 'text.secondary' : changed ? 'success.light' : 'text.primary' }}>
        {text || '—'}
    </Typography>
);

const IdentificationReviewPage: React.FC = () => {
    const qc = useQueryClient();
    const [band, setBand] = useState('High');
    const [skip, setSkip] = useState(0);
    const [selected, setSelected] = useState<Set<string>>(new Set());

    const pending = useQuery({
        queryKey: ['identification-review', band, skip],
        queryFn: () => identificationApi.review(band, skip, PAGE_SIZE).then(r => r.data),
    });
    const applied = useQuery({
        queryKey: ['identification-applied'],
        queryFn: () => identificationApi.applied().then(r => r.data),
    });

    const items = useMemo(() => pending.data?.items ?? [], [pending.data]);
    const chosen = items.filter(i => selected.has(i.workItemId));

    const refresh = () => {
        setSelected(new Set());
        // Titles and artists across the library may have changed.
        void qc.invalidateQueries();
    };

    const applyMut = useMutation({
        meta: { quiet: true },
        mutationFn: (decisions: ReviewDecision[]) => identificationApi.apply(decisions).then(r => r.data),
        onSuccess: outcome => { report('Corrected', outcome); refresh(); },
        onError: err => notify(errorMessage(err), 'error'),
    });
    const rejectMut = useMutation({
        meta: { quiet: true },
        mutationFn: (decisions: ReviewDecision[]) => identificationApi.reject(decisions).then(r => r.data),
        onSuccess: outcome => { report('Dismissed', outcome); refresh(); },
        onError: err => notify(errorMessage(err), 'error'),
    });
    const revertMut = useMutation({
        meta: { quiet: true },
        mutationFn: (proposalId: string) => identificationApi.revert(proposalId),
        onSuccess: () => { notify('Change undone.'); refresh(); },
        onError: err => notify(errorMessage(err), 'error'),
    });

    // Every high-confidence match, not just the ones on this page.
    const applyAllHighMut = useMutation({
        meta: { quiet: true },
        mutationFn: async () => {
            const all: ReviewDecision[] = [];
            for (let offset = 0; ; offset += 200) {
                const page = (await identificationApi.review('High', offset, 200)).data;
                all.push(...page.items.map(decisionFor));
                if (page.items.length < 200) break;
            }
            const total: ReviewOutcomeDto = { done: 0, skipped: [] };
            for (let start = 0; start < all.length; start += BATCH) {
                const outcome = (await identificationApi.apply(all.slice(start, start + BATCH))).data;
                total.done += outcome.done;
                total.skipped.push(...outcome.skipped);
            }
            return total;
        },
        onSuccess: outcome => { report('Corrected', outcome); refresh(); },
        onError: err => notify(errorMessage(err), 'error'),
    });

    const busy = applyMut.isPending || rejectMut.isPending || applyAllHighMut.isPending || revertMut.isPending;
    const allOnPage = items.length > 0 && items.every(i => selected.has(i.workItemId));

    const toggle = (id: string) => setSelected(current => {
        const next = new Set(current);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
    });
    const togglePage = () => setSelected(allOnPage ? new Set() : new Set(items.map(i => i.workItemId)));
    const changeBand = (value: string) => { setBand(value); setSkip(0); setSelected(new Set()); };

    const total = pending.data?.total ?? 0;
    const high = pending.data?.highConfidence ?? 0;

    return (
        <Box>
            <PageHeader title="Review matches"
                subtitle="Tracks whose identified title or artist differs from what the library shows. Applying a match corrects the library; your audio files are not modified."
                action={high > 0 ? (
                    <Button variant="contained" startIcon={<DoneAll />} disabled={busy}
                        onClick={() => applyAllHighMut.mutate()}>
                        {applyAllHighMut.isPending ? 'Applying…' : `Apply all ${high} high-confidence`}
                    </Button>
                ) : undefined} />

            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }} justifyContent="space-between" mb={2}>
                <ToggleButtonGroup size="small" exclusive value={band} aria-label="Confidence"
                    sx={{ '& .MuiToggleButton-root': { px: 1.75, textTransform: 'none' } }}
                    onChange={(_, value: string | null) => { if (value !== null) changeBand(value); }}>
                    {BANDS.map(b => <ToggleButton key={b.value} value={b.value}>{b.label}</ToggleButton>)}
                </ToggleButtonGroup>
                <Stack direction="row" spacing={1}>
                    <Button variant="contained" color="success" startIcon={<Check />} disabled={busy || chosen.length === 0}
                        onClick={() => applyMut.mutate(chosen.map(decisionFor))}>
                        Apply selected{chosen.length > 0 ? ` (${chosen.length})` : ''}
                    </Button>
                    <Button variant="outlined" startIcon={<Close />} disabled={busy || chosen.length === 0}
                        onClick={() => rejectMut.mutate(chosen.map(decisionFor))}>
                        Not a match
                    </Button>
                </Stack>
            </Stack>

            {pending.isLoading && <PageLoading />}
            {pending.isError && <QueryError error={pending.error} retry={pending.refetch} />}

            {pending.data && items.length === 0 && (
                <EmptyState title="Nothing to review"
                    description={total === 0 && band
                        ? 'No matches in this confidence band. Try All, or run identification from Music folders.'
                        : 'Every identified track already agrees with the library. Run identification from Music folders to analyse more.'} />
            )}

            {items.length > 0 && (
                <Card>
                    <TableContainer>
                        <Table size="small" sx={{ tableLayout: 'fixed' }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell padding="checkbox">
                                        <Checkbox checked={allOnPage} indeterminate={!allOnPage && chosen.length > 0}
                                            onChange={togglePage} slotProps={{ input: { 'aria-label': 'Select every match on this page' } }} />
                                    </TableCell>
                                    <TableCell sx={{ width: '36%' }}>In the library now</TableCell>
                                    <TableCell sx={{ width: 40 }} />
                                    <TableCell sx={{ width: '36%' }}>Identified as</TableCell>
                                    <TableCell>Confidence</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {items.map(item => (
                                    <TableRow key={item.workItemId} hover selected={selected.has(item.workItemId)}
                                        onClick={() => toggle(item.workItemId)} sx={{ cursor: 'pointer' }}>
                                        <TableCell padding="checkbox">
                                            <Checkbox checked={selected.has(item.workItemId)}
                                                slotProps={{ input: { 'aria-label': `Select ${item.currentTitle}` } }} />
                                        </TableCell>
                                        <TableCell>
                                            <Value text={item.currentTitle} changed={item.titleChanges} muted />
                                            <Value text={item.currentArtist} changed={item.artistChanges} muted />
                                            <Typography variant="caption" color="text.disabled" noWrap display="block"
                                                title={item.relativePath ?? ''}>
                                                {item.currentAlbum ? `${item.currentAlbum} · ` : ''}{item.relativePath ?? ''}
                                            </Typography>
                                        </TableCell>
                                        <TableCell><ArrowForward fontSize="small" color="disabled" /></TableCell>
                                        <TableCell>
                                            <Value text={item.titleChanges ? item.proposedTitle : item.currentTitle} changed={item.titleChanges} />
                                            <Value text={item.artistChanges ? item.proposedArtist : item.currentArtist} changed={item.artistChanges} />
                                            {item.conflicts && (
                                                <Typography variant="caption" color="warning.main" noWrap display="block" title={item.conflicts}>
                                                    {item.conflicts}
                                                </Typography>
                                            )}
                                        </TableCell>
                                        <TableCell>
                                            <Tooltip title={item.candidateCount > 1 ? `Best of ${item.candidateCount} possible matches` : 'The only match found'}>
                                                <Chip size="small" color={bandColor(item.confidenceBand)}
                                                    label={`${item.confidenceBand ?? 'Unknown'}${item.score != null ? ` · ${Math.round(item.score * 100)}` : ''}`} />
                                            </Tooltip>
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                    <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 1.5 }}>
                        <Typography variant="body2" color="text.secondary">
                            {skip + 1}–{skip + items.length} of {total}
                        </Typography>
                        <Stack direction="row" spacing={1}>
                            <Button size="small" disabled={skip === 0 || busy}
                                onClick={() => { setSkip(Math.max(0, skip - PAGE_SIZE)); setSelected(new Set()); }}>Previous</Button>
                            <Button size="small" disabled={skip + items.length >= total || busy}
                                onClick={() => { setSkip(skip + PAGE_SIZE); setSelected(new Set()); }}>Next</Button>
                        </Stack>
                    </Stack>
                </Card>
            )}

            <Typography variant="h6" sx={{ mt: 4, mb: 1 }}>Recent corrections</Typography>
            {applied.isError && <Alert severity="error">{errorMessage(applied.error)}</Alert>}
            {applied.data && applied.data.length === 0 && (
                <Typography variant="body2" color="text.secondary">Nothing has been applied yet.</Typography>
            )}
            {applied.data && applied.data.length > 0 && (
                <Card>
                    <CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}>
                        {applied.data.map(change => (
                            <Stack key={change.proposalId} direction="row" alignItems="center" spacing={2}
                                sx={{ py: 1, borderBottom: '1px solid rgba(255,255,255,0.04)' }}>
                                <Box sx={{ flex: 1, minWidth: 0 }}>
                                    <Typography variant="body2" noWrap>
                                        {change.newTitle}{change.newArtist ? ` — ${change.newArtist}` : ''}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" noWrap display="block">
                                        was {change.oldTitle}{change.oldArtist ? ` — ${change.oldArtist}` : ''}
                                        {change.appliedUtc ? ` · ${new Date(change.appliedUtc).toLocaleString()}` : ''}
                                    </Typography>
                                </Box>
                                <Button size="small" startIcon={<Undo />} disabled={busy}
                                    onClick={() => revertMut.mutate(change.proposalId)}>Undo</Button>
                            </Stack>
                        ))}
                    </CardContent>
                </Card>
            )}
        </Box>
    );
};

export default IdentificationReviewPage;
