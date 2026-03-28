import { useState, useEffect, useCallback } from "react";
import { Link } from "react-router";
import { Play, ChevronLeft, ChevronRight } from "lucide-react";
import { decodeThumbhash } from "@/lib/thumbhash";
import { useAmbientColor } from "@/hooks/useAmbientColor";
import type { SectionItem } from "@/api/types";

interface HeroBannerProps {
  items: SectionItem[];
  maxSlides?: number;
}

export default function HeroBanner({ items, maxSlides = 10 }: HeroBannerProps) {
  const slides = items.slice(0, maxSlides);
  const [activeIndex, setActiveIndex] = useState(0);
  const [loaded, setLoaded] = useState<Record<number, boolean>>({});

  const next = useCallback(() => {
    setActiveIndex((i) => (i + 1) % slides.length);
  }, [slides.length]);

  const prev = useCallback(() => {
    setActiveIndex((i) => (i - 1 + slides.length) % slides.length);
  }, [slides.length]);

  useEffect(() => {
    if (slides.length <= 1) return;
    const timer = setInterval(next, 8000);
    return () => clearInterval(timer);
  }, [next, slides.length]);

  const current = slides[activeIndex] ?? slides[0];
  useAmbientColor(current?.backdrop_thumbhash);
  if (slides.length === 0) return null;
  if (!current) return null;

  return (
    <section className="group relative mb-10 min-h-[72dvh] w-full overflow-hidden border-b border-border/60 lg:min-h-[78dvh]">
      {/* Backdrop layers – all stacked, crossfade via opacity */}
      {slides.map((slide, i) => {
        const thumbhash = slide.backdrop_thumbhash
          ? decodeThumbhash(slide.backdrop_thumbhash)
          : "";
        const isActive = i === activeIndex;
        return (
          <div
            key={slide.content_id ?? i}
            className={`absolute inset-0 bg-muted transition-opacity duration-1000 ease-in-out ${isActive ? "opacity-100" : "opacity-0"}`}
            style={
              thumbhash
                ? {
                    backgroundImage: `url(${thumbhash})`,
                    backgroundSize: "cover",
                    backgroundPosition: "center 20%",
                  }
                : undefined
            }
          >
            {slide.backdrop_url && (
              <img
                src={slide.backdrop_url}
                alt=""
                className={`h-full w-full object-cover object-[center_20%] transition-opacity duration-[--duration-slow] will-change-transform ${loaded[i] ? "opacity-100" : "opacity-0"}`}
                style={{
                  animation: `var(--animate-ken-burns-${i % 2 === 0 ? "a" : "b"})`,
                  filter: `brightness(var(--hero-backdrop-brightness, 0.78)) saturate(var(--hero-backdrop-saturate, 0.95))`,
                }}
                onLoad={() => setLoaded((prev) => ({ ...prev, [i]: true }))}
              />
            )}
          </div>
        );
      })}

      {/* Gradient overlay */}
      <div className="hero-gradient" />
      <div className="ambient-glow" />
      <div className="hero-gradient-left" />
      <div className="hero-vignette" />

      {/* Content */}
      <div className="relative z-10 flex min-h-[72dvh] items-end px-4 pb-10 sm:px-6 sm:pb-12 lg:min-h-[78dvh] lg:px-10 lg:pb-16 xl:px-12">
        <div className="w-full max-w-[1380px]">
          <div className="max-w-3xl">
            <h1
              className="mb-4 max-w-4xl font-display text-4xl font-extrabold tracking-[-0.04em] text-balance sm:text-5xl lg:text-7xl"
              style={{ textShadow: "var(--hero-text-shadow, none)" }}
            >
              {current.title}
            </h1>
            <div className="mb-5 flex flex-wrap items-center gap-2.5 text-sm text-foreground/78">
              {current.year > 0 && <span className="metadata-badge">{current.year}</span>}
              {current.rating_imdb != null && (
                <span className="metadata-badge">IMDb {current.rating_imdb.toFixed(1)}</span>
              )}
              {current.genres?.slice(0, 3).map((g) => (
                <span key={g} className="metadata-badge">
                  {g}
                </span>
              ))}
            </div>
            {current.overview && (
              <p className="mb-7 max-w-2xl text-sm leading-7 text-foreground/72 sm:text-base">
                {current.overview}
              </p>
            )}
            <div className="flex flex-wrap items-center gap-3">
              <Link
                to={`/item/${current.content_id}`}
                className="pill pill-primary hover:bg-primary/90 transition-colors duration-[--duration-fast]"
              >
                <Play className="h-4 w-4" fill="currentColor" />
                Open details
              </Link>
            </div>
          </div>
        </div>
      </div>

      {/* Navigation arrows */}
      {slides.length > 1 && (
        <>
          <button
            onClick={prev}
            className="glass-subtle absolute top-1/2 left-3 z-20 -translate-y-1/2 rounded-full p-2 opacity-80 transition-opacity duration-[--duration-fast] hover:opacity-100 sm:left-5 lg:opacity-0 lg:group-hover:opacity-100"
            aria-label="Previous slide"
          >
            <ChevronLeft className="h-5 w-5" />
          </button>
          <button
            onClick={next}
            className="glass-subtle absolute top-1/2 right-3 z-20 -translate-y-1/2 rounded-full p-2 opacity-80 transition-opacity duration-[--duration-fast] hover:opacity-100 sm:right-5 lg:opacity-0 lg:group-hover:opacity-100"
            aria-label="Next slide"
          >
            <ChevronRight className="h-5 w-5" />
          </button>

          {/* Dots */}
          <div className="carousel-dots absolute bottom-6 left-1/2 z-20 -translate-x-1/2 lg:bottom-8">
            {slides.map((_, i) => (
              <button
                key={i}
                onClick={() => setActiveIndex(i)}
                className={`dot transition-colors duration-[--duration-fast] ${
                  i === activeIndex ? "dot-active" : ""
                }`}
                aria-label={`Go to slide ${i + 1}`}
              />
            ))}
          </div>
        </>
      )}
    </section>
  );
}
