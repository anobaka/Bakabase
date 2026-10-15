import { useTranslation } from "react-i18next";

import { deviceColor } from "./resourceIdentity";

import { useUiOptionsStore } from "@/stores/options";

/** Owner identity is independent of its display color and of the browser viewing this server. */
export default function DeviceDot({
  serverId,
  name,
  isSource = false,
  className = "h-2 w-2",
}: {
  serverId: string;
  name?: string;
  isSource?: boolean;
  className?: string;
}) {
  const { t } = useTranslation();
  const color = useUiOptionsStore((state) => deviceColor(serverId, state.data.deviceColors));
  const label = t<string>(isSource ? "deviceAppearance.source" : "deviceAppearance.device", {
    name: name || serverId,
  });

  return (
    <span
      aria-label={label}
      className={`inline-block shrink-0 rounded-full ${className}`}
      style={{ backgroundColor: color }}
      title={label}
    />
  );
}
