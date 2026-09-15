import { createTheme } from '@mui/material/styles';

const theme = createTheme({
    palette: {
        mode: 'dark',
        primary: {
            main: '#7C4DFF',
            light: '#B388FF',
            dark: '#651FFF',
        },
        secondary: {
            main: '#A59AC8',
            light: '#C7BFDD',
            dark: '#76698F',
        },
        background: {
            default: '#0A0E17',
            paper: '#111827',
        },
        text: {
            primary: '#E8EAED',
            secondary: '#9AA0A6',
        },
        success: { main: '#00E676' },
        warning: { main: '#FFD600' },
        error: { main: '#FF5252' },
        divider: 'rgba(255,255,255,0.08)',
    },
    typography: {
        fontFamily: '"Inter", "Roboto", "Helvetica", "Arial", sans-serif',
        h4: { fontWeight: 700, letterSpacing: '-0.035em', fontSize: 'clamp(1.6rem, 3vw, 2.1rem)' },
        h5: { fontWeight: 600, letterSpacing: '-0.01em' },
        h6: { fontWeight: 600 },
        subtitle1: { fontWeight: 500 },
        button: { fontWeight: 600, textTransform: 'none' },
    },
    shape: { borderRadius: 12 },
    components: {
        MuiCssBaseline: { styleOverrides: {
            'html': { colorScheme: 'dark' },
            '*': { scrollbarWidth: 'thin', scrollbarColor: '#424857 transparent' },
            ':focus-visible': { outline: '2px solid #B388FF', outlineOffset: 3 },
            'img': { maxWidth: '100%' },
            '@media (prefers-reduced-motion: reduce)': { '*': { transition: 'none !important', animation: 'none !important' } },
        } },
        MuiCardActionArea: { styleOverrides: { root: { '&:hover': { backgroundColor: 'rgba(124,77,255,.06)' } } } },
        MuiPaper: {
            styleOverrides: {
                root: {
                    backgroundImage: 'none',
                    borderRadius: 12,
                },
            },
        },
        MuiButton: {
            styleOverrides: {
                root: {
                    borderRadius: 8,
                    padding: '8px 20px',
                },
                contained: {
                    boxShadow: '0 2px 8px rgba(124, 77, 255, 0.3)',
                },
            },
        },
        MuiTextField: {
            defaultProps: { variant: 'outlined', size: 'small' },
        },
        MuiCard: {
            styleOverrides: {
                root: {
                    border: '1px solid rgba(255,255,255,0.06)',
                    transition: 'border-color 0.2s, box-shadow 0.2s',

                },
            },
        },
        MuiDrawer: {
            styleOverrides: {
                paper: {
                    backgroundColor: '#0D1117',
                    borderRight: '1px solid rgba(255,255,255,0.06)',
                },
            },
        },
        MuiTableCell: {
            styleOverrides: {
                root: {
                    borderBottom: '1px solid rgba(255,255,255,0.06)',
                },
            },
        },
    },
});

export default theme;
