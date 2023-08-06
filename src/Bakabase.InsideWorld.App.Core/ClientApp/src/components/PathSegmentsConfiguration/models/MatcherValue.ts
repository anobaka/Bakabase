import i18n from '@/i18n';
import { ResourceMatcherValueType } from '@/sdk/constants';

interface IMatcherValue {
  type: ResourceMatcherValueType;
  layer?: number;
  // isReverse?: boolean;
  key?: string;
  regex?: string;
  fixedText?: string;
}

class MatcherValue implements IMatcherValue {
  type: ResourceMatcherValueType;
  /**
   * starts from 1
   */
  layer?: number;
  // isReverse?: boolean;
  key?: string;
  regex?: string;
  fixedText?: string;

  constructor(init?: Partial<IMatcherValue>) {
    Object.assign(this, init);
  }

  static Layer(layer: number): MatcherValue {
    return new MatcherValue({
      type: ResourceMatcherValueType.Layer,
      layer,
    });
  }

  static Regex(regex: string): MatcherValue {
    return new MatcherValue({
      type: ResourceMatcherValueType.Regex,
      regex,
    });
  }

  static FixedText(fixedText: string): MatcherValue {
    return new MatcherValue({
      type: ResourceMatcherValueType.FixedText,
      fixedText,
    });
  }

  equals(other: IMatcherValue): boolean {
    if (this.type !== other.type) {
      return false;
    }
    switch (this.type) {
      case ResourceMatcherValueType.Layer:
        return this.layer === other.layer;
      case ResourceMatcherValueType.Regex:
        return this.regex === other.regex;
      case ResourceMatcherValueType.FixedText:
        return this.fixedText === other.fixedText;
    }
    return false;
  }

  toString(): string {
    switch (this.type) {
      case ResourceMatcherValueType.FixedText:
        return i18n.t('Fixed text: {{text}}', { text: this.fixedText });
      case ResourceMatcherValueType.Regex:
        return i18n.t('Regex: {{regex}}', { regex: this.regex });
      case ResourceMatcherValueType.Layer:
        if (this.layer && this.layer < 0) {
          return i18n.t('Layer: the {{layer}} layer to the last', { layer: -this.layer });
        } else {
          return i18n.t('Layer: the {{layer}} layer', { layer: this.layer });
        }
    }
  }
}

export {
  IMatcherValue,
  ResourceMatcherValueType,
  MatcherValue,
};
