import type {
  EnhancerId,
  EnhancerTag,
  EnhancerTargetOptionsItem,
  PropertyType,
  StandardValueType,
} from "@/sdk/constants";

export type EnhancerDescriptor = {
  id: EnhancerId;
  name: string;
  description?: string;
  targets: EnhancerTargetDescriptor[];
  tags: EnhancerTag[];
};

export type EnhancerTargetDescriptor = {
  id: number;
  isDynamic: boolean;
  name: string;
  description?: string;
  valueType: StandardValueType;
  optionsItems?: EnhancerTargetOptionsItem[];
  propertyType: PropertyType;
};
