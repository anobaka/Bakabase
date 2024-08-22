import React from 'react';
import { generateTrees } from '../data/tree';
import { Button, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { createPortal } = useBakabaseContext();
  return (
    <Button
      size={'sm'}
      color={'primary'}
      onClick={() => {
        const trees = generateTrees();
        createPortal(Modal, {
          defaultVisible: true,
          children: renderTreeNodes(trees),
          size: 'xl',
        });
      }}
    >
      Open multilevel value editor
    </Button>
  );
};
