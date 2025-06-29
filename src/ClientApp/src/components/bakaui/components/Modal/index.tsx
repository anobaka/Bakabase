import type { ModalProps as NextUIModalProps } from '@heroui/react';
import { Modal as NextUiModal, ModalBody, ModalContent, ModalFooter, ModalHeader } from '@heroui/react';
import type { LegacyRef } from 'react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';

interface ISimpleFooter {
  actions: ('ok' | 'cancel')[];
  okProps?: ButtonProps & { ref?: LegacyRef<HTMLButtonElement> };
  cancelProps?: ButtonProps & { ref?: LegacyRef<HTMLButtonElement> };
}

export interface ModalProps extends DestroyableProps, Omit<NextUIModalProps, 'children'> {
  title?: any;
  children?: any;
  defaultVisible?: boolean;
  visible?: boolean;
  footer?: any | boolean | ISimpleFooter;
  onClose?: () => void;
  onOk?: () => any;
  size?: 'sm' | 'md' | 'lg' | 'xl' | 'full';
}


const Modal = (props: ModalProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(props.defaultVisible ?? props.visible);

  const [size, setSize] = useState<NextUIModalProps['size']>();

  const [okLoading, setOkLoading] = useState(false);
  const domRef = useRef<HTMLElement | null>(null);
  const isOpen = props.visible != undefined ? props.visible : visible;

  const autoFocused = useRef(false);

  useEffect(() => {
    // console.log('modal initialized');
  }, []);

  useEffect(() => {
    switch (props.size) {
      case 'sm':
        setSize('sm');
        break;
      case 'md':
        setSize('lg');
        break;
      case 'lg':
        setSize('2xl');
        break;
      case 'xl':
        setSize('5xl');
        break;
      case 'full':
        setSize('full');
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

    const simpleFooter: ISimpleFooter | undefined = (props.footer === undefined || props.footer === true) ? {
      actions: ['ok', 'cancel'],
    } : props.footer?.['actions'] ? props.footer as ISimpleFooter : undefined;

    if (simpleFooter == undefined) {
      return (
        <ModalFooter>
          {props.footer}
        </ModalFooter>
      );
    }

    const elements: any[] = [];
    if (simpleFooter.actions.includes('cancel')) {
      const {
        children,
        ref,
        ...otherProps
      } = simpleFooter.cancelProps || {};
      elements.push(
        <Button
          color="danger"
          variant="light"
          onPress={onClose}
          key={'cancel'}
          ref={ref}
          {...otherProps}
        >
          {children ?? t('Close')}
        </Button>,
      );
    }
    if (simpleFooter.actions.includes('ok')) {
      const {
        children,
        ref,
        autoFocus,
        ...otherProps
      } = simpleFooter.okProps || {};
      elements.push(
        <Button
          isLoading={okLoading}
          color="primary"
          onPress={async () => {
            const r = props.onOk?.();
            if (r instanceof Promise) {
              setOkLoading(true);
              try {
                const result = await r;
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
          key={'ok'}
          ref={r => {
            if (typeof ref === 'function') {
              ref(r);
            } else {
              if (ref && typeof ref === 'object' && 'current' in ref) {
                (ref as React.MutableRefObject<HTMLButtonElement | null>).current = r;
              }
            }
            if (r && !autoFocused.current && autoFocus) {
              autoFocused.current = true;
              r.focus();
            }
          }}
          {...otherProps}
        >
          {children ?? t('Confirm')}
        </Button>,
      );
    }
    return (
      <ModalFooter>
        {elements}
      </ModalFooter>
    );
  };

  return (
    <NextUiModal
      isOpen={isOpen}
      // onOpenChange={v => console.log('123456', v)}
      onClose={onClose}
      scrollBehavior={'inside'}
      isDismissable={props.isDismissable ?? false}
      isKeyboardDismissDisabled={props.isKeyboardDismissDisabled ?? true}
      size={size}
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
      classNames={props.classNames}
      className={props.className}
      style={props.style}
    >
      <ModalContent>
        <ModalHeader className="flex flex-col gap-1">{props.title}</ModalHeader>
        <ModalBody>
          {props.children}
        </ModalBody>
        {renderFooter()}
      </ModalContent>
    </NextUiModal>
  );
};

Modal.show = (props: ModalProps) => createPortalOfComponent(Modal, {
  ...props,
  defaultVisible: true,
});

export default Modal;
