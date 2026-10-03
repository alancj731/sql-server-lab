// Dev-server proxy so the SPA and API share an origin (no CORS). API_URL lets e2e use a separate API port.
const target = process.env.API_URL ?? 'http://localhost:5080';

export default {
  '/api': { target, secure: false },
  '/hubs': { target, secure: false, ws: true },
  '/health': { target, secure: false },
  '/config.json': { target, secure: false },
};
