"use client";

import type { ReactNode } from "react";
import type { MediaKind } from "./devices";

import { AiOutlineCustomerService, AiOutlineRead, AiOutlineVideoCamera } from "react-icons/ai";

/**
 * Shared pieces of the topic's mock-ups: a window frame, and the icon that tells whose
 * storage a thumbnail came from (the desktop PC keeps films, the laptop comics, the NAS
 * music — see `devices.ts`).
 */

const mediaIcons: Record<MediaKind, typeof AiOutlineVideoCamera> = {
  video: AiOutlineVideoCamera,
  comic: AiOutlineRead,
  music: AiOutlineCustomerService,
};

export const MediaIcon = ({ kind, className }: { kind: MediaKind; className?: string }) => {
  const Icon = mediaIcons[kind];

  return <Icon aria-hidden className={className} />;
};

/**
 * A simplified application window. Purely a picture: the controls inside it are the
 * mock-up's own, labelled for what they demonstrate, and it never pretends to be the
 * real page.
 */
export const MockWindow = ({
  title,
  badge,
  children,
  className,
}: {
  title: ReactNode;
  badge?: ReactNode;
  children: ReactNode;
  className?: string;
}) => (
  <div
    className={`overflow-hidden rounded-xl border border-default-200 bg-content1 shadow-sm ${
      className ?? ""
    }`}
  >
    <div className="flex items-center gap-2 border-b border-default-200 bg-default-100 px-3 py-1.5">
      <span aria-hidden className="flex shrink-0 gap-1">
        <span className="h-2 w-2 rounded-full bg-default-300" />
        <span className="h-2 w-2 rounded-full bg-default-300" />
        <span className="h-2 w-2 rounded-full bg-default-300" />
      </span>
      <span className="min-w-0 flex-1 truncate text-xs font-medium text-default-600">{title}</span>
      {badge}
    </div>
    {children}
  </div>
);
