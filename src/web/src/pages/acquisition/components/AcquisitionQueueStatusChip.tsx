import type { ReactNode } from "react";
import { Chip, Spinner } from "@/components/bakaui";

const processingStatuses = new Set(["starting", "running", "checking", "submitted"]);

/** Maps queue states to the same semantic colors used by the rest of the acquisition UI. */
export function acquisitionQueueStatusColor(status: string) {
  switch (status) {
    case "completed":
      return "success" as const;
    case "attention":
    case "paused":
    case "spaceBlocked":
      return "warning" as const;
    case "failed":
    case "error":
    case "unknown":
      return "danger" as const;
    case "queued":
    case "starting":
    case "running":
    case "checking":
    case "submitted":
      return "primary" as const;
    default:
      return "default" as const;
  }
}

export default function AcquisitionQueueStatusChip({
  status,
  children,
}: {
  status: string;
  children: ReactNode;
}) {
  const processing = processingStatuses.has(status);
  return (
    <Chip
      size="sm"
      variant="flat"
      color={acquisitionQueueStatusColor(status)}
      startContent={
        processing ? (
          <Spinner size="sm" aria-hidden classNames={{ wrapper: "h-3 w-3 shrink-0" }} />
        ) : undefined
      }
    >
      {children}
    </Chip>
  );
}
