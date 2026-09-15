import type { ReactNode } from 'react';
import { Alert, Box, Button, Skeleton, Stack, Typography } from '@mui/material';
import { errorMessage } from './Feedback';

export function PageHeader({ title, subtitle, action }: { title: string; subtitle: string; action?: ReactNode }) {
    return <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'center' }} spacing={2} mb={3}>
        <Box><Typography variant="h4" component="h1">{title}</Typography><Typography color="text.secondary" mt={0.75}>{subtitle}</Typography></Box>
        {action && <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>{action}</Stack>}
    </Stack>;
}
export function QueryError({ error, retry }: { error: unknown; retry: () => unknown }) {
    return <Alert severity="error" sx={{ my: 2 }} action={<Button color="inherit" onClick={() => retry()}>Retry</Button>}>{errorMessage(error)}</Alert>;
}
export function PageLoading() {
    return <Stack spacing={2} aria-label="Loading content" aria-busy="true"><Skeleton width="35%" height={56} /><Skeleton variant="rounded" height={180} /><Skeleton variant="rounded" height={120} /></Stack>;
}
export function EmptyState({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
    return <Box sx={{ p: { xs: 3, md: 6 }, textAlign: 'center', border: '1px dashed', borderColor: 'divider', borderRadius: 3 }}>
        <Typography variant="h6">{title}</Typography><Typography color="text.secondary" mt={1} mb={2}>{description}</Typography>{action}
    </Box>;
}
