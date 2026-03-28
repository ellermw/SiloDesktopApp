import { Info, Maximize, Minimize, Pause, Play } from "lucide-react";
import { SeekBar, formatTime } from "./SeekBar";
import { VolumeControl } from "./VolumeControl";
import { QualityMenu } from "./QualityMenu";
import { SubtitleMenu } from "./SubtitleMenu";
import { AudioTrackMenu } from "./AudioTrackMenu";
import type { PlayerAudioTrack, PlayerSubtitleInfo, QualityOption } from "../types";
import type { VersionInfo } from "./QualityMenu";
import type { PlayerConfig } from "../context/PlayerConfigContext";

interface PlayerControlsProps {
  // Visibility
  visible: boolean;
  // Video state
  playing: boolean;
  currentTime: number;
  duration: number;
  buffered: TimeRanges | null;
  volume: number;
  muted: boolean;
  isFullscreen: boolean;
  // Subtitles
  subtitleTracks: PlayerSubtitleInfo[];
  activeSubtitleIndex: number | null;
  onSubtitleSelect: (index: number | null) => void;
  mediaFileId?: number;
  playerConfig?: PlayerConfig;
  onRefreshSubtitles?: () => void;
  // Audio
  audioTracks: PlayerAudioTrack[];
  activeAudioIndex: number;
  onAudioSelect?: (index: number, currentPosition: number) => void;
  // Quality
  qualityOptions: QualityOption[];
  activeQualityId: string;
  isTranscoding: boolean;
  qualityError: string | null;
  onQualitySelect: (id: string) => void;
  // Version switching
  versions?: VersionInfo[];
  onSwitchVersion?: (fileId: number) => void;
  // Playback info
  showPlaybackInfo: boolean;
  onTogglePlaybackInfo: () => void;
  // Callbacks
  onPlayPause: () => void;
  onSeek: (seconds: number) => void;
  onVolumeChange: (volume: number) => void;
  onMutedChange: (muted: boolean) => void;
  onFullscreenToggle: () => void;
}

export function PlayerControls({
  visible,
  playing,
  currentTime,
  duration,
  buffered,
  volume,
  muted,
  isFullscreen,
  subtitleTracks,
  activeSubtitleIndex,
  onSubtitleSelect,
  mediaFileId,
  playerConfig,
  onRefreshSubtitles,
  audioTracks,
  activeAudioIndex,
  onAudioSelect,
  qualityOptions,
  activeQualityId,
  isTranscoding,
  qualityError,
  onQualitySelect,
  versions,
  onSwitchVersion,
  showPlaybackInfo,
  onTogglePlaybackInfo,
  onPlayPause,
  onSeek,
  onVolumeChange,
  onMutedChange,
  onFullscreenToggle,
}: PlayerControlsProps) {
  return (
    <div
      className={`absolute inset-0 z-10 flex flex-col justify-end transition-opacity duration-300 ${
        visible ? "opacity-100" : "pointer-events-none opacity-0"
      }`}
      onClick={onPlayPause}
    >
      {/* Pause overlay */}
      {!playing && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <div className="flex h-16 w-16 items-center justify-center rounded-full bg-black/50">
            <Play className="ml-1 h-8 w-8 text-white" />
          </div>
        </div>
      )}

      {/* Gradient scrim */}
      <div className="pointer-events-none absolute inset-x-0 bottom-0 h-32 bg-gradient-to-t from-black/80 to-transparent" />

      {/* Controls */}
      <div
        className="relative z-10 flex flex-col gap-1 px-2 pb-3"
        onClick={(e) => e.stopPropagation()}
      >
        <SeekBar
          currentTime={currentTime}
          duration={duration}
          buffered={buffered}
          onSeek={onSeek}
        />

        <div className="flex items-center gap-1">
          {/* Play/Pause */}
          <button
            type="button"
            className="flex h-8 w-8 items-center justify-center text-white hover:text-white/80"
            onClick={onPlayPause}
            aria-label={playing ? "Pause" : "Play"}
          >
            {playing ? <Pause className="h-5 w-5" /> : <Play className="h-5 w-5" />}
          </button>

          {/* Volume */}
          <VolumeControl
            volume={volume}
            muted={muted}
            onVolumeChange={onVolumeChange}
            onMutedChange={onMutedChange}
          />

          {/* Time */}
          <div className="ml-2 text-xs text-white/80 tabular-nums">
            {formatTime(currentTime)} / {formatTime(duration)}
          </div>

          {/* Spacer */}
          <div className="flex-1" />

          {/* Playback Info */}
          <button
            type="button"
            className={`flex h-8 w-8 items-center justify-center hover:text-white/80 ${
              showPlaybackInfo ? "text-blue-400" : "text-white"
            }`}
            onClick={onTogglePlaybackInfo}
            aria-label="Playback info"
          >
            <Info className="h-5 w-5" />
          </button>

          {/* Audio */}
          {onAudioSelect && (
            <AudioTrackMenu
              tracks={audioTracks}
              activeIndex={activeAudioIndex}
              onSelect={onAudioSelect}
              currentPosition={currentTime}
            />
          )}

          {/* Subtitles */}
          <SubtitleMenu
            tracks={subtitleTracks}
            activeIndex={activeSubtitleIndex}
            onSelect={onSubtitleSelect}
            mediaFileId={mediaFileId}
            playerConfig={playerConfig}
            onRefreshSubtitles={onRefreshSubtitles}
          />

          {/* Quality */}
          <QualityMenu
            options={qualityOptions}
            activeId={activeQualityId}
            isTranscoding={isTranscoding}
            error={qualityError}
            onSelect={onQualitySelect}
            versions={versions}
            onSwitchVersion={onSwitchVersion}
          />

          {/* Fullscreen */}
          <button
            type="button"
            className="flex h-8 w-8 items-center justify-center text-white hover:text-white/80"
            onClick={onFullscreenToggle}
            aria-label={isFullscreen ? "Exit fullscreen" : "Fullscreen"}
          >
            {isFullscreen ? <Minimize className="h-5 w-5" /> : <Maximize className="h-5 w-5" />}
          </button>
        </div>
      </div>
    </div>
  );
}
