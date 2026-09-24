"use client";

import ServerLog from "./ServerLog";

/**
 * The log page: what the server this window shows recorded.
 *
 * In the desktop app showing a managed server that is the managed server's log. Its relay
 * deliberately does not serve this computer's log to the page, because the page belongs to
 * the other server; this computer's log is on its own window's log page.
 */
export default function LogPage() {
  return (
    <div className="p-4">
      <ServerLog />
    </div>
  );
}
