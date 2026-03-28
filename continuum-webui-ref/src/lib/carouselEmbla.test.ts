import { describe, expect, it } from "vitest";
import { getCarouselEmblaOptions, getCarouselWheelGestureOptions } from "./carouselEmbla";

describe("carouselEmbla", () => {
  it("enables drag-free momentum with trimmed edge scrolling", () => {
    expect(getCarouselEmblaOptions()).toMatchObject({
      align: "start",
      containScroll: "trimSnaps",
      dragFree: true,
    });
  });

  it("forces wheel gestures onto the horizontal axis", () => {
    const target = { nodeName: "DIV" } as unknown as HTMLElement;

    expect(getCarouselWheelGestureOptions(target)).toMatchObject({
      forceWheelAxis: "x",
      target,
    });
  });
});
