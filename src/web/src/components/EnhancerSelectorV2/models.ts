import type {
  EnhancerId,
  EnhancerTag,
  EnhancerTargetOptionsItem,
  PropertyPool,
  PropertyType,
  PropertyValueScope,
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

// --- Models migrated from CategoryEnhancerOptionsDialog/models.ts ---

export interface CategoryEnhancerFullOptions {
  categoryId: number;
  enhancerId: EnhancerId;
  active: boolean;
  options?: EnhancerFullOptions;
}

export interface EnhancerTranslationOptions {
  enabled?: boolean;
  targetLanguage?: string;
}

export interface EnhancerFullOptions {
  targetOptions?: EnhancerTargetFullOptions[];
  requirements?: EnhancerId[];
  expressions?: string[];
  keywordProperty?: {
    pool: PropertyPool;
    id: number;
    scope: PropertyValueScope;
  };
  pretreatKeyword?: boolean;
  bangumiPrioritySubjectType?: number;
  translationOptions?: EnhancerTranslationOptions;
}

export interface EnhancerTargetFullOptions {
  propertyId?: number;
  autoMatchMultilevelString?: boolean;
  autoBindProperty?: boolean;
  target: number;
  dynamicTarget?: string;
  propertyPool?: PropertyPool;
  customPrompt?: string;
}

export function defaultEnhancerTargetOptions(
  descriptor: EnhancerTargetDescriptor,
): EnhancerTargetFullOptions {
  return {
    target: descriptor.id,
  };
}
