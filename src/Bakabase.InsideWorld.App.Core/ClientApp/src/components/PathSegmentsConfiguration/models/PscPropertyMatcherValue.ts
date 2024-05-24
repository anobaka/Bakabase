import type { IPscMatcherValue } from './PscMatcherValue';
import type { ResourceMatcherValueType } from '@/sdk/constants';
import { ResourceProperty } from '@/sdk/constants';
import type { IPscProperty } from '@/components/PathSegmentsConfiguration/models/PscProperty';

export interface IPscPropertyMatcherValue extends IPscMatcherValue {
  propertyId: number;
  isReservedProperty: boolean;
  propertyName?: string;
}

export class PscPropertyMatcherValue implements IPscPropertyMatcherValue {
  propertyId: number;
  isReservedProperty: boolean;
  propertyName?: string;
  valueType: ResourceMatcherValueType;
  layer?: number;
  regex?: string;
  fixedText?: string;

  constructor(init: IPscPropertyMatcherValue) {
    Object.assign(this, init);
  }

  get isRootPath(): boolean {
    return this.isReservedProperty && this.propertyId == ResourceProperty.RootPath;
  }
}
