# Episode still quick play

Approved server-alignment report F; official source freshly verified `63ef34b236b88dc77acca1a9a3ed90235377691f`. This independent candidate starts from newer Beta `5a6555492801aa0172d2559257ef8711a8e0efe4` (the original `a15621a0` plus the updater title PR).

Match SeasonEpisodeGrid's separate play overlay on episode stills in series/season and shared sibling cards. Keep ordinary card navigation intact. Offer play only when the server episode files contain at least one file not explicitly unreadable. Do not infer availability from paths or artwork. Reuse normal authorized PlayerService playback and resume behavior with captured content/library/account context; never pass from-start or a zero position override.

The overlay is an accessible button, shown on hover or keyboard focus with a clear Play/Resume episode name. Its click cannot bubble into detail navigation; hover/focus never starts playback. Keep spoiler artwork and existing watched/more actions, including narrow cards.

Capture the original surface/navigation/item/library and account context. Retirement remains permanent across unload/reload. A queued playback request must reject a departed owner before canceling current playback or preparing the replacement; preserve existing room/authority gates. No test-only product injection seams.

Carry that owner through the transport drain and preparation to the actual context-bound capability/start HTTP requests and retries, with checks before native load. New viewer clicks supersede older pending viewer starts. Accepted cancellation releases its own loading surface and resources without hiding a successor's loading or error state. Active-room intent follows the existing attached transport or detail Send-to-room staging/suggestion policy; a still never replaces room media independently. Hover and keyboard focus jointly reveal actions, and any positive resume position uses the Resume label.

Use actual synthetic native controls and in-memory API responses to prove distinct play/detail action, availability, keyboard action, scope/default resume, narrow bounds and stale queued callback behavior. No real accounts/media, installed app or setup. Pipeline owns Beta stamping/release; no Stable promotion.
