import type { EnhancerId, PropertyValueScope } from "@/sdk/constants";
import type { PropertyPool } from "@/sdk/constants";

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
