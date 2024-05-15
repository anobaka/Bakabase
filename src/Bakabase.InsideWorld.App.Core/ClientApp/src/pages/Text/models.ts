import type { SpecialTextType } from '@/sdk/constants';

export type SpecialText = {
  id: number;
  value1: string;
  value2?: string;
  type: SpecialTextType;
};
