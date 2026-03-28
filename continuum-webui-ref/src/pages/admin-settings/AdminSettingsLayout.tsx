import { useState } from "react";
import {
  Settings2,
  PlayCircle,
  ScanSearch,
  Gauge,
  Puzzle,
  MonitorPlay,
  Database,
  HardDrive,
  ScrollText,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";

import GeneralSettings from "./GeneralSettings";
import PlaybackSettings from "./PlaybackSettings";
import ScannerSettings from "./ScannerSettings";
import RateLimitSettings from "./RateLimitSettings";
import IntegrationsSettings from "./IntegrationsSettings";
import JellyfinSettings from "./JellyfinSettings";
import DatabaseSettings from "./DatabaseSettings";
import StorageSettings from "./StorageSettings";
import LogRetentionSettings from "./LogRetentionSettings";

interface SettingsNav {
  id: string;
  label: string;
  icon: LucideIcon;
  component: React.ComponentType;
}

const SETTINGS_NAV: SettingsNav[] = [
  { id: "general", label: "General", icon: Settings2, component: GeneralSettings },
  { id: "playback", label: "Playback", icon: PlayCircle, component: PlaybackSettings },
  { id: "scanner", label: "Scanner & Matcher", icon: ScanSearch, component: ScannerSettings },
  { id: "rate-limiting", label: "Rate Limiting", icon: Gauge, component: RateLimitSettings },
  { id: "integrations", label: "Integrations", icon: Puzzle, component: IntegrationsSettings },
  { id: "jellyfin", label: "Jellyfin Compat", icon: MonitorPlay, component: JellyfinSettings },
  { id: "database", label: "Database", icon: Database, component: DatabaseSettings },
  { id: "storage", label: "Storage", icon: HardDrive, component: StorageSettings },
  { id: "log-retention", label: "Log Retention", icon: ScrollText, component: LogRetentionSettings },
];

export default function AdminSettingsLayout() {
  const [activeId, setActiveId] = useState("general");
  const active = SETTINGS_NAV.find((n) => n.id === activeId) ?? SETTINGS_NAV[0]!;
  const ActiveComponent = active.component;

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Settings</h1>
        <p className="page-subtitle text-sm sm:text-base">
          Configure server-wide settings. Most changes require a server restart to take effect.
        </p>
      </div>

      <div className="surface-panel flex min-h-[500px] flex-col overflow-hidden rounded-[1.8rem] border-0 lg:flex-row">
        {/* Sub-nav sidebar */}
        <nav className="border-border overflow-x-auto border-b px-2 py-3 lg:w-56 lg:flex-shrink-0 lg:border-r lg:border-b-0 lg:px-0">
          <div className="flex gap-1 lg:block lg:space-y-1">
          {SETTINGS_NAV.map((item) => {
            const isActive = item.id === activeId;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => setActiveId(item.id)}
                className={`relative flex min-w-max items-center gap-2.5 rounded-xl px-4 py-2.5 text-left text-[13px] font-medium transition-colors lg:w-full ${
                  isActive
                    ? "text-foreground bg-accent"
                    : "text-muted-foreground hover:text-foreground hover:bg-accent/50"
                }`}
              >
                {isActive && (
                  <span
                    className="absolute top-auto bottom-0 left-1/2 h-[3px] w-8 -translate-x-1/2 rounded-t-sm lg:top-1/2 lg:bottom-auto lg:left-0 lg:h-[18px] lg:w-[3px] lg:-translate-x-0 lg:-translate-y-1/2 lg:rounded-r-sm lg:rounded-t-none"
                    style={{ background: "var(--primary)" }}
                  />
                )}
                <item.icon className="h-4 w-4 flex-shrink-0" />
                <span>{item.label}</span>
              </button>
            );
          })}
          </div>
        </nav>

        {/* Content area */}
        <div className="flex-1 overflow-y-auto p-4 sm:p-6">
          <ActiveComponent />
        </div>
      </div>
    </div>
  );
}
