import { useState } from "react";
import { Star } from "lucide-react";

interface StarRatingProps {
  value: number | null;
  onChange: (rating: number | null) => void;
  size?: number;
}

const STAR_COUNT = 5;

export default function StarRating({ value, onChange, size = 20 }: StarRatingProps) {
  const [hoverValue, setHoverValue] = useState<number | null>(null);

  const displayValue = hoverValue ?? value;

  function handleMouseEnter(star: number) {
    setHoverValue(star);
  }

  function handleMouseLeave() {
    setHoverValue(null);
  }

  function handleClick(star: number) {
    if (star === value) {
      onChange(null);
    } else {
      onChange(star);
    }
  }

  return (
    <div
      role="group"
      aria-label="Star rating"
      className="glass-subtle flex items-center gap-0.5 rounded-full px-2.5 py-2"
      onMouseLeave={handleMouseLeave}
    >
      {Array.from({ length: STAR_COUNT }, (_, i) => {
        const star = i + 1;
        const filled = displayValue !== null && star <= displayValue;
        return (
          <button
            key={star}
            type="button"
            role="radio"
            aria-label={`${star} star${star !== 1 ? "s" : ""}`}
            aria-checked={value === star}
            className={`cursor-pointer border-none bg-transparent p-0.5 leading-none transition-all duration-150 hover:scale-110 ${filled ? "text-yellow-400" : "text-muted-foreground/50"}`}
            onMouseEnter={() => handleMouseEnter(star)}
            onClick={() => handleClick(star)}
          >
            <Star size={size} fill={filled ? "currentColor" : "none"} strokeWidth={1.5} />
          </button>
        );
      })}
    </div>
  );
}
