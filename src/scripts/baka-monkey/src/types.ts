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

export interface SiteConfig {
  key: string;
  domains: string[];
  extractFilter: (url: string) => string | null;
  findContents: (doc: Document) => HTMLElement[];
  extractContentInfo: (element: HTMLElement) => ContentInfo;
  onMarkViewed: (markVisibleFn: () => void) => void;
  /** Render site-specific overlay/buttons for a content element. */
  renderMarker: (element: HTMLElement, status: ContentStatus) => ReactNode;
}
