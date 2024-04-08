import { Chip } from '@nextui-org/react';
import React from 'react';


interface IProps {
  children: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  onClick?: () => any;
}

export default (props: IProps) => {
  return (
    <Chip {...props} />
  );
};
