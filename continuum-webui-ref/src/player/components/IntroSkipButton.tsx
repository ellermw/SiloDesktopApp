interface IntroSkipButtonProps {
  onSkip: () => void;
}

/**
 * "Skip Intro" button visible during the intro time range.
 */
export function IntroSkipButton({ onSkip }: IntroSkipButtonProps) {
  return (
    <button
      onClick={onSkip}
      type="button"
      className="absolute right-6 bottom-24 z-50 rounded border border-white/40 bg-black/70 px-6 py-2 text-sm font-medium text-white transition-colors hover:bg-white/20"
    >
      Skip Intro
    </button>
  );
}
