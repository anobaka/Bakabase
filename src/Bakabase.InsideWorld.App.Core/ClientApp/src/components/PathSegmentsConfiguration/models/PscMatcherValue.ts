import type { TFunction } from 'react-i18next';
import { ResourceMatcherValueType } from '@/sdk/constants';

export interface IPscMatcherValue {
  valueType: ResourceMatcherValueType;
  /**
   * starts from 1
   */
  layer?: number;
  regex?: string;
  fixedText?: string;
}

export class PscMatcherValue implements IPscMatcherValue {
  valueType: ResourceMatcherValueType;
  layer?: number | undefined;
  regex?: string | undefined;
  fixedText?: string | undefined;


  constructor(v: IPscMatcherValue) {
    Object.assign(this, v);
  }

  static Layer(layer: number): IPscMatcherValue {
    return new PscMatcherValue({
      valueType: ResourceMatcherValueType.Layer,
      layer,
    });
  }

  static Regex(regex: string): IPscMatcherValue {
    return new PscMatcherValue({
      valueType: ResourceMatcherValueType.Regex,
      regex,
    });
  }

  static FixedText(fixedText: string): IPscMatcherValue {
    return new PscMatcherValue({
      valueType: ResourceMatcherValueType.FixedText,
      fixedText,
    });
  }

  static ToString(t: TFunction<'translation', undefined>, value: IPscMatcherValue): string {
    switch (value.valueType) {
      case ResourceMatcherValueType.FixedText:
        return t('Fixed text: {{text}}', { text: value.fixedText });
      case ResourceMatcherValueType.Regex:
        return t('Regex: {{regex}}', { regex: value.regex });
      case ResourceMatcherValueType.Layer:
        if (value.layer && value.layer < 0) {
          return t('Layer: the {{layer}} layer to the last', { layer: -value.layer });
        } else {
          return t('Layer: the {{layer}} layer', { layer: value.layer });
        }
    }
  }

  equals(other: IPscMatcherValue): boolean {
    if (this.valueType !== other.valueType) {
      return false;
    }
    switch (this.valueType) {
      case ResourceMatcherValueType.Layer:
        return this.layer === other.layer;
      case ResourceMatcherValueType.Regex:
        return this.regex === other.regex;
      case ResourceMatcherValueType.FixedText:
        return this.fixedText === other.fixedText;
    }
    return false;
  }

  buildDisplayText(t: TFunction<'translation', undefined>): string {
    return PscMatcherValue.ToString(t, this);
  }
}
