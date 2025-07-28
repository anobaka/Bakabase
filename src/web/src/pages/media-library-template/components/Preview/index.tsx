"use client";

import type { Property } from "@/core/models/Resource";
import type { PropertyPool } from "@/sdk/constants";

import { useState } from "react";

export type PreviewResource = {
  name: string;
  properties?: { [key in PropertyPool]?: Record<number, Property> };
  path: string;
  cover?: string;
  children?: PreviewResource[];
};
const Preview = () => {
  const [resources, setResources] = useState<PreviewResource[]>([]);

  return <div className={"flex flex-col gap-1"}>123</div>;
};

Preview.displayName = "Preview";

export default Preview;
