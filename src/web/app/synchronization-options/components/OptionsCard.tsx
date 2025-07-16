import React from 'react';
import { Card, CardBody, CardHeader } from '@/components/bakaui';

type Props = {
  header: React.ReactNode;
  children?: React.ReactNode;
};

export default ({
                  header,
                  children,
                }: Props) => {
  return (
    <Card>
      <CardHeader>
        <div className={'text-xl'}>
          {header}
        </div>
      </CardHeader>
      <CardBody>
        <div className={'grid gap-2 items-center'} style={{ gridTemplateColumns: 'repeat(2, auto)' }}>
          {children}
        </div>
      </CardBody>
    </Card>
  );
};
