import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import fs from 'node:fs';
import path from 'node:path';
import childProcess from 'node:child_process';
import { env } from 'node:process';

export default defineConfig(({ command }) => {
    const config = {
        plugins: [plugin()],
        resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
        build: {
            rollupOptions: {
                output: {
                    // Split the dependencies that change on their own schedule into
                    // separately cacheable chunks, so an app change does not invalidate
                    // all of React and MUI in every browser cache.
                    manualChunks(id) {
                        if (!id.includes('node_modules')) return;
                        // Rollup module ids always use forward slashes, on Windows too.
                        if (/\/node_modules\/(react|react-dom|react-router|react-router-dom|scheduler)\//.test(id)) return 'vendor-react';
                        if (id.includes('@mui') || id.includes('@emotion')) return 'vendor-mui';
                        if (id.includes('@tanstack')) return 'vendor-query';
                        if (id.includes('@microsoft/signalr')) return 'vendor-signalr';
                    },
                },
            },
        },
    };
    // Production builds do not serve HTTPS or need a local .NET certificate.
    if (command === 'build') return config;

    const baseFolder = env.APPDATA ? `${env.APPDATA}/ASP.NET/https` : `${env.HOME}/.aspnet/https`;
    const certFilePath = path.join(baseFolder, 'sonaflyui.client.pem');
    const keyFilePath = path.join(baseFolder, 'sonaflyui.client.key');
    fs.mkdirSync(baseFolder, { recursive: true });
    if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
        const result = childProcess.spawnSync('dotnet', [
            'dev-certs', 'https', '--export-path', certFilePath, '--format', 'Pem', '--no-password',
        ], { stdio: 'inherit' });
        if (result.status !== 0) throw new Error('Could not create certificate.');
    }

    const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
        env.ASPNETCORE_URLS?.split(';')[0] || 'https://localhost:7116';
    return {
        ...config,
        server: {
            proxy: { '/api': { target, secure: false }, '/hubs': { target, secure: false, ws: true } },
            port: parseInt(env.DEV_SERVER_PORT || '65457'),
            https: { key: fs.readFileSync(keyFilePath), cert: fs.readFileSync(certFilePath) },
        },
    };
});
