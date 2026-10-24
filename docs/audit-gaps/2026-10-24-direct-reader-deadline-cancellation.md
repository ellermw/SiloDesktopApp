# Direct-reader cancellation and adaptive observation — October24

During exact-payload native verification of the new buffering controller, one controlled low-throughput run failed to adjust quality within its test window. This was investigated instead of accepting an earlier passing run.

## Observed terminal reader failure

In `verified-connection-transport`, the fourth slow HTTP response failed at exactly its five-second progress deadline with ObjectDisposedException. The recorded failure preceded caller disposal and native route recovery. The existing reader retries OperationCanceledException/HTTP/I/O errors, but classified this disposed-stream exception as terminal, forcing a native transport reload and resetting the controller's adoption window. Another instrumented run of the unchanged flow returned the usual retryable TaskCanceledException at the same deadlines and passed; the exception representation was intermittent.

Controlled handler/stream regressions then reproduce the distinction deterministically: deliver useful bytes, wait for the real read-token deadline, and throw ObjectDisposedException from the canceled read. The pre-fix reader fails the resumed byte-equality and lifetime-cancellation cases. An arbitrary disposed-stream exception without a canceled deadline is already terminal and must stay so.

The narrow repair surrounds only the body ReadAsync. ObjectDisposedException is converted to OperationCanceledException **only when that read's linked deadline is actually canceled**, retaining the original exception as its inner cause. Existing outer cancellation/retry policy then retries an internal progress deadline from the current byte offset, or stops on lifetime cancellation without issuing more HTTP requests. IsRetryable is not broadened to arbitrary disposal.

Red/green evidence: `.codex-tmp/oct24-implementation/direct-deadline-*.log`; original/first-candidate native evidence remains preserved. This new correction advances the local candidate to304 (the original15 audit groups, two Home/navigation groups, and this reader repair).

## Late successful read across a deadline

Repeating the first304 candidate captured an additional exact stack: `NetworkStream.ReadAsync → HttpConnection.ReadAsync → ContentLengthReadStream.ReadAsync → DirectHttpReader.ReadAsync`. A read had successfully returned useful bytes after5018ms, leaving zero progress budget. The following read reused that response with a new one-millisecond token that was not yet canceled, so the earlier narrowly gated catch correctly did not apply to its already-disposed stream.

Two further controlled regressions reproduce this boundary, including a late chunk large enough to reset useful-progress counters. The final repair latches a response's expired deadline/budget before those counters can reset. A pre-body check after header acquisition routes the next read through existing retry/pool rotation at the exact unread byte; all late useful bytes are delivered first. Closing/replacing the response clears the flag. Resumed offsets8192 and86016 are verified byte-for-byte, while unrelated disposal remains terminal and lifetime cancellation sends no new request.

The corrected final304 payload passes both fresh native repeats and the original mixed guard sequence. The expired-window guard executes three times in actual native I/O without terminal disposal, false healthy outage, lost bytes or unintended route recovery. This distinguishes the final installer from the preserved first304 candidate.

## Separate observation defect

The instrumented fresh-flow trace also exposed an adapter mistake in the new feature: a historical nonterminal read retry (`_failures>0`) was treated as a current failed transport during subsequent one-second gaps. It repeatedly marked outage/extended the six-second recovery grace even though actual no-byte time was only1.1–1.6 seconds, below the four-second direct outage threshold. That prevented slow-but-live media from accumulating stalls. The observation must use terminal request failure or the actual elapsed no-byte threshold; the historical failure counter remains diagnostic data.

Evidence: `connection-repeatable-1/connection-observations.jsonl` records repeated false outage at25.3/30.0/36.1/41.1/47.1 seconds without an adoption change. The implementation and native replay are verified before the304 installer is delivered.

These are reproducible client defects. They do not establish that every historical live buffering report has the same cause, and no physical Internet/server fault is inferred from the synthetic loopback test.

Final verification:2,120 main tests,32 native scenario runs and24 packaged-service checks pass. The repeated native transport default/default/original cases all pass on final DLL `0A3894D156CFCF841C76B06BDCD2D8AA8431D1154AB9588BADEC98FE5257B29E`. See the full `direct-reader-deadline-audit.md` and `transport-304-verified-analysis.json` under the implementation evidence directory and the304 release/verification receipt for exact scope and preserved failures.
