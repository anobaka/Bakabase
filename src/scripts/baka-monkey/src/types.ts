import type { ReactNode } from 'react';

export interface ContentInfo {
  id: string | null;
  updateTime: Date | null;
}

export interface ContentStatus {
  isViewed: boolean;
  hasUpdate: boolean;
  viewedAt: Date | null;
  updatedAt: Date | null;
}

// ---------------------------------------------------------------------------
// Action adapters – site-specific logic that shared action components need
// ---------------------------------------------------------------------------

/** Adapter for the "parse post" action (e.g. extract download info via AI). */
export interface ParseTaskAdapter {
  /** PostParserSource enum value identifying this site on the backend. */
  source: number;
  /** PostParseTarget values to request. Defaults to `[1]` (DownloadInfo). */
  targets?: number[];
  /** Extract the canonical post URL from a content element. */
  extractPostUrl: (element: HTMLElement) => string;
}

/** Adapter for the "download" action (e.g. one-click download from exhentai). */
export interface DownloadTaskAdapter {
  /** Extract the downloadable URL from a content element. */
  extractUrl: (element: HTMLElement) => string;
  /** Call the site-specific backend API to create a download task. */
  createTask: (url: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// SiteConfig
// ---------------------------------------------------------------------------

export interface SiteConfig {
  key: string;
  domains: string[];
  extractFilter: (url: string) => string | null;
  findContents: (doc: Document) => HTMLElement[];
  extractContentInfo: (element: HTMLElement) => ContentInfo;
  onMarkViewed: (markVisibleFn: () => void) => void;

  /**
   * Optional: render fully custom marker UI.
   * When omitted, App.tsx auto-renders shared action buttons + ContentTrackerBadge
   * based on the adapters declared below.
   */
  renderMarker?: (element: HTMLElement, status: ContentStatus) => ReactNode;
  /** Create the DOM container for the React portal inside a content element. */
  createContainer: (element: HTMLElement) => HTMLElement;

  // -- Action adapters (declare to enable the corresponding shared button) --
  parseTask?: ParseTaskAdapter;
  downloadTask?: DownloadTaskAdapter;
}
