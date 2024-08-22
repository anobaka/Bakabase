import { SortableContainer } from 'react-sortable-hoc';
import React from 'react';
import SortableEnhancer from '@/components/EnhancerSelector/components/SortableEnhancer';

export default SortableContainer(({ enhancers }) => {
  console.log(enhancers);
  return (
    <div className="enhancer-orders">
      {enhancers?.map((enhancer, index) => (
        <>
          <SortableEnhancer enhancer={enhancer} index={index} />
          {index != enhancers.length - 1 && '>'}
        </>
      ))}
    </div>
  );
});
