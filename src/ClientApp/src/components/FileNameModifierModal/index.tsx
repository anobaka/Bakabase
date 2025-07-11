import React, { useState } from 'react';
import { Modal, Input, Button, Textarea, Notification, Divider } from '../bakaui';
import OperationCard from './OperationCard';
import PreviewList from './PreviewList';
import { useFileNameModifier } from './useFileNameModifier';
import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from '@/sdk/Api';
// 1. 直接在本文件声明 defaultOperation 并导出 FileNameModificationResult 类型
export interface FileNameModificationResult {
  originalPath: string;
  modifiedPath: string;
  originalFileName: string;
  modifiedFileName: string;
  commonPrefix: string;
  originalRelative: string;
  modifiedRelative: string;
}

const defaultOperation: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation = {
  target: 2, // FileNameWithoutExtension
  operation: 1,
  position: 1,
  positionIndex: 0,
  targetText: '',
  text: '',
  deleteCount: 0,
  deleteStartPosition: 0,
  caseType: 1,
  dateTimeFormat: '',
  alphabetStartChar: 'A',
  alphabetCount: 0,
  replaceEntire: false,
};
import { useTranslation } from 'react-i18next';
import { DestroyableProps } from '../bakaui/types';
import BApi from '@/sdk/BApi';
import { useEffect, useRef } from 'react';
import { AiOutlineEdit, AiOutlineEye, AiOutlineEyeInvisible, AiOutlinePlusCircle } from 'react-icons/ai';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

interface FileNameModifierModalProps extends DestroyableProps {
  onClose: () => void;
  initialFilePaths?: string[];
}

// 校验函数，返回 i18n key
function validateOperation(op: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation): string {
  if (!op.target) return 'FileNameModifier.Error.TargetRequired';
  if (!op.operation) return 'FileNameModifier.Error.OperationTypeRequired';
  switch (op.operation) {
    case 1: // Insert
      if (!op.text && !op.targetText) return 'FileNameModifier.Error.InsertTextRequired';
      break;
    case 2: // AddDateTime
      if (!op.dateTimeFormat) return 'FileNameModifier.Error.DateTimeFormatRequired';
      break;
    case 3: // Delete
      if (op.deleteCount == null || op.deleteStartPosition == null || !op.position) return 'FileNameModifier.Error.DeleteParamsRequired';
      break;
    case 4: // Replace
      if (!op.text && !op.targetText) return 'FileNameModifier.Error.ReplaceTextRequired';
      break;
    case 5: // ChangeCase
      if (!op.caseType) return 'FileNameModifier.Error.CaseTypeRequired';
      break;
    case 6: // AddAlphabetSequence
      if (!op.alphabetStartChar || op.alphabetCount == null) return 'FileNameModifier.Error.AlphabetParamsRequired';
      break;
    case 7: // Reverse
      break;
    default:
      return 'FileNameModifier.Error.UnknownOperationType';
  }
  return '';
}

