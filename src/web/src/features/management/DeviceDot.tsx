import { deviceColor } from "./resourceIdentity";

import { useUiOptionsStore } from "@/stores/options";

/** Owner identity is independent of its display color and of the browser viewing this server. */
export default function DeviceDot({
  serverId,
  name,
  className = "h-2 w-2",
}: {
  serverId: string;
  name?: string;
  className?: string;
}) {
  const color = useUiOptionsStore((state) => deviceColor(serverId, state.data.deviceColors));

  return (
    <span
      aria-label={name}
      className={`inline-block shrink-0 rounded-full ${className}`}
      style={{ backgroundColor: color }}
      title={name}
    />
  );
}
