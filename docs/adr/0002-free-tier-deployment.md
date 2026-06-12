# 0002 — Free-tier deployment: Vercel + Render + Neon Postgres

## Status
Accepted (2026-06-12)

## Context
Stack is Angular (frontend) and .NET C# (backend). The user wants to deploy on Vercel and Render.com at minimal cost. Constraints discovered:

- Render free web services sleep after 15 min idle (~50 s cold start). EA ingestion tolerates this because submissions are idempotent and retried — data is delayed, not lost.
- Render's free Postgres is a 30-day trial and is then deleted, so it is unsuitable for persistent trade history.

## Decision
- Angular SPA on **Vercel** (free).
- .NET 8 Web API as a Docker service on **Render free tier**, accepting cold starts.
- **Neon.tech free Postgres** as the database instead of Render Postgres.

## Consequences
- $0/month to start; upgrade path is Render Starter ($7/mo) if cold-start delays become annoying.
- Database lives outside Render — connection string crosses providers; latency is acceptable for this workload.
- The collector EA must retry failed pushes to ride out backend cold starts.