const FileNameModifierModal: React.FC<FileNameModifierModalProps> = ({ onClose, initialFilePaths = [] }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    operations,
    setOperations,
    filePaths,
    setFilePaths,
    previewResults,
    setPreviewResults,
    error,
    setError,
    // ...其它方法
  } = useFileNameModifier(initialFilePaths);

  // 折叠/展开状态
  const [expandedItems, setExpandedItems] = useState<Set<number>>(new Set());
  const [showTextarea, setShowTextarea] = useState(false);
  const [showFullPaths, setShowFullPaths] = useState(false);
  const [modifying, setModifying] = useState(false);

  // 操作项增删改、移动、复制
  const handleOperationChange = (idx: number, op) => {
    setOperations(ops => ops.map((item, i) => (i === idx ? op : item)));
  };
  const handleOperationDelete = (idx: number) => {
    setOperations(ops => ops.filter((_, i) => i !== idx));
  };
  const handleOperationMoveUp = (idx: number) => {
    setOperations(ops => {
      if (idx === 0) return ops;
      const next = [...ops];
      if (next[idx] && next[idx - 1]) {
        const temp = { ...next[idx - 1] } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
        next[idx - 1] = { ...next[idx] } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
        next[idx] = temp;
      }
      return next;
    });
  };
  const handleOperationMoveDown = (idx: number) => {
    setOperations(ops => {
      if (idx === ops.length - 1) return ops;
      const next = [...ops];
      if (next[idx] && next[idx + 1]) {
        const temp = { ...next[idx + 1] } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
        next[idx + 1] = { ...next[idx] } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
        next[idx] = temp;
      }
      return next;
    });
  };
  const handleOperationCopy = (idx: number) => {
    setOperations(ops => {
      const next = [...ops];
      const copy = { ...ops[idx] } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
      next.splice(idx + 1, 0, copy);
      return next;
    });
  };

  // 文件路径输入
  const handleConfirmPaths = () => {
    const paths = filePaths.join('\n').split('\n').map(f => f.trim()).filter(Boolean);
    setFilePaths(paths);
    setShowTextarea(false);
  };
  const handleShowFileListEdit = () => {
    setShowTextarea(true);
  };
  // 新增：去重、清空、粘贴
  const handleDeduplicatePaths = () => {
    setFilePaths(paths => Array.from(new Set(paths.map(f => f.trim()).filter(Boolean))));
  };
  // 预览区主行/展开
  const commonPrefix = '';// TODO: 用 utils.detectCommonPrefix(previewResults.map(r => r.originalPath))

  const debounceTimer = useRef<NodeJS.Timeout | null>(null);
  useEffect(() => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current); 

    if (filePaths.length === 0) {
      return;
    }
    // 只用合法操作预览
    const validOperations = operations.filter(op => !validateOperation(op));
    debounceTimer.current = setTimeout(() => {
      (async () => {
        try {
          setError('');
          const rsp = await BApi.fileNameModifier.previewFileNameModification({
            filePaths,
            operations: validOperations,
          });
          let modifiedPaths: string[] = rsp.data ?? [];
          // 构建 previewResults
          const results = filePaths.map((originalPath, i) => {
            const modifiedPath = modifiedPaths[i] || originalPath;
            const getFileName = (p: string) => p.split(/[\\/]/).pop() || '';
            return {
              originalPath,
              modifiedPath,
              originalFileName: getFileName(originalPath),
              modifiedFileName: getFileName(modifiedPath),
              commonPrefix: '',
              originalRelative: '',
              modifiedRelative: '',
            };
          });
          setPreviewResults(results);
        } catch (e: any) {
          setError(e?.message || t('FileNameModifier.PreviewFailed'));
        }
      })();
    }, 300);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filePaths, operations]);

  return (
    <Modal defaultVisible onClose={onClose} title={t('FileNameModifier.Title')} footer={null} size='xl'>
      <div className="flex flex-col min-h-0 grow md:flex-row gap-4">
        {/* 左侧：操作配置区域 */}
        <div className="flex-1 flex flex-col min-w-0">
          <h5 className="font-semibold mb-2">
            {t('FileNameModifier.OperationsList')}
          </h5>
          <div className="flex-1 overflow-y-auto rounded p-2">
            {operations.map((op, idx) => (
              <OperationCard
                key={idx}
                operation={op}
                index={idx}
                errors={validateOperation(op) ? t(validateOperation(op)) : ''}
                onChange={op2 => handleOperationChange(idx, op2)}
                onDelete={() => handleOperationDelete(idx)}
                onMoveUp={idx > 0 ? () => handleOperationMoveUp(idx) : undefined}
                onMoveDown={idx < operations.length - 1 ? () => handleOperationMoveDown(idx) : undefined}
                onCopy={() => handleOperationCopy(idx)}
                aria-label={t('FileNameModifier.OperationCardAria', { index: idx + 1 })}
              />
            ))}
            <Button
              onClick={() => setOperations(ops => [...ops, { ...defaultOperation }])}
              // size="sm"
              variant="light"
              className="w-full mt-2"
              aria-label={t('FileNameModifier.AddOperation')}
            >
              <AiOutlinePlusCircle className='text-lg' />
              {t('FileNameModifier.AddOperation')}
            </Button>
          </div>
          {/* 操作按钮 */}
          <div className="mt-4">
            <div className="flex gap-2">
              <Button variant="solid" aria-label={t('FileNameModifier.ExecuteModification')}
                isLoading={modifying}
                onClick={async () => {
                  const validOperations = operations.filter(op => !validateOperation(op));
                  if (validOperations.length === 0) {
                    setError(t('FileNameModifier.Error.NoValidOperation'));
                    return;
                  }
                  try {
                    setModifying(true);
                    setError('');
                    const rsp = await BApi.fileNameModifier.modifyFileNames({
                      filePaths,
                      operations: validOperations,
                    });
                    const result = rsp.data ?? [];
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t('FileNameModifier.ModificationResult'),
                      onClose: () => {},
                      size: 'xl',
                      footer: {
                        actions: ['cancel']
                      },
                      children: (
                        <>
                          <div className="mb-2">
                            <span className="text-green-600 font-semibold mr-4">{t('FileNameModifier.ModificationSuccessCount', { count: result.filter(r => r.success).length })}</span>
                            <span className="text-red-600 font-semibold">{t('FileNameModifier.ModificationFailCount', { count: result.filter(r => !r.success).length })}</span>
                          </div>
                          {result.filter(r => !r.success).length > 0 && (
                            <div className="max-h-48 overflow-y-auto border rounded p-2 bg-background border border-default">
                              <div className="font-semibold mb-1">{t('FileNameModifier.ModificationFailList')}</div>
                              <ul className="text-xs">
                                {result.filter(r => !r.success).map(item => (
                                  <li key={item.oldPath} className="mb-1">
                                    <span className="text-foreground">{item.oldPath}</span>
                                    <span className="text-red-500 ml-2">{t('FileNameModifier.ModificationFailReason')}: {item.error}</span>
                                  </li>
                                ))}
                              </ul>
                            </div>
                          )}
                        </>
                      ),
                    });
                    setModifying(false);
                  } catch (e: any) {
                    setModifying(false);
                    setError(e?.message || t('FileNameModifier.ModificationFailed'));
                  }
                }}
              >
                {t('FileNameModifier.ExecuteModification')}
              </Button>
            </div>
            {error && <div className="text-red-500 text-xs mt-2">{error}</div>}
          </div>
        </div>
        {/* 分隔线 */}
        <Divider orientation="vertical" />
        {/* 右侧：文件路径输入/预览区域 */}
        <div className="flex-1 flex flex-col min-w-0">
          <div className="mb-2 flex justify-between items-center">
            <h5 className="font-semibold mb-0 flex items-center gap-1">
              {showTextarea ? t('FileNameModifier.EditFileList') : t('FileNameModifier.PreviewResults')}
              {!showTextarea && (
                <Button
                  size="sm"
                  variant="light"
                  onClick={() => setShowFullPaths(!showFullPaths)}
                  className="text-xs"
                  aria-label={showFullPaths ? t('FileNameModifier.HideFullPaths') : t('FileNameModifier.ShowFullPaths')}
                >
                  {showFullPaths ? <AiOutlineEyeInvisible className='text-base' /> : <AiOutlineEye className='text-base' />}
                  {showFullPaths ? t('FileNameModifier.HideFullPaths') : t('FileNameModifier.ShowFullPaths')}
                </Button>
              )}
            </h5>
            {!showTextarea && (
              <Button
                onClick={handleShowFileListEdit}
                size="sm"
                variant="light"
                aria-label={t('FileNameModifier.EditFileList')}
              >
                <AiOutlineEdit className='text-base' />
                {t('FileNameModifier.EditFileList')}
              </Button>
            )}
          </div>
          <div className="flex-1 rounded min-h-0">
            {showTextarea ? (
              <div className="h-full flex flex-col min-h-0">
                <Textarea
                  value={filePaths.join('\n')}
                  onValueChange={e => setFilePaths(e.split('\n'))}
                  placeholder={t('FileNameModifier.FilePathsPlaceholder')}
                  aria-label={t('FileNameModifier.FilePathsTextarea')}
                  minRows={10}
                  maxRows={15}
                />
                <div className="mt-2 flex flex-wrap gap-2">
                  <Button onClick={handleConfirmPaths} variant="solid" color='primary' size="sm" aria-label={t('FileNameModifier.ConfirmPaths')}>
                    {t('FileNameModifier.ConfirmPaths')}
                  </Button>
                  <Button onClick={handleDeduplicatePaths} color='secondary'
                    size="sm" variant="light" aria-label={t('FileNameModifier.Deduplicate')}>{t('FileNameModifier.Deduplicate')}</Button>
                  <Button
                    onClick={() => setShowTextarea(false)}
                    size="sm"
                    variant="flat"
                    aria-label={t('FileNameModifier.Cancel')}
                  >
                    {t('FileNameModifier.Cancel')}
                  </Button>
                </div>
              </div>
            ) : (
              <PreviewList
                results={previewResults}
                showFullPaths={showFullPaths}
                commonPrefix={commonPrefix}
              />
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
};

export default FileNameModifierModal; 