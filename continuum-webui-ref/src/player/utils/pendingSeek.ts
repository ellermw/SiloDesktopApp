export interface PendingSeekResolution {
  currentTime: number;
  pendingSeekTime: number | null;
}

const DEFAULT_SEEK_SETTLE_TOLERANCE_SECONDS = 1;

export function resolvePendingSeekTime(
  actualTime: number,
  pendingSeekTime: number | null,
  toleranceSeconds = DEFAULT_SEEK_SETTLE_TOLERANCE_SECONDS,
): PendingSeekResolution {
  if (pendingSeekTime == null) {
    return { currentTime: actualTime, pendingSeekTime: null };
  }

  if (Math.abs(actualTime - pendingSeekTime) <= toleranceSeconds) {
    return { currentTime: actualTime, pendingSeekTime: null };
  }

  return { currentTime: pendingSeekTime, pendingSeekTime };
}
