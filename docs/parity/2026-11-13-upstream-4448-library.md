# November 13 - Mounted catalog updates during scans

Independent branchcodex/library-scan-4448-nov13, Beta6f9f87f7,
version1.2.1017-beta.1 /1.2.1016.1. Official pin4448e401;3580eda1 motivated
checking scan-versus-letter ownership. Canonical and legacy catalog events now
queue a library-scoped, fixed two-second batch. One read and one trailing dirty
batch bound event storms. Content refreshes preserve authorized rows, viewport,
filters and native slots. Transport confirmation still quarantines old data.

A pending letter seek and both native scroll boundaries retain their ownership.
A letter click during a healthy content refresh hands the read back without
clearing total/rail/held rows. Failure or snapshot churn returns joined foreground
ranges to ordinary loading while preserving accepted rows. Healthy background
failure does not leave a sticky error that forces reconnect remount. Access loss
still cancels actual query work and immediately hides bound cards. Actual detach
retires work; late Unloaded after reattachment cannot retire the new visit. Native
clamp targets retain their accepted offset so later extent growth releases holds.

Verification: final138 related unit cases passed, zero failed/skipped. Initial
6115 full units passed before review corrections; that count is historical and
is not claimed for this revised source. Release publish and native host build
passed. Native library-scan-refresh passed seven scenarios (held landing, newer
jump, access retirement, cached return, target failure, refresh failure, extent
growth); library-reconnect also passed. New units reproduced refresh-before-jump
and failed/churned handback issues before correction. Independent review found
one P1 and three P2; their corrections have the targeted evidence above.

The first native revisit assertion held a foreground-restoration read rather
than the retained scan batch. It now waits on a fresh-snapshot gate and confirms
the scan owner before testing old-finally ownership. Original failure traces are
retained. No live scan, expanded-playback throughput or exhaustive visual-parity
certification. Background batching latency remains the deliberate two seconds.

Evidence:D:/SiloPlayer-build/4448-library-*.log, results/4448-library-review-final.log,
results/library-scan-refresh and results/library-reconnect; final delivery receipt
D:/SiloPlayer-build/4448-library-delivery-receipt.json. Same bounded output/publish/
native payload reused; no installer or installed-player change. Cleanup was not
retried after the previous policy block; recovered0GiB on either drive.
Product DLL SHA256: 39940BC6C301F120F084FE712DB551771B31BC941C55A55B1D31EDFDFEDF4D6B
