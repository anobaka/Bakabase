import { SortableElement } from 'react-sortable-hoc';
import React from 'react';
import i18n from 'i18next';
import SimpleLabel from '@/components/SimpleLabel';

export default SortableElement(({
  enhancer,
}: { enhancer: any }) => {
  return (
    <SimpleLabel className={'sortable-enhancer-order'}>
      {i18n.t(enhancer?.name)}
    </SimpleLabel>
  );
});
