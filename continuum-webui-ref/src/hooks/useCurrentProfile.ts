import { useAuth } from "@/hooks/useAuth";
import { useProfiles } from "@/hooks/queries/profiles";
import { storage } from "@/utils/storage";
import type { Profile } from "@/api/types";

export function resolveCurrentProfile(
  profiles: Profile[],
  cachedProfile: Profile | null,
  selectedProfileId?: string | null,
): Profile | null {
  const activeProfileId = selectedProfileId ?? cachedProfile?.id ?? null;
  if (activeProfileId) {
    const freshProfile = profiles.find((profile) => profile.id === activeProfileId);
    if (freshProfile) {
      return freshProfile;
    }
  }
  return cachedProfile;
}

export function useCurrentProfile() {
  const { profile: cachedProfile } = useAuth();
  const profilesQuery = useProfiles();
  const selectedProfileId = storage.get(storage.KEYS.PROFILE_ID);
  const profile = resolveCurrentProfile(
    profilesQuery.data ?? [],
    cachedProfile,
    selectedProfileId,
  );

  return {
    ...profilesQuery,
    profile,
  };
}
