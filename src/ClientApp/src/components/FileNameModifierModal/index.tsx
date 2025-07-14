import React from 'react';
import { Modal } from '../bakaui';
import { useTranslation } from 'react-i18next';
import { DestroyableProps } from '../bakaui/types';
import FileNameModifier from '../FileNameModifier';
import BetaChip from '../Chips/BetaChip';

export interface FileNameModificationResult {
  originalPath: string;
  modifiedPath: string;
  originalFileName: string;
  modifiedFileName: string;
  commonPrefix: string;
  originalRelative: string;
  modifiedRelative: string;
}

interface FileNameModifierModalProps extends DestroyableProps {
  onClose?: () => void;
  initialFilePaths?: string[];
}

const FileNameModifierModal: React.FC<FileNameModifierModalProps> = ({ onClose, initialFilePaths = [] }) => {
  const { t } = useTranslation();

  return (
    <Modal defaultVisible onClose={onClose} title={(
      <div className="flex items-center gap-1">
        {t('FileNameModifier.Title')}
        <BetaChip />
      </div>
    )} footer={null} size='xl'>
      <FileNameModifier 
        initialFilePaths={initialFilePaths}
        onClose={onClose}
      />
    </Modal>
  );
};

export default FileNameModifierModal; 