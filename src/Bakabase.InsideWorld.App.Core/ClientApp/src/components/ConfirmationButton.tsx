import React, { useCallback, useRef, useState } from 'react';
import { Button } from '@alifd/next';
import { ButtonProps } from '@alifd/next/types/button';
import CustomIcon from '@/components/CustomIcon';
import i18n from 'i18next';

export interface ConfirmationButtonProps extends ButtonProps {
  label: string;
  icon?: string;
  confirmationLabel?: string;
  confirmation?: boolean;
}

const ConfirmationButton = (props: ConfirmationButtonProps) => {
  const { icon, label, confirmationLabel = 'Sure?', confirmation = false, onClick, ...otherProps } = props;
  const [confirming, setConfirming] = useState(false);
  const buttonRef = useRef();

  // fusion will make button blur on mouseup, fuck this.
  const clickingRef = useRef(false);

  console.log('confirmation', confirmation, 'confirming', confirming);

  return (
    <Button
      {...otherProps}
      ref={buttonRef}
      className={'confirmation-button'}
      autoFocus={false}
      onMouseDown={(e) => {
        clickingRef.current = true;
      }}
      onMouseUp={(e) => {
        // console.log('mouse up', this);
        clickingRef.current = false;
      }}
      onBlur={(e) => {
        // console.log('blur', e, e.target, e.currentTarget, document.activeElement, document.activeElement === e.target);
        if (!clickingRef.current) {
          // console.log('setting confirming to false');
          setConfirming(false);
        }
      }}
      onClick={(evt) => {
        // console.log('click', document.activeElement);
        // console.log('confirmation', confirmation, 'confirming', confirming);
        if (confirming || !confirmation) {
          if (onClick) {
            // console.log('on click');
            onClick(evt);
          }
        } else {
          // console.log(ref.current, ref.current._instance);
          // Reset initial status
          buttonRef.current._instance?.button?.focus();
          setConfirming(true);
        }
      }}
    >
      {icon && (<CustomIcon type={icon} size={props.size || 'small'} />)}
      {i18n.t(confirming ? confirmationLabel : label)}
    </Button>
  );
};

export default React.memo(ConfirmationButton);
