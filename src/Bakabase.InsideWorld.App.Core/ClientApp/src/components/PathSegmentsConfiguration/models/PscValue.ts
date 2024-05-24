import type { IPscPropertyMatcherValue } from './PscPropertyMatcherValue';
import { PscPropertyMatcherValue } from './PscPropertyMatcherValue';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import { standardizePath } from '@/components/utils';


interface IPscValue {
  path?: string;
  rpmValues?: IPscPropertyMatcherValue[];
}

class PscValue implements IPscValue {
  path?: string;
  rpmValues?: PscPropertyMatcherValue[];

  constructor(init?: Partial<IPscValue>) {
    Object.assign(this, init);
    this.rpmValues = init?.rpmValues?.map(s => new PscPropertyMatcherValue(s)) || [];
  }

  static fromComponentValue(value: PscPropertyMatcherValue[]): PscValue {
    const rootPath = (value.filter(v => v.isRootPath))[0]?.fixedText;
    const dto = new PscValue({
      path: rootPath,
      // regex: resourceRegex,
      rpmValues: value.filter(v => !v.isRootPath),
    });

    console.log('Convert component value to dto value', value, dto);
    return dto;
  }

  toComponentValue(): PscPropertyMatcherValue[] {
    const value: PscPropertyMatcherValue[] = [];
    if (this.path) {
      value.push(
        new PscPropertyMatcherValue({
          valueType: ResourceMatcherValueType.FixedText,
          fixedText: this.path,
          propertyId: ResourceProperty.RootPath,
          isReservedProperty: true,
        }),
      );
    }

    if (this.rpmValues) {
      for (const segment of this.rpmValues) {
        (valueMap[segment.property] ??= []).push(segment.toValue());
      }
    }

    console.log(valueMap);

    const rootPathValue = (valueMap[ResourceProperty.RootPath] || [])[0];
    if (rootPathValue) {
      if (rootPathValue.fixedText) {
        rootPathValue.fixedText = standardizePath(rootPathValue.fixedText);
      }
    }

    console.log('Convert dto value to component value', this, valueMap);
    return valueMap;
  }
}


export {
  PscValue,
  IPscValue,
};
