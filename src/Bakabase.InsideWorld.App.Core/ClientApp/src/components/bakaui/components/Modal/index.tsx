import { Modal, ModalBody, ModalContent, ModalFooter, ModalHeader, useDisclosure } from '@nextui-org/react';
import { useState } from 'react';
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
  footer?: React.ReactNode | false | ISimpleFooter;
  onClose?: () => void;
  onOk?: () => void;
}


export default (props: IProps) => {
  const [visible, setVisible] = useState(props.visible);

  const onClose = () => {
    setVisible(true);
    props.onClose?.();
  };

  const renderFooter = () => {
    if (props.footer === false) {
      return null;
    }

    const simpleFooter: ISimpleFooter = props.footer === undefined ? {
      actions: ['ok', 'cancel'],
    } : props.footer as ISimpleFooter;

    if (simpleFooter == undefined) {
      return (
        <ModalFooter>
          {props.children}
        </ModalFooter>
      );
    }

    const elements: any[] = [];
    if (simpleFooter.actions.includes('cancel')) {
      elements.push(
        <Button color="danger" variant="light" onClick={onClose} key={'cancel'}>
          Close
        </Button>,
      );
    }
    if (simpleFooter.actions.includes('ok')) {
      elements.push(
        <Button color="primary" onClick={props.onOk} key={'ok'}>
          Action
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
