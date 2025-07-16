import type {
  EnhancerId,
  EnhancerTargetOptionsItem,
  PropertyType,
  StandardValueType,
} from "@/sdk/constants";

export type EnhancerDescriptor = {
  id: EnhancerId;
  name: string;
  description?: string;
  targets: EnhancerTargetDescriptor[];
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
