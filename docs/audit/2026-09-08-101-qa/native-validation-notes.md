# Independent challenge of N01, N02, N03 and N05

Source-only verification by client_backend. Read the four report sections and the relevant implementation/caller/event-handler bodies; searched all shipping source for subtitle-window callers and overlay activation. No application interaction, source changes, tests, network requests or server changes. This is a targeted challenge, not an additional full-file coverage claim.

## N01: upheld, high confidence

The attempted disproof was that UI dispatch, cancellation and Core's replan semaphore might serialize the entire operation. They do not. OSC dispatch invokes SwitchAudioTrackAsync and SwitchQualityTierAsync without awaiting them (PlayerService.cs:5362 and :5381). CancelAndDrainTransportRestartsAsync (:597–601) acquires and immediately releases only the seek-restart gate. PlaybackManager.ReplanAsync releases _replanGuard (:454) before its caller prepares/loads a transport. The v3 caller continuations run with ConfigureAwait(false).

A concrete allowed interleaving requires no transport that ignores cancellation: audio replan finishes and its caller captures session/version/URL, then pauses at PlayerService.cs:4134. A new content request replaces the session/proxy and cancels the old playback token. The old caller resumes PreparePlaybackTransportAsync. Its protocol-v3 branch (:3151–3158) performs synchronous preparation without checking ct. PrepareDirectStreamForMpv (:3393–3394) and PrepareHlsStreamForMpv (:3410–3411) dispose the globally active proxies. Only afterward does the audio caller check ct and manager/session identity (:4140–4142). The guard prevents publication but cannot undo disposal of the newer transport. Quality has the same ordering at :5495–5503. Existing switching booleans suppress selected events; they are not admission control for these mutations.

Keep N01's candidate-resource ownership recommendation. The exact race is source-established, not runtime reproduced.

## N02: upheld with material qualification

The narrow gap is an exception after manager.ReplanAudioAsync/ReplanQualityAsync accepts and adopts a new server plan, before local replacement is successfully established. Examples include missing version/URL, local proxy construction/bind failure, or an exception in synchronous BeginMpvLoad setup. Audio catch (:4154–4162) and quality catch (:5518–5528) reset flags and show notice; they do not track accepted-plan state or explicitly recover. Legacy audio has serverTransportChanged and recovery at :4302 onward. BeginMpvLoad's catch (:1971–1976) cancels its watchdog and rethrows; a failure after its Pause (:1966) can also leave native playback paused, which ordinary stall observation does not repair as a stall.

Do **not** describe all v3 replacement-load failures as uncovered: successful BeginMpvLoad arms a 15-second direct / 30-second other load watchdog (:1979–1987). MonitorFileLoadAsync (:2051 onward) can recover, and HandleMpvPlaybackError (:3974 onward) retries native load errors and reaches terminal state after repeated failure. After the v3 catch clears flags, the normal stall detector (:2220 onward) may also eventually recover an unpaused frozen stream. These mechanisms mitigate impact but do not establish correct accepted-plan commit/failure handling. Suggested wording: “Local setup failure after an accepted v3 replan lacks explicit rollback/recovery; later native error/stall handling may recover some cases.”

No fresh server contract verification was performed in this challenge. A server transition may retire the old output; do not imply every replan immediately invalidates every old URL.

## N03: upheld, high confidence in the missing guard

MpvPlayer.cs:939–960 excludes STOP/QUIT and routes explicit END_FILE ERROR to PlaybackError, which PlayerService handles through recovery. That rules out the broader claim that every end-file event advances a book. However EOF reason triggers PlaybackEnded, and the separate eof-reached property triggers EofReached (:1019–1024). Both are wired directly to HandleMpvEndSignal at PlayerService.cs:3930–3934.

After switching/closing/idle suppression, the audiobook branch (:3607) advances parts (:3621–3627) or sets Position to full book duration (:3630), before the video's IsAtMediaEnd validation (:3644). ContinueAudiobookPartAsync (:3728–3731) first force-reports the next part's absolute start, so a premature EOF can skip unheard audio and overwrite progress. Final-part reporting (:1307–1330) sends aggregate position=duration with force_overwrite=true when total duration is known. An unknown active part also falls through to that completion branch; total duration <=0 suppresses the network progress call. Keep the finding phrased as premature EOF, not generic playback errors. No truncated-audiobook stream was reproduced here.

## N05: upheld, with exact affected scope

The only production caller of TickSubtitleWindows is PlayerOverlay.xaml.cs:449. Its 250ms timer starts only in Activate (:133–136), while Deactivate stops it (:160–164). MainWindow never calls PlayerOverlayControl.Activate and deactivates it in every player state (:2178, :2195, :2214, :2226). Constructor does not start the timer. There is no alternate caller from native OSC, playback position events, loaded-media initialization, or active audiobook/mini-player controls.

Affected subtitles are embedded text sidecars on non-direct transports. DirectProgressive selects native embedded sid (:4400 and :4633–4638), external/downloaded subtitles use full URLs (:4652–4668), PGS uses a full sidecar (:4693–4702), and unsupported bitmap subtitles use burn-in (:4627–4630). The text-sidecar path appends position and 600-second duration at :4705–4707. Its retained coverage never slides in normal playback. Continuous playback beyond the window is the clearest trigger. An HLS local seek with CanSeekAnywhere (:413–426) also performs no subtitle maintenance; transport-restart seeks may reinitialize subtitle state and should not all be described as equally affected. Selecting the same cached track at :4679–4683 only reselects sid, rather than fetching a fresh window.

The command-result subfinding also holds: MpvPlayer.AddSubtitle (:779) discards Command's bool result (:826–859), and subsequent sid queries populate the mapping even on failure. The Error event is logging-only at PlayerService.cs:3940–3941, so it does not convert sub-add failure into a failed selection result. AppendPositionDuration (:4761) uses current-culture interpolation, confirming the decimal-comma wire risk. No subtitle playback or locale scenario was executed.

Marker subsection was not re-challenged beyond confirming SendMarkersToOsc reads WatchDetail marker fields (:5112–5125); parent full-file review remains its evidence owner.
