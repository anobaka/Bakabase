import React, { useState } from 'react';
import { Modal, Input, Button, Table, Textarea, Notification } from '../bakaui';
import BApi from '../../sdk/BApi';

// 直接定义类型
interface FileNameModifierOperation {
  Target: number;
  Operation: number;
  Position: number;
  PositionIndex: number;
  TargetText?: string;
  Text?: string;
  DeleteCount: number;
  DeleteStartPosition: number;
  CaseType: number;
  DateTimeFormat?: string;
  AlphabetStartChar: string;
  AlphabetCount: number;
  ReplaceEntire: boolean;
}

interface FileNameModifierModalProps {
  open: boolean;
  onClose: () => void;
}

const defaultOperation: FileNameModifierOperation = {
  Target: 1 as number,
  Operation: 1 as number,
  Position: 1 as number,
  PositionIndex: 0,
  TargetText: '',
  Text: '',
  DeleteCount: 0,
  DeleteStartPosition: 0,
  CaseType: 1 as number,
  DateTimeFormat: '',
  AlphabetStartChar: 'A',
  AlphabetCount: 0,
  ReplaceEntire: false,
};

// 操作类型枚举
const OperationType = {
  Insert: 1,
  AddDateTime: 2,
  Delete: 3,
  Replace: 4,
  ChangeCase: 5,
  AddAlphabetSequence: 6,
  Reverse: 7,
};

const CaseTypeOptions = [
  { label: 'TitleCase', value: 1 },
  { label: 'UpperCase', value: 2 },
  { label: 'LowerCase', value: 3 },
  { label: 'CamelCase', value: 4 },
  { label: 'PascalCase', value: 5 },
];

