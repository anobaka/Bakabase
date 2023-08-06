import { ResourceProperty } from '@/sdk/constants';
import { standardizePath } from '@/components/utils';
import { MatcherValue, ResourceMatcherValueType } from '@/components/PathSegmentsConfiguration/models/MatcherValue';

interface IPscMatcherValue {
  property: ResourceProperty;
  valueType: ResourceMatcherValueType;
  layer?: number;
  key?: string;
  regex?: string;
}

class PscMatcherValue implements IPscMatcherValue {
  property: ResourceProperty;
  valueType: ResourceMatcherValueType;
  layer?: number;
  key?: string;
  regex?: string;

  constructor(init?: Partial<IPscMatcherValue>) {
    Object.assign(this, init);
  }

  toValue(): MatcherValue {
    return new MatcherValue({
      type: this.valueType,
      key: this.key,
      layer: this.layer,
      regex: this.regex,
    });
  }
}

interface IPscValue {
  path?: string;
  // regex?: string;
  rpmValues?: IPscMatcherValue[];
}

class PscValue implements IPscValue {
  path?: string;
  // regex?: string;
  rpmValues?: PscMatcherValue[];

  constructor(init?: Partial<IPscValue>) {
    Object.assign(this, init);
    this.rpmValues = init?.rpmValues?.map(s => new PscMatcherValue(s)) || [];
  }

  static fromComponentValue(valueMap: { [property in ResourceProperty]?: MatcherValue[] }): PscValue {
    const rootPath = (valueMap[ResourceProperty.RootPath] || [])[0]?.fixedText;

    const segmentConfigurations: IPscMatcherValue[] = [];

    Object.keys(valueMap)
      .forEach(a => {
        const property: ResourceProperty = parseInt(a, 10);
        if (property != ResourceProperty.RootPath) {
          const oValues = valueMap[property] || [];

          for (const ov of oValues) {
            segmentConfigurations.push({
              ...ov,
              valueType: ov.type,
              property,
            });
          }
        }
      });

    const dto = new PscValue({
      path: rootPath,
      // regex: resourceRegex,
      rpmValues: segmentConfigurations,
    });

    console.log('Convert component value to dto value', valueMap, dto);

    return dto;
  }

  toComponentValue(): { [property in ResourceProperty]?: MatcherValue[] } {
    const valueMap: { [property in ResourceProperty]?: MatcherValue[] } = {};

    if (this.path) {
      valueMap[ResourceProperty.RootPath] = [
        new MatcherValue({
          type: ResourceMatcherValueType.FixedText,
          fixedText: this.path,
        }),
      ];
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
  PscMatcherValue,
  IPscMatcherValue,
};
