import React, { useState } from 'react';
import BApi from '@/sdk/BApi';
import { Button, Textarea, Notification } from '@/components/bakaui';

const defaultPaths = [
  'C:/test/abc.txt',
  'C:/test/def.txt',
  'C:/test/ghi.txt',
];

const defaultOperations = [
  {
    target: 1,
    operation: 1, // Insert
    position: 1,
    positionIndex: 0,
    targetText: '',
    text: 'PRE_',
    deleteCount: 0,
    deleteStartPosition: 0,
    caseType: 1,
    dateTimeFormat: '',
    alphabetStartChar: 'A',
    alphabetCount: 0,
    replaceEntire: false,
  },
];

const FileNameModifierTest: React.FC = () => {
  const [filePaths, setFilePaths] = useState(defaultPaths.join('\n'));
  const [operations, setOperations] = useState(defaultOperations);
  const [previewResult, setPreviewResult] = useState<string[]>([]);
  const [modifyResult, setModifyResult] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);

  const handlePreview = async () => {
    setLoading(true);
    try {
      const r = await BApi.fileNameModifier.previewFileNameModification({
        filePaths: filePaths.split('\n').map(f => f.trim()).filter(Boolean),
        operations,
      });
      setPreviewResult(r.data ?? []);
    } catch (e) {
      Notification.error({ content: '预览失败' });
    } finally {
      setLoading(false);
    }
  };

  const handleModify = async () => {
    setLoading(true);
    try {
      const r = await BApi.fileNameModifier.modifyFileNames({
        filePaths: filePaths.split('\n').map(f => f.trim()).filter(Boolean),
        operations,
      });
      setModifyResult(r.data??[]);
      Notification.success({ content: '修改成功' });
    } catch (e) {
      Notification.error({ content: '修改失败' });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ padding: 24 }}>
      <h2>FileNameModifier 测试</h2>
      <Textarea
        value={filePaths}
        onChange={e => setFilePaths(e.target.value)}
        rows={5}
        style={{ width: 400, marginBottom: 12 }}
      />
      <div style={{ marginBottom: 12 }}>
        <Button onClick={handlePreview} isLoading={loading} style={{ marginRight: 8 }}>预览</Button>
        <Button onClick={handleModify} isLoading={loading} variant="solid">执行修改</Button>
      </div>
      {previewResult.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <div>预览结果：</div>
          <ul>
            {previewResult.map((name, idx) => (
              <li key={idx}>{filePaths.split('\n')[idx]} → {name}</li>
            ))}
          </ul>
        </div>
      )}
      {modifyResult.length > 0 && (
        <div>
          <div>修改结果：</div>
          <ul>
            {modifyResult.map((r, idx) => (
              <li key={idx} style={{ color: r.success ? 'green' : 'red' }}>
                {r.oldPath} → {r.newPath} {r.success ? '✔' : `✗ (${r.error})`}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
};

export default FileNameModifierTest; 