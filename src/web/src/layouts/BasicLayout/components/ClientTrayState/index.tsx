"use client";

import type React from "react";

import { useEffect, useRef } from "react";

import { clientApi } from "@/core/clientApi";
import { BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

/**
 * How long the state has to hold before it is reported.
 *
 * Tasks finish and start in bursts — a scan completing kicks off indexing — and the icon
 * flickering through idle between them says less than nothing.
 */
const SETTLE_MS = 500;

/**
 * Keeps this machine's tray icon in step with the server's work.
 *
 * Renders nothing. In the all-in-one the task manager sets the icon itself, in the same
 * process, and this component stands aside entirely: the tray there is already correct
 * and a second writer could only make it wrong.
 *
 * In a thin client the tasks are on the server and the tray is on this desk. The window
 * already holds the server's task feed, so the truth is here — it just has to be handed
 * to the process that owns the icon.
 */
const ClientTrayState: React.FC = () => {
  // Not in the desktop app's console: its tray belongs to the app and shows the app's own
  // tasks, and the relay would only answer that it applied nothing.
  const pureClient = useIsPureClient();
  const clientHost = useRemoteAccessStore((state) => state.clientHost);
  const isPureClient = pureClient && clientHost !== "console";
  // A client that has lost its server knows nothing about what it is doing, and the last
  // thing it saw is not evidence that the work is still running.
  const serverReachable = useRemoteAccessStore((state) => state.serverReachable);
  const anyRunning = useBTasksStore((state) =>
    state.tasks.some((t) => t.status === BTaskStatus.Running),
  );

  const running = isPureClient && serverReachable && anyRunning;
  const reported = useRef<boolean>();

  useEffect(() => {
    if (!isPureClient || reported.current === running) {
      return;
    }

    const timer = setTimeout(() => {
      reported.current = running;
      clientApi.setTrayRunning(running).catch(() => {
        // The client not answering about its own icon is not worth telling anyone
        // about. Forget the report so the next change tries again.
        reported.current = undefined;
      });
    }, SETTLE_MS);

    return () => clearTimeout(timer);
  }, [isPureClient, running]);

  return null;
};

ClientTrayState.displayName = "ClientTrayState";

export default ClientTrayState;
