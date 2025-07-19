"use client";

import type { DialogProps } from "@alifd/next/types/dialog";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Modal } from "@/components/bakaui";
import { useUpdateEffect } from "react-use";

import {
  buildLogger,
  createPortalOfComponent,
  forceFocus,
  uuidv4,
} from "@/components/utils";

interface IResult {
  code?: number;
  message?: string | null;
}

export interface ISimpleOneStepDialogProps extends DialogProps {
  onOk: () => boolean | IResult | Promise<boolean | IResult>;
  title: any;
  children?: any;
}

const log = buildLogger("SimpleOneStepDialog");

const SimpleOneStepDialog = (props: ISimpleOneStepDialogProps) => {
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
        log("Force focusing");
        forceFocus(target);
      }
    }, 250);

    return () => {
      log("Stop force focusing");
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
      throw new Error("Processing");
    }

    setProcessing(true);

    const result = await Promise.resolve(propsOnOk());
    let success: boolean;
    let message: string | undefined | null;

    if (typeof result === "boolean") {
      success = result;
    } else {
      success = result.code === 0;
      message = result.message;
    }
    if (!success && (message === undefined || message === null)) {
      message = t<string>("Error");
    }

    setProcessing(false);

    if (success) {
      close();
    } else {
      throw new Error(message!);
    }
  }, [propsOnOk]);

  return (
    <Modal
      autoFocus
      centered
      v2
      cache={false}
      closeMode={["close", "mask", "esc"]}
      okProps={{
        id: enterBtnIdRef.current,
        tabIndex: 0,
        loading: processing,
        ...(propsOkProps || {}),
      }}
      visible={visible}
      width={"auto"}
      onCancel={close}
      onClose={close}
      onOk={onOk}
      {...otherProps}
    >
      {children}
    </Modal>
  );
};

SimpleOneStepDialog.show = (props: ISimpleOneStepDialogProps) =>
  createPortalOfComponent(SimpleOneStepDialog, props);

export default SimpleOneStepDialog;
