import { useState } from "react";

interface ImpersonationBannerProps {
  userName: string;
  impersonatorName: string;
  onEnd: () => void | Promise<void>;
}

export default function ImpersonationBanner({
  userName,
  impersonatorName,
  onEnd,
}: ImpersonationBannerProps) {
  const [isEnding, setIsEnding] = useState(false);

  async function handleEnd() {
    setIsEnding(true);
    try {
      await onEnd();
    } finally {
      setIsEnding(false);
    }
  }

  return (
    <div className="border-border bg-muted/40 border-b">
      <div className="flex flex-col gap-2 px-4 py-2 sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:pl-[292px] lg:pr-8">
        <p className="text-sm">
          <span className="font-medium">Impersonating</span>{" "}
          <span className="font-medium">{userName}</span>
          <span className="text-muted-foreground"> as requested by {impersonatorName}</span>
        </p>
        <button
          type="button"
          onClick={() => void handleEnd()}
          disabled={isEnding}
          className="border-border bg-background hover:bg-accent hover:text-accent-foreground focus-visible:border-ring focus-visible:ring-ring/50 inline-flex h-8 shrink-0 items-center justify-center rounded-md border px-3 text-sm font-medium whitespace-nowrap transition-colors duration-150 outline-none focus-visible:ring-[3px] disabled:pointer-events-none disabled:opacity-50"
        >
          End impersonation
        </button>
      </div>
    </div>
  );
}
