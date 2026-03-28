import { useMemo, useCallback } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { SidebarPin, SidebarPins } from "@/api/types";
import { useSetting, useSetSetting } from "./settings";
import { settingsKeys } from "./keys";

const SIDEBAR_PINS_KEY = "sidebar_pins";
const SIDEBAR_PINS_REVISION_KEY = [
  ...settingsKeys.detail(SIDEBAR_PINS_KEY),
  "optimistic-revision",
] as const;

let nextSidebarPinsRevision = 0;

export function parseSidebarPins(value: string | null | undefined): SidebarPins {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return {};
    return parsed as SidebarPins;
  } catch {
    return {};
  }
}

function serializeSidebarPins(pins: SidebarPins): string {
  return JSON.stringify(pins);
}

export function toggleSidebarPins(
  pins: SidebarPins,
  libraryId: number,
  pin: SidebarPin,
): SidebarPins {
  const key = String(libraryId);
  const existing = pins[key] ?? [];
  const idx = existing.findIndex((p) => p.type === pin.type && p.id === pin.id);

  const nextPins = { ...pins };
  if (idx >= 0) {
    const next = existing.filter((_, i) => i !== idx);
    if (next.length === 0) {
      delete nextPins[key];
    } else {
      nextPins[key] = next;
    }
    return nextPins;
  }

  nextPins[key] = [...existing, pin];
  return nextPins;
}

interface SidebarPinsOptimisticMutation {
  previousRaw: string | null;
  previousRevision: number | null;
  optimisticRaw: string;
  revision: number;
}

export function createSidebarPinsOptimisticMutation({
  currentRaw,
  currentRevision,
  libraryId,
  pin,
  revision,
}: {
  currentRaw: string | null | undefined;
  currentRevision: number | null | undefined;
  libraryId: number;
  pin: SidebarPin;
  revision: number;
}): SidebarPinsOptimisticMutation {
  const currentPins = parseSidebarPins(currentRaw);
  const nextPins = toggleSidebarPins(currentPins, libraryId, pin);

  return {
    previousRaw: currentRaw ?? null,
    previousRevision: currentRevision ?? null,
    optimisticRaw: serializeSidebarPins(nextPins),
    revision,
  };
}

export function rollbackSidebarPinsOptimisticMutation({
  currentRevision,
  mutation,
}: {
  currentRevision: number | null | undefined;
  mutation: SidebarPinsOptimisticMutation;
}): { raw: string | null; revision: number | null } | null {
  if ((currentRevision ?? null) !== mutation.revision) {
    return null;
  }

  return {
    raw: mutation.previousRaw,
    revision: mutation.previousRevision,
  };
}

export function useSidebarPins() {
  const { data: raw, isLoading } = useSetting(SIDEBAR_PINS_KEY);
  const pins = useMemo(() => parseSidebarPins(raw), [raw]);
  return { pins, isLoading };
}

export function useToggleSidebarPin() {
  const { data: raw } = useSetting(SIDEBAR_PINS_KEY);
  const queryClient = useQueryClient();
  const setSetting = useSetSetting();

  const isPinned = useCallback(
    (libraryId: number, pinType: SidebarPin["type"], targetId: string): boolean => {
      const cachedRaw =
        queryClient.getQueryData<string | null>(settingsKeys.detail(SIDEBAR_PINS_KEY)) ?? raw;
      const pins = parseSidebarPins(cachedRaw);
      const key = String(libraryId);
      return (pins[key] ?? []).some((p) => p.type === pinType && p.id === targetId);
    },
    [queryClient, raw],
  );

  const togglePin = useCallback(
    (libraryId: number, pin: SidebarPin) => {
      const cachedRaw =
        queryClient.getQueryData<string | null>(settingsKeys.detail(SIDEBAR_PINS_KEY)) ?? raw;
      const cachedRevision =
        queryClient.getQueryData<number | null>(SIDEBAR_PINS_REVISION_KEY) ?? null;
      nextSidebarPinsRevision += 1;
      const mutation = createSidebarPinsOptimisticMutation({
        currentRaw: cachedRaw,
        currentRevision: cachedRevision,
        libraryId,
        pin,
        revision: nextSidebarPinsRevision,
      });

      queryClient.setQueryData(settingsKeys.detail(SIDEBAR_PINS_KEY), mutation.optimisticRaw);
      queryClient.setQueryData(SIDEBAR_PINS_REVISION_KEY, mutation.revision);
      setSetting.mutate(
        { key: SIDEBAR_PINS_KEY, value: mutation.optimisticRaw },
        {
          onError: () => {
            const rollback = rollbackSidebarPinsOptimisticMutation({
              currentRevision: queryClient.getQueryData<number | null>(SIDEBAR_PINS_REVISION_KEY),
              mutation,
            });

            if (!rollback) {
              return;
            }

            queryClient.setQueryData(settingsKeys.detail(SIDEBAR_PINS_KEY), rollback.raw);
            queryClient.setQueryData(SIDEBAR_PINS_REVISION_KEY, rollback.revision);
          },
        },
      );
    },
    [queryClient, raw, setSetting],
  );

  return { togglePin, isPinned };
}
