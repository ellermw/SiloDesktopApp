import type { ReactNode } from "react";
import { createContext, useContext, useEffect, useRef, useState } from "react";
import { api, getAccessToken } from "@/api/client";
import type {
  AdminSession,
  AdminSessionErrorMessage,
  AdminSessionSnapshotMessage,
  AdminSessionStreamMessage,
} from "@/api/types";

type ConnectionState = "connecting" | "live" | "disconnected";

interface AdminSessionStreamContextValue {
  sessions: AdminSession[];
  isLoading: boolean;
  connectionState: ConnectionState;
  error?: string;
  refresh: () => Promise<void>;
}

const defaultContextValue: AdminSessionStreamContextValue = {
  sessions: [],
  isLoading: false,
  connectionState: "disconnected",
  error: undefined,
  refresh: async () => {},
};

const AdminSessionStreamContext =
  createContext<AdminSessionStreamContextValue>(defaultContextValue);

export function buildAdminSessionStreamUrl(
  token: string | null,
  location: Pick<Location, "protocol" | "host">,
) {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const search = new URLSearchParams();
  if (token) {
    search.set("token", token);
  }
  return `${protocol}//${location.host}/api/v1/admin/sessions/ws${search.toString() ? `?${search.toString()}` : ""}`;
}

function parseAdminSessionStreamMessage(value: unknown): AdminSessionStreamMessage | null {
  if (typeof value !== "string") {
    return null;
  }

  try {
    const parsed = JSON.parse(value) as AdminSessionSnapshotMessage | AdminSessionErrorMessage;
    if (!parsed || typeof parsed.type !== "string") {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function AdminSessionStreamProvider({ children }: { children: ReactNode }) {
  const [sessions, setSessions] = useState<AdminSession[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [connectionState, setConnectionState] = useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();
  const reconnectTimerRef = useRef<number | undefined>(undefined);
  const closingRef = useRef(false);

  async function refresh() {
    try {
      const next = await api<AdminSession[]>("/admin/sessions");
      setSessions(next ?? []);
      setError(undefined);
    } catch {
      setError("Unable to load active sessions.");
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    closingRef.current = false;
    let ws: WebSocket | null = null;

    const clearReconnect = () => {
      if (reconnectTimerRef.current !== undefined) {
        window.clearTimeout(reconnectTimerRef.current);
        reconnectTimerRef.current = undefined;
      }
    };

    const scheduleReconnect = () => {
      if (closingRef.current || reconnectTimerRef.current !== undefined) {
        return;
      }
      reconnectTimerRef.current = window.setTimeout(() => {
        reconnectTimerRef.current = undefined;
        connect();
      }, 1000);
    };

    const connect = () => {
      setConnectionState("connecting");
      const url = buildAdminSessionStreamUrl(getAccessToken(), window.location);
      try {
        ws = new WebSocket(url);
      } catch {
        setConnectionState("disconnected");
        setError("Unable to open session stream.");
        scheduleReconnect();
        return;
      }

      ws.onopen = () => {
        setConnectionState("live");
        setError(undefined);
      };

      ws.onmessage = (event) => {
        const message = parseAdminSessionStreamMessage(event.data);
        if (!message) {
          return;
        }

        if (message.type === "snapshot") {
          setSessions(message.entries);
          setIsLoading(false);
          setError(undefined);
          return;
        }

        setError(message.message);
      };

      ws.onerror = () => {
        setConnectionState("disconnected");
        setError("Session stream disconnected.");
      };

      ws.onclose = () => {
        setConnectionState("disconnected");
        if (!closingRef.current) {
          scheduleReconnect();
        }
      };
    };

    void refresh().catch(() => {
      setIsLoading(false);
      setError("Unable to load active sessions.");
    });
    connect();

    return () => {
      closingRef.current = true;
      clearReconnect();
      if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
        ws.close();
      }
    };
  }, []);

  return (
    <AdminSessionStreamContext.Provider
      value={{ sessions, isLoading, connectionState, error, refresh }}
    >
      {children}
    </AdminSessionStreamContext.Provider>
  );
}

export function useAdminSessionStream() {
  return useContext(AdminSessionStreamContext);
}
