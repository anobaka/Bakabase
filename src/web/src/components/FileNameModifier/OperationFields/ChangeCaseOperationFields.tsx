'use client';

import React from 'react';
import { Select } from '../../bakaui';
import {
  FileNameModifierCaseType,
  fileNameModifierCaseTypes,
} from '@/sdk/constants';

const CaseTypeOptions = fileNameModifierCaseTypes.map(opt => ({ label: 'FileNameModifier.CaseType.' + FileNameModifierCaseType[opt.value], value: opt.value }));

const ChangeCaseOperationFields: React.FC<any> = ({ operation, t, onChange }) => (
  <Select
    dataSource={CaseTypeOptions.map(opt => ({ label: t<string>(opt.label), value: opt.value }))}
    selectedKeys={[operation.caseType?.toString() || '']}
    onSelectionChange={keys => {
      const key = parseInt(Array.from(keys)[0] as string);
      if (key !== operation.caseType) {
        onChange({ ...operation, caseType: key });
      }
    }}
    label={t<string>('FileNameModifier.Label.CaseType')}
    size="sm"
    isRequired={!operation.caseType}
    className="w-[180px]"
    placeholder={t<string>('FileNameModifier.Placeholder.CaseType')}
  />
);

export default ChangeCaseOperationFields; 