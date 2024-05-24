import { ResourceProperty } from '@/sdk/constants';
import { PscPropertyType } from '@/components/PathSegmentsConfiguration/models/PscPropertyType';

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

  equals: (other: PscProperty) => boolean = (other: PscProperty) => {
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
}

export default PscProperty;
