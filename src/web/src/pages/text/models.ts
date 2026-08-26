import type { TextTypeShape, WellKnownTextType } from "@/sdk/constants";

export type TextType = {
  id: number;
  name: string;
  wellKnown?: WellKnownTextType;
  shape: TextTypeShape;
  description?: string;
  entryCount: number;
};

export type TextEntry = {
  id: number;
  typeId: number;
  value1: string;
  value2?: string;
};
