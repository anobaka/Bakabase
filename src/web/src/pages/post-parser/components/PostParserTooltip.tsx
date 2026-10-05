import type { ComponentProps } from "react";

import Tooltip from "@/components/bakaui/components/Tooltip";

// Avoid opening a description whenever the pointer briefly crosses a compact button.
export default function PostParserTooltip(props: ComponentProps<typeof Tooltip>) {
  return <Tooltip closeDelay={200} delay={250} {...props} />;
}
