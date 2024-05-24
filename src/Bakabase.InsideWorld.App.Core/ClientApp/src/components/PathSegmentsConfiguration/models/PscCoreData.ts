import i18n from 'i18next';
import type { TFunction } from 'react-i18next';
import { ResourceProperty } from '@/sdk/constants';
import {
  SegmentMatcherConfigurationModesData,
} from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration';
import { allMatchers } from '@/components/PathSegmentsConfiguration/models/instances';
import PscProperty from '@/components/PathSegmentsConfiguration/models/PscProperty';
import { PscPropertyType } from '@/components/PathSegmentsConfiguration/models/PscPropertyType';

class PscCoreData {
  segments: PscCoreData.Segment[] = [];
  globalErrors: PscCoreData.SimpleGlobalError[] = [];
  globalMatches: PscCoreData.SimpleGlobalMatch[] = [];

  get hasError(): boolean {
    const error = this.globalErrors?.length > 0 ||
      this.segments.some(s => s.matchResults?.some(t => t.errors && t.errors.length > 0));
    if (error) {
      return true;
    }
    const missingProperties = allMatchers.filter(a => a.isRequired &&
      !this.segments.some(e => e.matchResults.some(r => r.property.type == a.propertyType)));
    return missingProperties.length > 0;
  }
}

namespace PscCoreData {
  class SimplePscCoreDataItem {
    property: PscProperty;
    valueIndex?: number;

    constructor(property: PscProperty, valueIndex: number | undefined) {
      this.property = property;
      this.valueIndex = valueIndex;
    }

    get label(): string {
      return BuildMatchResultLabel(this.property, this.valueIndex);
    }
  }

  export class Segment {
    text: string;
    matchResults: PscCoreData.SimpleMatchResult[] = [];
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
    propertyType: PscPropertyType;
    readonly: boolean;
    replaceCurrent: boolean;
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
      return !this.readonly && (Object.keys(this.matchModes).some(s => this.matchModes[s].available));
    }

    get useSmc(): boolean {
      return this.isConfigurable && !this.matchModes.oneClick.available;
    }

    buildProperty(property?: { id: number; name: string }, t: TFunction<'translation', undefined>): PscProperty {
      switch (this.propertyType) {
        case PscPropertyType.RootPath:
          return new PscProperty(ResourceProperty.RootPath, true, t(ResourceProperty.RootPath));
        case PscPropertyType.Resource:
          return new PscProperty(ResourceProperty.Resource, true, t(ResourceProperty.Resource));
        case PscPropertyType.ParentResource:
          return new PscProperty(ResourceProperty.ParentResource, true, t(ResourceProperty.ParentResource));
        case PscPropertyType.CustomProperty:
          return new PscProperty(property!.id, false, property!.name);
      }
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

  class CommonMatchMode {
    available: boolean;
    errors: string[] = [];
  }

  class RegexMatchMode extends CommonMatchMode {
    text?: string;
  }

  class LayerMatchMode extends CommonMatchMode {
    layers: number[] = [];
  }

  export class SimpleGlobalMatch extends SimplePscCoreDataItem {
    matches: string[];

    constructor(property: PscProperty, valueIndex: number | undefined, matches: string[]) {
      super(property, valueIndex);
      this.matches = matches;
    }
  }

  export class SimpleGlobalError extends SimplePscCoreDataItem {
    message: string;
    deletable: boolean;

    constructor(property: PscProperty, valueIndex: number | undefined, message: string, deletable: boolean) {
      super(property, valueIndex);
      this.message = message;
      this.deletable = deletable;
    }
  }


  export class SimpleMatchResult extends SimplePscCoreDataItem {
    errors: string[] = [];
    readonly: boolean = false;

    constructor(property: PscProperty, valueIndex: number | undefined, errors?: string[]) {
      super(property, valueIndex);
      this.errors = errors || [];
    }
  }
}

function BuildMatchResultLabel(property: PscProperty, valueIndex: number | undefined): string {
  return `${property.isReserved ? i18n.t(ResourceProperty[property.id]) : property.name}${valueIndex == undefined ? '' : valueIndex + 1}`;
}


export {
  PscCoreData,
  BuildMatchResultLabel,
};
