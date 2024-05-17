import React from 'react';
import { useTranslation } from 'react-i18next';
import type { Resource } from '@/core/models/Resource';
import { Chip } from '@/components/bakaui';
import CustomPropertyValue from '@/components/Resource/components/DetailDialog/CustomPropertyValue';

type Props = {
  resource: Resource;
  className?: string;
};

export default ({ resource, className }: Props) => {
  const { t } = useTranslation();
  const cps = resource.customPropertiesV2 ?? [];
  const cpvs = resource.customPropertyValues ?? [];

  if (cps.length == 0) {
    return (
      <div className={'opacity-60'}>
        {t('There is no property bound yet, you can bind properties to category first.')}
      </div>
    );
  }

  return (
    <div
      className={`grid gap-2 ${className}`}
      style={{ gridTemplateColumns: 'auto 1fr' }}
    >
      {cps.map((cp, i) => {
        return (
          <>
            <Chip
              size={'sm'}
              radius={'sm'}
            >
              {cp.name}
            </Chip>
            <div>
              <CustomPropertyValue
                resourceId={resource.id}
                property={cp}
                value={cpvs[i]?.value}
              />
            </div>
          </>
        );
      })}
    </div>
  );
};
