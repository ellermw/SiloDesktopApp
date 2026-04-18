# Setup Wizard — Full Audit

Webui: SetupWizard.tsx + setup-wizard/steps/ (8 steps)
Desktop: SetupWizardPage.xaml + .xaml.cs + SetupWizardViewModel.cs (5 steps)

## Status: Functional — missing 3 optional steps

### Already matching
- Step 1: Account (username, email, password)
- Step 2: Profile (name)
- Step 3: Library (name, type, paths, scan toggle)
- Step 4: Server (Redis, FFmpeg, transcode dir, hw accel, transcoding toggle, Jellyfin compat, S3 storage)
- Step indicator with dots + labels
- Glass card styling
- Next/Back/Finish navigation
- DetermineStartingStepAsync (skip completed steps)

### Gaps

| # | Sev | Gap | Effort |
|---|-----|-----|--------|
| 1 | P1 | Integrations step missing (subtitle providers) | medium |
| 2 | P1 | Downloads step missing (download settings) | small |
| 3 | P1 | Recommendations step missing (pgvector/embeddings) | medium |
| 4 | P2 | Library type missing music/live-tv options | small |
| 5 | P2 | Step change animation (webui animates, desktop toggles visibility) | small |
