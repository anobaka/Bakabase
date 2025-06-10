import React from 'react';
import { TbExternalLink } from 'react-icons/tb';
import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';
import BApi from '@/sdk/BApi';

type Props = {
  href: string;
} & ButtonProps;

export default ({ href, children, ...otherProps }: Props) => {
  return (
    <Button
      color={'primary'}
      href={href}
      variant={'light'}
      {...otherProps}
      onPress={(e) => {
        BApi.gui.openUrlInDefaultBrowser({ url: href });
      }}
    >
      {children}
      <TbExternalLink className={'text-medium'} />
    </Button>
  );
};