const FileNameModifierModal: React.FC<FileNameModifierModalProps> = ({ open, onClose }) => {
  const [filePaths, setFilePaths] = useState<string>('');
  const [operations, setOperations] = useState<FileNameModifierOperation[]>([defaultOperation]);
  const [previewResult, setPreviewResult] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);

  // 简化：只做基础操作编辑，实际可拆分为单独组件
  const handleOperationChange = (idx: number, key: keyof FileNameModifierOperation, value: any) => {
    setOperations(ops => {
      const next = [...ops];
      next[idx] = { ...next[idx], [key]: value };
      return next;
    });
  };

  // 校验所有操作参数
  const validateOperations = () => {
    for (const op of operations) {
      switch (op.Operation) {
        case OperationType.Insert:
          if (!op.Text) return 'Insert操作需要Text';
          break;
        case OperationType.AddDateTime:
          if (!op.DateTimeFormat) return 'AddDateTime操作需要DateTimeFormat';
          break;
        case OperationType.Delete:
          if (op.DeleteCount <= 0) return 'Delete操作需要DeleteCount>0';
          if (op.DeleteStartPosition < 0) return 'Delete操作需要DeleteStartPosition>=0';
          break;
        case OperationType.Replace:
          if (!op.Text && !op.TargetText) return 'Replace操作需要Text或TargetText';
          break;
        case OperationType.AddAlphabetSequence:
          if (op.AlphabetCount <= 0) return 'AddAlphabetSequence操作需要AlphabetCount>0';
          break;
        // 其它类型无需必填
      }
    }
    return '';
  };

  const handlePreview = async () => {
    setLoading(true);
    const err = validateOperations();
    if (err) {
      Notification.error({ content: err });
      setLoading(false);
      return;
    }
    try {
      const data = await BApi.fileNameModifier.preview({
        filePaths: filePaths.split('\n').map(f => f.trim()).filter(Boolean),
        operations,
      });
      setPreviewResult(data);
    } catch (e) {
      Notification.error({ content: '预览失败' });
    } finally {
      setLoading(false);
    }
  };

  const handleModify = async () => {
    setLoading(true);
    const err = validateOperations();
    if (err) {
      Notification.error({ content: err });
      setLoading(false);
      return;
    }
    try {
      const data = await BApi.fileNameModifier.modify({
        filePaths: filePaths.split('\n').map(f => f.trim()).filter(Boolean),
        operations,
      });
      Notification.success({ content: '修改成功' });
      setPreviewResult(data);
    } catch (e) {
      Notification.error({ content: '修改失败' });
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal isOpen={open} onClose={onClose} title="批量文件名修改" footer={null}>
      <div style={{ marginBottom: 16 }}>
        <Textarea
          value={filePaths}
          onChange={e => setFilePaths(e.target.value)}
          placeholder="每行一个文件完整路径"
          rows={5}
        />
      </div>
      <div style={{ marginBottom: 16 }}>
        <div>操作列表：</div>
        {operations.map((op, idx) => (
          <div key={idx} style={{ display: 'flex', gap: 8, marginBottom: 8, alignItems: 'center' }}>
            <Input
              value={op.Operation}
              onChange={e => handleOperationChange(idx, 'Operation', Number(e.target.value))}
              as="select"
              style={{ width: 120 }}
            >
              <option value={OperationType.Insert}>Insert</option>
              <option value={OperationType.AddDateTime}>AddDateTime</option>
              <option value={OperationType.Delete}>Delete</option>
              <option value={OperationType.Replace}>Replace</option>
              <option value={OperationType.ChangeCase}>ChangeCase</option>
              <option value={OperationType.AddAlphabetSequence}>AddAlphabetSequence</option>
              <option value={OperationType.Reverse}>Reverse</option>
            </Input>
            {/* Insert/Replace 需要 Text */}
            {(op.Operation === OperationType.Insert || op.Operation === OperationType.Replace) && (
              <Input
                value={op.Text}
                onChange={e => handleOperationChange(idx, 'Text', e.target.value)}
                placeholder="Text"
                style={{ width: 100 }}
              />
            )}
            {/* Replace/Insert 需要 TargetText */}
            {(op.Operation === OperationType.Replace || op.Operation === OperationType.Insert) && (
              <Input
                value={op.TargetText}
                onChange={e => handleOperationChange(idx, 'TargetText', e.target.value)}
                placeholder="TargetText"
                style={{ width: 100 }}
              />
            )}
            {/* AddDateTime 需要 DateTimeFormat */}
            {op.Operation === OperationType.AddDateTime && (
              <Input
                value={op.DateTimeFormat}
                onChange={e => handleOperationChange(idx, 'DateTimeFormat', e.target.value)}
                placeholder="DateTimeFormat"
                style={{ width: 120 }}
              />
            )}
            {/* Delete 需要 DeleteCount/DeleteStartPosition */}
            {op.Operation === OperationType.Delete && (
              <>
                <Input
                  value={op.DeleteCount}
                  type="number"
                  onChange={e => handleOperationChange(idx, 'DeleteCount', Number(e.target.value))}
                  placeholder="DeleteCount"
                  style={{ width: 80 }}
                />
                <Input
                  value={op.DeleteStartPosition}
                  type="number"
                  onChange={e => handleOperationChange(idx, 'DeleteStartPosition', Number(e.target.value))}
                  placeholder="DeleteStartPos"
                  style={{ width: 80 }}
                />
              </>
            )}
            {/* ChangeCase 需要 CaseType */}
            {op.Operation === OperationType.ChangeCase && (
              <Input
                as="select"
                value={op.CaseType}
                onChange={e => handleOperationChange(idx, 'CaseType', Number(e.target.value))}
                style={{ width: 120 }}
              >
                {CaseTypeOptions.map(opt => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </Input>
            )}
            {/* AddAlphabetSequence 需要 AlphabetStartChar/AlphabetCount */}
            {op.Operation === OperationType.AddAlphabetSequence && (
              <>
                <Input
                  value={op.AlphabetStartChar}
                  onChange={e => handleOperationChange(idx, 'AlphabetStartChar', e.target.value)}
                  placeholder="StartChar"
                  style={{ width: 60 }}
                  maxLength={1}
                />
                <Input
                  value={op.AlphabetCount}
                  type="number"
                  onChange={e => handleOperationChange(idx, 'AlphabetCount', Number(e.target.value))}
                  placeholder="Count"
                  style={{ width: 60 }}
                />
              </>
            )}
            <Button onClick={() => setOperations(ops => ops.filter((_, i) => i !== idx))}>删除</Button>
          </div>
        ))}
        <Button onClick={() => setOperations(ops => [...ops, { ...defaultOperation }])}>添加操作</Button>
      </div>
      <div style={{ marginBottom: 16 }}>
        <Button onClick={handlePreview} isLoading={loading} style={{ marginRight: 8 }}>预览</Button>
        <Button onClick={handleModify} isLoading={loading} variant="solid">执行修改</Button>
      </div>
      {previewResult.length > 0 && (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={{ border: '1px solid #eee', padding: 4 }}>原文件名</th>
              <th style={{ border: '1px solid #eee', padding: 4 }}>新文件名</th>
            </tr>
          </thead>
          <tbody>
            {previewResult.map((modified, idx) => (
              <tr key={idx}>
                <td style={{ border: '1px solid #eee', padding: 4 }}>{filePaths.split('\n')[idx] || ''}</td>
                <td style={{ border: '1px solid #eee', padding: 4 }}>{modified}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Modal>
  );
};

export default FileNameModifierModal; 