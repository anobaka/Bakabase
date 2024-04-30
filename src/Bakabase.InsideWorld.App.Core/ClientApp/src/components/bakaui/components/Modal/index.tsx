import { Modal as NextUiModal, ModalBody, ModalContent, ModalFooter, ModalHeader } from '@nextui-org/react';
import { useEffect, useState } from 'react';
import type { ModalProps } from '@nextui-org/modal/dist/modal';
import { useTranslation } from 'react-i18next';
import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import PropertyDialog from '@/components/PropertyDialog';

interface ISimpleFooter {
  actions: ('ok' | 'cancel')[];
  okProps?: ButtonProps;
  cancelProps?: ButtonProps;
}

interface IProps {
  title?: any;
  children?: any;
  defaultVisible?: boolean;
  visible?: boolean;
  footer?: any | boolean | ISimpleFooter;
  onClose?: () => void;
  onOk?: () => any;
  size?: 'sm' | 'md' | 'lg' | 'xl' | 'full';
  afterClose?: any;
}


const Modal = (props: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(props.defaultVisible ?? props.visible);

  const [size, setSize] = useState<ModalProps['size']>();

  const [okLoading, setOkLoading] = useState(false);

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
    props.afterClose?.();
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
      elements.push(
        <Button color="danger" variant="light" onClick={onClose} key={'cancel'}>
          {simpleFooter.cancelProps?.children ?? t('Close')}
        </Button>,
      );
    }
    if (simpleFooter.actions.includes('ok')) {
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
        >
          {simpleFooter.okProps?.children ?? t('Confirm')}
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
      isOpen={props.visible != undefined ? props.visible : visible}
      onClose={onClose}
      scrollBehavior={'inside'}
      size={size}
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

Modal.show = (props: IProps) => createPortalOfComponent(Modal, { ...props, defaultVisible: true });

export default Modal;
