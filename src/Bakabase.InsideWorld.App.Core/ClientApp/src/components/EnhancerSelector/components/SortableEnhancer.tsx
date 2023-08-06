import { SortableElement } from 'react-sortable-hoc';
import React from 'react';
import i18n from 'i18next';

export default SortableElement(({
  enhancer,
}: { enhancer: any }) => {
  return (
    <div className={'sortable-enhancer-order'}>
      {i18n.t(enhancer?.name)}
    </div>
  );
});
