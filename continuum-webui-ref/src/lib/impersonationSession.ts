const STORED_IMPERSONATION_ADMIN_SESSION_KEY = "impersonation_admin_session";

export interface StoredImpersonationAdminSession {
  accessToken: string;
  refreshToken: string;
  returnPath: string;
}

export function saveStoredImpersonationAdminSession(
  session: StoredImpersonationAdminSession,
): void {
  try {
    localStorage.setItem(STORED_IMPERSONATION_ADMIN_SESSION_KEY, JSON.stringify(session));
  } catch {
    // localStorage is unavailable.
  }
}

export function loadStoredImpersonationAdminSession(): StoredImpersonationAdminSession | null {
  try {
    const rawSession = localStorage.getItem(STORED_IMPERSONATION_ADMIN_SESSION_KEY);
    if (!rawSession) {
      return null;
    }

    const parsed = JSON.parse(rawSession) as Partial<StoredImpersonationAdminSession>;
    if (
      typeof parsed.accessToken !== "string" ||
      typeof parsed.refreshToken !== "string" ||
      typeof parsed.returnPath !== "string"
    ) {
      return null;
    }

    return {
      accessToken: parsed.accessToken,
      refreshToken: parsed.refreshToken,
      returnPath: parsed.returnPath,
    };
  } catch {
    return null;
  }
}

export function clearStoredImpersonationAdminSession(): void {
  try {
    localStorage.removeItem(STORED_IMPERSONATION_ADMIN_SESSION_KEY);
  } catch {
    // localStorage is unavailable.
  }
}
