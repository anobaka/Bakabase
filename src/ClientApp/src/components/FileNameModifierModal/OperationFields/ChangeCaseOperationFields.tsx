import React from 'react';
import { Select } from '../../bakaui';
import { CaseTypeOptions } from '../OperationCard';

const ChangeCaseOperationFields: React.FC<any> = ({ operation, t, onChange }) => (
  <Select
    dataSource={CaseTypeOptions.map(opt => ({ label: t(opt.label), value: opt.value }))}
    selectedKeys={[operation.caseType?.toString() || '']}
    onSelectionChange={keys => {
      const key = parseInt(Array.from(keys)[0] as string);
      if (key !== operation.caseType) {
        onChange({ ...operation, caseType: key });
      }
    }}
    label={t('FileNameModifier.Label.CaseType')}
    size="sm"
    isRequired={!operation.caseType}
    className="w-[180px]"
    placeholder={t('FileNameModifier.Placeholder.CaseType')}
  />
);

export default ChangeCaseOperationFields; 