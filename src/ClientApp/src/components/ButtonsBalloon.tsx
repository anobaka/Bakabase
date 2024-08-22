import React, { useState } from 'react';
import { Balloon } from '@alifd/next';
import { BalloonProps } from '@alifd/next/types/balloon';
import ConfirmationButton, { ConfirmationButtonProps } from '@/components/ConfirmationButton';

interface OperationsBalloonProps extends BalloonProps {
  triggerType?: 'hover' | 'click';
  operations: ConfirmationButtonProps[];
  trigger: any;
  align?: 't' | 'r' | 'b' | 'l' | 'tl' | 'tr' | 'bl' | 'br' | 'lt' | 'lb' | 'rt' | 'rb';
  balloonRenderer?: (buttons: React.ReactNode[]) => React.ReactNode;
  children?: any;
}

export default (props: OperationsBalloonProps) => {
  const { triggerType = 'hover', operations = [], trigger, align = 't', balloonRenderer = (buttons) => buttons, children, ...otherProps } = props;

  const buttons = operations.map((o, i) => (
    <ConfirmationButton
      {...o}
      type={o?.type || 'normal'}
      warning={o?.warning || false}
      size={o.size || 'small'}
      key={i}
    />
  ));

  return (
    <Balloon
      v2
      triggerType={triggerType}
      trigger={trigger}
      closable={false}
      autoFocus={false}
      align={align}
      {...otherProps}
    >
      <div className="confirmation-button-group">
        {children}
        {balloonRenderer(buttons)}
      </div>
    </Balloon>
  );
};
