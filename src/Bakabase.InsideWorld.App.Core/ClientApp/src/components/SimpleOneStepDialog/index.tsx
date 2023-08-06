import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Dialog } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useUpdateEffect } from 'react-use';
import { act } from 'react-dom/test-utils';
import { buildLogger, forceFocus, uuidv4 } from '@/components/utils';

interface IResult {
  code?: number;
  message?: string | null;
}

export interface ISimpleOneStepDialogProps extends DialogProps {
  onOk: () => (boolean | IResult | Promise<boolean | IResult>);
  title: any;
  children: any;
}

const log = buildLogger('SimpleOneStepDialog');

export default (props: ISimpleOneStepDialogProps) => {
  const {
    onOk: propsOnOk,
    okProps: propsOkProps,
    children,
    ...otherProps
  } = props;

  const { t } = useTranslation();

  const [visible, setVisible] = useState(true);

  const [processing, setProcessing] = useState(false);
  const processingRef = useRef(processing);

  const enterBtnIdRef = useRef(uuidv4());

  useEffect(() => {
    const forceFocusInterval = setInterval(() => {
      const target = document.getElementById(enterBtnIdRef.current);
      if (!target) {
        clearInterval(forceFocusInterval);
      }
      const current = document.activeElement;
      if (target != current) {
        log('Force focusing');
        forceFocus(target);
      }
    }, 250);

    return () => {
      log('Stop force focusing');
      clearInterval(forceFocusInterval);
    };
  }, []);

  useUpdateEffect(() => {
    processingRef.current = processing;
  }, [processing]);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  const onOk = useCallback(async () => {
    if (processingRef.current) {
      throw new Error('Processing');
    }

    setProcessing(true);

    const result = await Promise.resolve(propsOnOk());
    let success: boolean;
    let message: string | undefined | null;
    if (typeof result === 'boolean') {
      success = result;
    } else {
      success = result.code === 0;
      message = result.message;
    }
    if (!success && (message === undefined || message === null)) {
      message = t('Error');
    }

    setProcessing(false);

    if (success) {
      close();
    } else {
      throw new Error(message!);
    }
  }, [propsOnOk]);

  return (
    <Dialog
      visible={visible}
      v2
      width={'auto'}
      onClose={close}
      cache={false}
      onCancel={close}
      closeMode={['close', 'mask', 'esc']}
      onOk={onOk}
      centered
      autoFocus
      okProps={{
        id: enterBtnIdRef.current,
        tabIndex: 0,
        loading: processing,
        ...(propsOkProps || {}),
      }}
      {...otherProps}
    >
      {children}
    </Dialog>
  );
};
