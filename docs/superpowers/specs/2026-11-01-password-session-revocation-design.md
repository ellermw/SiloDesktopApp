# Password session revocation

Approved scope: task A, desktop base `a15621a0d6d5960d6b31a620a23ae259d734dedb`, source version 1.2.519. Official server/WebUI reference: `63ef34b236b88dc77acca1a9a3ed90235377691f`, `internal/apiv2/account.go` and `web/src/pages/settings/AccountSettings.tsx`.

Expose an initially unchecked **Sign out of other devices** choice only when password changes are allowed and the capability advertises `sign_out_other_sessions`. Explain that other browsers/apps must sign in again, this app stays signed in, and API keys keep working. Omit the request member unless supported and selected; older servers reject even false. The server owns revocation; preserve the current client sign-in.

Extend the existing capability/API/view-model/view chain. Capture the selected request before marking submission busy, disable selection while pending, and derive success feedback from the captured request. Clear the choice on success, reload, page departure and account/profile change. Preserve existing validation, cancellation, generations and input clearing.

Approaches: extending this form preserves its existing request ownership and is the selected implementation. A separate revocation endpoint or local session sign-out would duplicate server authority and violate the approved contract.

Verification uses synthetic HTTP and real native controls, never real accounts. Cover absent/false/true capability, omission/opt-in payload, unavailable/read-only accounts, delayed capability/submission, reset and retained current authentication. Native checks visibility, unchecked default, input binding, disabled pending state and bounded wrapped layout. Release app publish is required; no installer or installed-player action and no release version bump.
