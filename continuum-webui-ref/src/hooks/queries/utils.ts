import { api, ApiClientError } from "@/api/client";

/**
 * Check if a resource exists by its API path.
 * Returns true on 204 (exists), false on 404 (not found).
 * Re-throws other errors for TanStack Query to handle.
 */
export async function checkBooleanStatus(path: string): Promise<boolean> {
  try {
    await api<undefined>(path);
    return true;
  } catch (err) {
    if (err instanceof ApiClientError && err.status === 404) {
      return false;
    }
    throw err;
  }
}
