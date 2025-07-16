import i18n from 'i18next';
import type { TFunction } from 'react-i18next';
import { allMatchers } from '../matchers';
import { SegmentMatcherConfigurationModesData } from '../SegmentMatcherConfiguration';
import type PscProperty from './PscProperty';
import type { PscPropertyType } from './PscPropertyType';
import { ResourceProperty } from '@/sdk/constants';

export class SimplePscContextItem {
  property: PscProperty;
  valueIndex?: number;

  constructor(property: PscProperty, valueIndex: number | undefined) {
    this.property = property;
    this.valueIndex = valueIndex;
  }

  toString(t: TFunction<'translation', undefined>): string {
    return this.property.toString(t, this.valueIndex);
  }
}

export class Segment {
  text: string = '';
  matchResults: SimpleMatchResult[] = [];
  selectiveMatchers: SelectiveMatcher[] = [];
  /**
   * Not truly disabled, it displays as disabled
   */
  disabled: boolean = false;

  constructor(init?: Partial<Segment>) {
    Object.assign(this, init);
  }
}

export enum SelectMode {
  Segment,
  Layer,
  Regex,
}

export class SelectiveMatcher {
  propertyType: PscPropertyType = 0 as PscPropertyType;
  readonly: boolean = false;
  replaceCurrent: boolean = false;
  errors: string[] = [];
  matchModes: {
    oneClick: CommonMatchMode;
    regex: RegexMatchMode;
    layer: LayerMatchMode;
  } = {
    oneClick: new CommonMatchMode(),
    regex: new RegexMatchMode(),
    layer: new LayerMatchMode(),
  };

  constructor(init?: Partial<SelectiveMatcher>) {
    Object.assign(this, init);
  }

  get isConfigurable(): boolean {
    return !this.readonly && (Object.keys(this.matchModes).some(s => (this.matchModes as any)[s].available));
  }

  get useSmc(): boolean {
    return this.isConfigurable && !this.matchModes.oneClick.available;
  }

  buildModesData(): SegmentMatcherConfigurationModesData {
    const data = new SegmentMatcherConfigurationModesData();
    if (this.matchModes.regex.available) {
      data.regex = {
        text: this.matchModes.regex.text!,
      };
    }
    if (this.matchModes.layer.available) {
      data.layers = this.matchModes.layer.layers;
    }
    return data;
  }
}

export class CommonMatchMode {
  available: boolean = false;
  errors: string[] = [];
}

export class RegexMatchMode extends CommonMatchMode {
  text?: string;
}

export class LayerMatchMode extends CommonMatchMode {
  layers: number[] = [];
}

export class SimpleGlobalMatchResult extends SimplePscContextItem {
  matches: string[];

  constructor(property: PscProperty, valueIndex: number | undefined, matches: string[]) {
    super(property, valueIndex);
    this.matches = matches;
  }
}

export class SimpleMatchResult extends SimplePscContextItem {
  errors: string[] = [];
  readonly: boolean = false;

  constructor(property: PscProperty, valueIndex: number | undefined, errors?: string[]) {
    super(property, valueIndex);
    this.errors = errors || [];
  }
}

export class SimpleGlobalError extends SimplePscContextItem {
  message: string;
  deletable: boolean;
  preventSubmitting: boolean;

  constructor(property: PscProperty, valueIndex: number | undefined, message: string, deletable: boolean, preventSubmitting: boolean) {
    super(property, valueIndex);
    this.message = message;
    this.deletable = deletable;
    this.preventSubmitting = preventSubmitting;
  }

  equals(other: SimpleGlobalError): boolean {
    return this.property.equals(other.property) && this.valueIndex == other.valueIndex && this.message == other.message;
  }
}

export class PscContext {
  segments: Segment[] = [];
  globalErrors: SimpleGlobalError[] = [];
  globalMatches: SimpleGlobalMatchResult[] = [];

  get preventSubmitting(): boolean {
    const error = this.globalErrors?.filter(e => e.preventSubmitting).length > 0 ||
      this.segments.some(s => s.matchResults?.some(t => t.errors && t.errors.length > 0));
    if (error) {
      return true;
    }
    const missingProperties = allMatchers.filter(a => a.isRequired &&
      !this.segments.some(e => e.matchResults.some(r => r.property.type == a.propertyType)));
    return missingProperties.length > 0;
  }
}
