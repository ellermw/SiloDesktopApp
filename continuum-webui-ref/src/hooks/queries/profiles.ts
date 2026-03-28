import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { Profile, CreateProfileRequest } from "@/api/types";
import { profileKeys } from "./keys";
import { toast } from "sonner";

export function useProfiles() {
  return useQuery({
    queryKey: profileKeys.list(),
    queryFn: () => api<{ profiles: Profile[] }>("/profiles").then((d) => d.profiles ?? []),
  });
}

export function useCreateProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateProfileRequest) =>
      api("/profiles", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Profile created");
      queryClient.invalidateQueries({ queryKey: profileKeys.list() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save profile");
    },
  });
}

export function useUpdateProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: Partial<CreateProfileRequest> }) =>
      api<Profile>(`/profiles/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: (updatedProfile) => {
      queryClient.setQueryData<Profile[]>(profileKeys.list(), (profiles) => {
        if (!profiles || profiles.length === 0) {
          return [updatedProfile];
        }
        return profiles.map((profile) =>
          profile.id === updatedProfile.id ? updatedProfile : profile,
        );
      });
      toast.success("Profile updated");
      queryClient.invalidateQueries({ queryKey: profileKeys.list() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save profile");
    },
  });
}

export function useDeleteProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api(`/profiles/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success("Profile deleted");
      queryClient.invalidateQueries({ queryKey: profileKeys.list() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to delete");
    },
  });
}
