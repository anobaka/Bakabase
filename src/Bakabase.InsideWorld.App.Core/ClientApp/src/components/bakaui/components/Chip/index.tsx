import { Chip } from '@nextui-org/react';
import React from 'react';


interface IProps {
  children: React.ReactNode;
}

export default (props: IProps) => {
  return (
    <Chip {...props} />
  );
};
