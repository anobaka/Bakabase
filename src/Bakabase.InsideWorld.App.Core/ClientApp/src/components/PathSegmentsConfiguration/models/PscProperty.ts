import { PscPropertyType } from './PscPropertyType';
import { ResourceProperty } from '@/sdk/constants';

export interface IPscProperty {
  id: number;
  isReserved: boolean;
  name?: string;
}

class PscProperty implements IPscProperty {
  id: number;
  isReserved: boolean;
  name?: string;

  constructor(p: IPscProperty) {
    Object.assign(this, p);
  }

  equals: (other: IPscProperty) => boolean = (other: IPscProperty) => {
    return this.id === other.id && this.isReserved == other.isReserved;
  };

  get key(): string {
    return `${this.isReserved ? 'r' : 'c'}-${this.id}`;
  }

  get type(): PscPropertyType | undefined {
    if (this.isReserved) {
      const rp = this.id as ResourceProperty;
      switch (rp) {
        case ResourceProperty.RootPath:
          return PscPropertyType.RootPath;
        case ResourceProperty.ParentResource:
          return PscPropertyType.ParentResource;
        case ResourceProperty.Resource:
          return PscPropertyType.Resource;
        default:
          return;
      }
    } else {
      return PscPropertyType.CustomProperty;
    }
  }

  get isRootPath(): boolean {
    return this.isReserved && this.id == ResourceProperty.RootPath;
  }

  get isResource(): boolean {
    return this.isReserved && this.id == ResourceProperty.Resource;
  }

  static fromPscType(type: PscPropertyType): PscProperty {
    switch (type) {
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
    isReserved: true,
    name: ResourceProperty[ResourceProperty.Resource],
  });
  static RootPath = new PscProperty({
    id: ResourceProperty.RootPath,
    isReserved: true,
    name: ResourceProperty[ResourceProperty.RootPath],
  });
  static ParentResource = new PscProperty({
    id: ResourceProperty.ParentResource,
    isReserved: true,
    name: ResourceProperty[ResourceProperty.ParentResource],
  });
  static CustomProperty = new PscProperty({
    id: ResourceProperty.CustomProperty,
    isReserved: true,
    name: ResourceProperty[ResourceProperty.CustomProperty],
  });
}

export default PscProperty;
