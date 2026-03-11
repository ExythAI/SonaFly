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
            main: '#00E5FF',
            light: '#18FFFF',
            dark: '#00B8D4',
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
        h4: { fontWeight: 700, letterSpacing: '-0.02em' },
        h5: { fontWeight: 600, letterSpacing: '-0.01em' },
        h6: { fontWeight: 600 },
        subtitle1: { fontWeight: 500 },
        button: { fontWeight: 600, textTransform: 'none' },
    },
    shape: { borderRadius: 12 },
    components: {
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
                    '&:hover': {
                        borderColor: 'rgba(124, 77, 255, 0.3)',
                        boxShadow: '0 4px 20px rgba(124, 77, 255, 0.15)',
                    },
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
