# Deployment Checklist

Profit Hub deploys as three pieces: a Neon Postgres database, the .NET API on
Render (Docker), and the Angular frontend on Vercel.

## Manual deploy steps

1. **Neon** — Create a Neon project and copy the **pooled** connection string.
2. **Render** — Push the repo to GitHub, then create a Render Blueprint from
   `render.yaml`. In the Render dashboard, set the `sync: false` env vars:
   - `ConnectionStrings__Default` → the Neon pooled connection string
   - `Cors__Origins` → the Vercel URL (e.g. `https://profit-hub.vercel.app`)

   `Jwt__Key` is generated automatically by Render.
3. **Vercel** — `cd frontend && npx vercel --prod` (project root directory =
   `frontend`). The SPA rewrite in `frontend/vercel.json` routes all paths to
   `index.html`.
4. **Smoke test** — register → add account → curl ingest with the Ingest Key →
   confirm the trade appears on the dashboard → export CSV.
5. **EA** — Update `ea/README.md` and the EA's default `ApiUrl` with the real
   Render URL.

## Notes

- The API service is named `profit-hub-api` in `render.yaml`, which produces the
  URL `https://profit-hub-api.onrender.com`. This matches
  `frontend/src/environments/environment.ts` (`apiUrl`).
- Render free-tier services sleep when idle; the first request after a sleep may
  take a few seconds.
