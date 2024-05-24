import type { ModalProps as NextUIModalProps } from '@nextui-org/react';
import { Modal as NextUiModal, ModalBody, ModalContent, ModalFooter, ModalHeader } from '@nextui-org/react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';

interface ISimpleFooter {
  actions: ('ok' | 'cancel')[];
  okProps?: ButtonProps;
  cancelProps?: ButtonProps;
}

export interface ModalProps extends DestroyableProps, Omit<NextUIModalProps, 'children'>{
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
      const { children, ref, ...otherProps } = simpleFooter.cancelProps || {};
      elements.push(
        <Button
          color="danger"
          variant="light"
          onClick={onClose}
          key={'cancel'}
          {...otherProps}
        >
          {children ?? t('Close')}
        </Button>,
      );
    }
    if (simpleFooter.actions.includes('ok')) {
      const { children, ref, ...otherProps } = simpleFooter.okProps || {};
      elements.push(
        <Button
          isLoading={okLoading}
          color="primary"
          onClick={async () => {
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

Modal.show = (props: ModalProps) => createPortalOfComponent(Modal, { ...props, defaultVisible: true });

export default Modal;
