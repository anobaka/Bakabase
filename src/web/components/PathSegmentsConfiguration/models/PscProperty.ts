import type { TFunction } from 'react-i18next';
import { PscPropertyType } from './PscPropertyType';
import { ResourceProperty } from '@/sdk/constants';

export interface IPscProperty {
  id: number;
  isCustom: boolean;
  name?: string;
}

class PscProperty implements IPscProperty {
  id: number;
  isCustom: boolean;
  name?: string;

  constructor(p: IPscProperty) {
    Object.assign(this, p);
  }

  equals: (other: IPscProperty) => boolean = (other: IPscProperty) => {
    return this.id === other.id && this.isCustom == other.isCustom;
  };

  get key(): string {
    return `${(this.isCustom ? 'c' : 'r')}-${this.id}`;
  }

  get type(): PscPropertyType | undefined {
    if (this.isCustom) {
      return PscPropertyType.CustomProperty;
    } else {
      const rp = this.id as ResourceProperty;
      switch (rp) {
        case ResourceProperty.RootPath:
          return PscPropertyType.RootPath;
        case ResourceProperty.ParentResource:
          return PscPropertyType.ParentResource;
        case ResourceProperty.Resource:
          return PscPropertyType.Resource;
        case ResourceProperty.Rating:
          return PscPropertyType.Rating;
        case ResourceProperty.Introduction:
          return PscPropertyType.Introduction;
        default:
          return;
      }
    }
  }

  get isRootPath(): boolean {
    return !this.isCustom && this.id == ResourceProperty.RootPath;
  }

  get isResource(): boolean {
    return !this.isCustom && this.id == ResourceProperty.Resource;
  }

  toString(t: TFunction<'translation', undefined>, index?: number): string {
    return `${(this.isCustom ? this.name : t(ResourceProperty[this.id]))}${index == undefined ? '' : index + 1}`;
  }

  static fromPscType(type: PscPropertyType): PscProperty {
    switch (type) {
      case PscPropertyType.Rating:
        return this.Rating;
      case PscPropertyType.Introduction:
        return this.Introduction;
      case PscPropertyType.RootPath:
        return this.RootPath;
      case PscPropertyType.Resource:
        return this.Resource;
      case PscPropertyType.ParentResource:
        return this.ParentResource;
      case PscPropertyType.CustomProperty:
        return this.CustomProperty;
    }
  }

  static Resource = new PscProperty({
    id: ResourceProperty.Resource,
    isCustom: false,
    name: ResourceProperty[ResourceProperty.Resource],
  });
  static RootPath = new PscProperty({
    id: ResourceProperty.RootPath,
    isCustom: false,
    name: ResourceProperty[ResourceProperty.RootPath],
  });
  static ParentResource = new PscProperty({
    id: ResourceProperty.ParentResource,
    isCustom: false,
    name: ResourceProperty[ResourceProperty.ParentResource],
  });
  static Rating = new PscProperty({
    id: ResourceProperty.Rating,
    isCustom: false,
    name: ResourceProperty[ResourceProperty.Rating],
  });
  static Introduction = new PscProperty({
    id: ResourceProperty.Introduction,
    isCustom: false,
    name: ResourceProperty[ResourceProperty.Introduction],
  });
  static CustomProperty = new PscProperty({
    id: ResourceProperty.CustomProperty,
    isCustom: true,
    name: ResourceProperty[ResourceProperty.CustomProperty],
  });
}

export default PscProperty;
