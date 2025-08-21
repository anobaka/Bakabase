"use client";

import type { ModalProps as NextUIModalProps } from "@heroui/react";
import type { LegacyRef } from "react";
import type { ButtonProps } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";

import {
  Modal as NextUiModal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
} from "@heroui/react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import _ from "lodash";

import { Button } from "@/components/bakaui";

interface ISimpleFooter {
  actions: ("ok" | "cancel")[];
  okProps?: ButtonProps & { ref?: LegacyRef<HTMLButtonElement> };
  cancelProps?: ButtonProps & { ref?: LegacyRef<HTMLButtonElement> };
}

type Size = "sm" | "md" | "lg" | "xl" | "2xl" | "full";

export interface ModalProps
  extends DestroyableProps,
    Omit<NextUIModalProps, "children"> {
  title?: any;
  children?: any;
  defaultVisible?: boolean;
  visible?: boolean;
  footer?: any | boolean | ISimpleFooter;
  onClose?: () => void;
  onOk?: () => any;
  size?: Size;
  okProps?: ButtonProps;
}

const Modal = (props: ModalProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(props.defaultVisible ?? props.visible);

  const [size, setSize] = useState<NextUIModalProps["size"]>();

  const [okLoading, setOkLoading] = useState(false);
  const domRef = useRef<HTMLElement | null>(null);
  const isOpen = props.visible != undefined ? props.visible : visible;

  const autoFocused = useRef(false);

  useEffect(() => {
    // console.log('modal initialized');
  }, []);

  useEffect(() => {
    switch (props.size) {
      case "sm":
        setSize("sm");
        break;
      case "md":
        setSize("lg");
        break;
      case "lg":
        setSize("2xl");
        break;
      case "xl":
        setSize("5xl");
        break;
      case "full":
        setSize("full");
        break;
      default:
        setSize(undefined);
    }
  }, [props.size]);

  const onClose = () => {
    setVisible(false);
    props.onClose?.();
  };

  const renderFooter = () => {
    if (props.footer === false) {
      return null;
    }

    const simpleFooter: ISimpleFooter | undefined =
      props.footer === undefined || props.footer === true
        ? {
            actions: ["ok", "cancel"],
          }
        : props.footer?.["actions"]
          ? (props.footer as ISimpleFooter)
          : undefined;

    if (simpleFooter == undefined) {
      return <ModalFooter>{props.footer}</ModalFooter>;
    }

    const elements: any[] = [];

    if (simpleFooter.actions.includes("cancel")) {
      const { children, ref, ...otherProps } = simpleFooter.cancelProps || {};

      elements.push(
        <Button
          key={"cancel"}
          ref={ref}
          color="danger"
          variant="light"
          onPress={onClose}
          {...otherProps}
        >
          {children ?? t<string>("Close")}
        </Button>,
      );
    }
    if (simpleFooter.actions.includes("ok")) {
      const { children, ref, autoFocus, ...otherProps } = {
        ...simpleFooter.okProps,
        ...props.okProps,
      };

      elements.push(
        <Button
          key={"ok"}
          ref={(r) => {
            if (typeof ref === "function") {
              ref(r);
            } else {
              if (ref && typeof ref === "object" && "current" in ref) {
                (
                  ref as React.MutableRefObject<HTMLButtonElement | null>
                ).current = r;
              }
            }
            if (r && !autoFocused.current && autoFocus) {
              autoFocused.current = true;
              r.focus();
            }
          }}
          color="primary"
          isLoading={okLoading}
          onPress={async () => {
            const r = props.onOk?.();

            if (r instanceof Promise) {
              setOkLoading(true);
              try {
                await r;
                onClose();
              } catch (e) {
              } finally {
                setOkLoading(false);
              }
            } else {
              onClose();
            }
            // console.log('onok sync finish');
          }}
          {...otherProps}
        >
          {children ?? t<string>("Confirm")}
        </Button>,
      );
    }

    return <ModalFooter>{elements}</ModalFooter>;
  };

  const classNames: ModalProps["classNames"] = _.merge({}, props.classNames);

  if (props.size == "2xl") {
    const propsBaseClassName = props.classNames?.["base"] ?? "";

    classNames["base"] = propsBaseClassName
      ? `max-w-[80vw] ${propsBaseClassName}`
      : "max-w-[80vw]";
  }

  return (
    <NextUiModal
      ref={r => {
        // console.log(domRef.current, r);
        if (domRef.current && !r && !isOpen) {
          // closed
          // there is no such a method likes onDestroyed in nextui v2.3.0
          // console.log('after close', props.onDestroyed);
          props.onDestroyed?.();
        }
        domRef.current = r;
      }}
      className={props.className}
      classNames={classNames}
      size={size}
      style={props.style}
      isOpen={isOpen}
      // onOpenChange={v => console.log('123456', v)}
      onClose={onClose}
      scrollBehavior={'inside'}
      // isDismissable={props.isDismissable ?? false}
      isKeyboardDismissDisabled={props.isKeyboardDismissDisabled ?? true}
    >
      <ModalContent>
        <ModalHeader className="flex flex-col gap-1">{props.title}</ModalHeader>
        <ModalBody>{props.children}</ModalBody>
        {renderFooter()}
      </ModalContent>
    </NextUiModal>
  );
};

export default Modal;
