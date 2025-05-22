import { useState } from 'react';
import type { Property } from '@/core/models/Resource';
import { Resource } from '@/core/models/Resource';
import type { PropertyPool, PropertyValueScope } from '@/sdk/constants';

export type PreviewResource = {
  name: string;
  properties?: {[key in PropertyPool]?: Record<number, Property>};
  path: string;
  cover?: string;
  children?: PreviewResource[];
};

export default () => {
  const [resources, setResources] = useState<PreviewResource[]>([]);
  return (
    <div className={'flex flex-col gap-1'}>
      123
    </div>
  );
};
