import { Modal, ModalBody, ModalContent, ModalFooter, ModalHeader } from '@nextui-org/react';
import { useEffect, useState } from 'react';
import type { ModalProps } from '@nextui-org/modal/dist/modal';
import { useTranslation } from 'react-i18next';
import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';

interface ISimpleFooter {
  actions: ('ok' | 'cancel')[];
  okProps?: ButtonProps;
  cancelProps?: ButtonProps;
}

interface IProps {
  title?: any;
  children?: any;
  visible?: boolean;
  footer?: any | boolean | ISimpleFooter;
  onClose?: () => void;
  onOk?: () => void;
  size?: 'sm' | 'md' | 'lg' | 'xl' | 'full';
}


export default (props: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(props.visible);

  const [size, setSize] = useState<ModalProps['size']>();

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
    setVisible(true);
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
      elements.push(
        <Button color="danger" variant="light" onClick={onClose} key={'cancel'}>
          {t('Close')}
        </Button>,
      );
    }
    if (simpleFooter.actions.includes('ok')) {
      elements.push(
        <Button color="primary" onClick={props.onOk} key={'ok'}>
          {t('Confirm')}
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
    <Modal
      isOpen={props.visible ?? visible}
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
    </Modal>
  );
};
